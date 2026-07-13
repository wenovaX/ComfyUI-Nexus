using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ComfyUI_Nexus.Views.Components;

public partial class LogTailView : ContentView
{
	private const string FlushOperationName = "LogTail.Flush";
	private const string ClearOperationName = "LogTail.Clear";
	private const string DispatchSource = "LOG_TAIL";
	private const int DefaultMaxRows = 100;
	private const int DefaultPrewarmRows = 50;
	private const int DefaultMaxFlushBatchSize = 40;
	private const int DefaultMaxLineLength = 420;
	private const int DefaultMaxCopyLineLength = 4000;
	private const int DefaultMaxPendingRows = 240;
	private const int DefaultFlushDebounceMs = 16;
	private const int DefaultScrollDebounceMs = 48;
	private const double DefaultAutoScrollThreshold = 28;
	private static readonly Regex PipRawProgressRegex = new(
		@"^Progress\s+(?<current>\d+)\s+of\s+(?<total>\d+)\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
	private static readonly Color RowTextColor = Color.FromArgb("#a3c4db");
	private static readonly Color RowHoverTextColor = Color.FromArgb("#dffaff");
	private static readonly Color RowHoverBackgroundColor = Color.FromArgb("#1231d8ff");
	private static readonly Color TransparentColor = Colors.Transparent;

	private readonly object _gate = new();
	private readonly List<LogTailEntry> _pendingLines = new();
	private readonly UiObjectPool<Label> _rowPool;
	private bool _flushScheduled;
	private bool _scrollScheduled;
	private bool _isPinnedToEnd = true;
	private bool _isUnloaded;
	private int _flushRequestId;
	private int _scrollRequestId;
	private long _lastPipProgressBytes = -1;
	private DateTimeOffset _lastPipProgressAt;

	public int MaxRows { get; set; } = DefaultMaxRows;
	public int PrewarmRows { get; set; } = DefaultPrewarmRows;
	public int MaxFlushBatchSize { get; set; } = DefaultMaxFlushBatchSize;
	public int MaxLineLength { get; set; } = DefaultMaxLineLength;
	public int MaxCopyLineLength { get; set; } = DefaultMaxCopyLineLength;
	public int MaxPendingRows { get; set; } = DefaultMaxPendingRows;
	public int FlushDebounceMs { get; set; } = DefaultFlushDebounceMs;
	public int ScrollDebounceMs { get; set; } = DefaultScrollDebounceMs;
	public double AutoScrollThreshold { get; set; } = DefaultAutoScrollThreshold;
	public bool AutoScroll { get; set; } = true;
	public bool AnimateScroll { get; set; }
	public Func<string, Color?>? RowColorResolver { get; set; }

	public LogTailView()
	{
		InitializeComponent();
		_rowPool = new UiObjectPool<Label>(CreateLogRow, ResetLogRow);
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	public void AppendLine(string? line)
	{
		if (_isUnloaded)
		{
			return;
		}

		lock (_gate)
		{
			var entry = FormatLine(line);
			if (entry.IsInlineProgress && _pendingLines.Count > 0 && _pendingLines[^1].IsInlineProgress)
			{
				_pendingLines[^1] = entry;
			}
			else
			{
				_pendingLines.Add(entry);
			}

			TrimPendingLinesLocked();

			if (_flushScheduled)
			{
				return;
			}

			_flushScheduled = true;
			_flushRequestId++;
		}

		_ = FlushPendingLinesWhenIdleAsync();
	}

	public void SetLines(IEnumerable<string> lines)
	{
		if (_isUnloaded)
		{
			return;
		}

		lock (_gate)
		{
			_pendingLines.Clear();
			foreach (string line in lines)
			{
				_pendingLines.Add(FormatLine(line));
			}

			TrimPendingLinesLocked();
			_flushScheduled = _pendingLines.Count > 0;
			_flushRequestId++;
			_scrollScheduled = false;
			_scrollRequestId++;
		}

		UiThread.TryBeginInvoke(() =>
		{
			ReturnVisibleRows();
			if (_flushScheduled)
			{
				FlushPendingLines();
			}
		}, $"{DispatchSource}:SET_LINES");
	}

	public void Clear()
	{
		lock (_gate)
		{
			_pendingLines.Clear();
			_flushScheduled = false;
			_flushRequestId++;
			_scrollScheduled = false;
			_scrollRequestId++;
		}

		UiThread.TryBeginInvoke(ReturnVisibleRows, $"{DispatchSource}:CLEAR");
	}

	public void ReleaseRows()
	{
		Clear();
		_rowPool.Clear();
	}

	public string GetCopyText()
	{
		try
		{
			var lines = TailStack.Children
				.OfType<Label>()
				.Select(label => label.BindingContext is LogTailRowState state ? state.CopyText : label.Text)
				.Where(line => !string.IsNullOrWhiteSpace(line));

			return string.Join(Environment.NewLine, lines);
		}
		catch (ObjectDisposedException)
		{
			return string.Empty;
		}
		catch (InvalidOperationException)
		{
			return string.Empty;
		}
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
		_isUnloaded = false;
		_rowPool.Prewarm(Math.Max(0, PrewarmRows));
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
		_isUnloaded = true;
		ReleaseRows();
	}

	private void FlushPendingLines()
	{
		try
		{
			using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation(FlushOperationName);
			if (_isUnloaded)
			{
				ResetPendingSchedule();
				return;
			}

			var lines = DequeuePendingBatch();
			foreach (var line in lines)
			{
				if (line.IsInlineProgress && TryUpdateLastInlineProgressRow(line))
				{
					continue;
				}

				TailStack.Children.Add(RentRow(line));
				TrimVisibleRows();
			}

			bool hasMorePending;
			lock (_gate)
			{
				hasMorePending = _pendingLines.Count > 0;
				_flushScheduled = hasMorePending;
			}

			if (AutoScroll && TailStack.Children.Count > 0)
			{
				RequestAutoScroll();
			}

			if (hasMorePending && !UiThread.TryBeginInvoke(FlushPendingLines, $"{DispatchSource}:FLUSH_CONTINUE"))
			{
				ResetPendingSchedule();
			}
		}
		catch (ObjectDisposedException)
		{
			ResetPendingSchedule();
		}
		catch (InvalidOperationException)
		{
			ResetPendingSchedule();
		}
		catch (Exception ex)
		{
			ResetPendingSchedule();
			NexusLog.Exception(ex, "[LOG TAIL] Flush failed");
		}
	}

	private async Task FlushPendingLinesWhenIdleAsync()
	{
		int requestId;
		lock (_gate)
		{
			requestId = _flushRequestId;
		}

		try
		{
			await Task.Delay(Math.Max(0, FlushDebounceMs));
			if (_isUnloaded)
			{
				ResetPendingSchedule();
				return;
			}

			bool shouldFlush;
			lock (_gate)
			{
				shouldFlush = requestId == _flushRequestId && _flushScheduled;
			}

			if (!shouldFlush)
			{
				return;
			}

			if (!UiThread.TryBeginInvoke(FlushPendingLines, $"{DispatchSource}:FLUSH"))
			{
				ResetPendingSchedule();
			}
		}
		catch (ObjectDisposedException)
		{
			ResetPendingSchedule();
		}
		catch (InvalidOperationException)
		{
			ResetPendingSchedule();
		}
	}

	private List<LogTailEntry> DequeuePendingBatch()
	{
		lock (_gate)
		{
			int batchSize = Math.Min(Math.Max(1, MaxFlushBatchSize), _pendingLines.Count);
			var lines = _pendingLines.GetRange(0, batchSize);
			_pendingLines.RemoveRange(0, batchSize);
			return lines;
		}
	}

	private void TrimPendingLinesLocked()
	{
		int safeMaxPendingRows = Math.Max(MaxRows, MaxPendingRows);
		if (_pendingLines.Count <= safeMaxPendingRows)
		{
			return;
		}

		_pendingLines.RemoveRange(0, _pendingLines.Count - safeMaxPendingRows);
	}

	private void ResetPendingSchedule()
	{
		lock (_gate)
		{
			_flushScheduled = false;
			_flushRequestId++;
		}
	}

	private void RequestAutoScroll()
	{
		if (!_isPinnedToEnd)
		{
			return;
		}

		int requestId;
		lock (_gate)
		{
			if (_scrollScheduled)
			{
				return;
			}

			_scrollScheduled = true;
			requestId = ++_scrollRequestId;
		}

		_ = ScrollToEndWhenIdleAsync(requestId);
	}

	private async Task ScrollToEndWhenIdleAsync(int requestId)
	{
		try
		{
			await Task.Delay(Math.Max(0, ScrollDebounceMs));
			if (_isUnloaded)
			{
				ResetScrollSchedule();
				return;
			}

			bool shouldScroll;
			lock (_gate)
			{
				shouldScroll = requestId == _scrollRequestId && _isPinnedToEnd;
				_scrollScheduled = false;
			}

			if (!shouldScroll || TailStack.Children.Count == 0)
			{
				return;
			}

			await TailScrollView.ScrollToAsync(TailStack, ScrollToPosition.End, AnimateScroll);
		}
		catch (ObjectDisposedException)
		{
			ResetScrollSchedule();
		}
		catch (InvalidOperationException)
		{
			ResetScrollSchedule();
		}
	}

	private void ResetScrollSchedule()
	{
		lock (_gate)
		{
			_scrollScheduled = false;
		}
	}

	private void OnTailScrolled(object? sender, ScrolledEventArgs e)
	{
		try
		{
			double contentHeight = Math.Max(0, TailScrollView.ContentSize.Height);
			double viewportHeight = Math.Max(0, TailScrollView.Height);
			if (contentHeight <= viewportHeight + 1)
			{
				_isPinnedToEnd = true;
				return;
			}

			double maxScrollY = Math.Max(0, contentHeight - viewportHeight);
			_isPinnedToEnd = maxScrollY - Math.Max(0, e.ScrollY) <= Math.Max(0, AutoScrollThreshold);
		}
		catch (ObjectDisposedException)
		{
		}
		catch (InvalidOperationException)
		{
		}
	}

	private Label RentRow(LogTailEntry line)
	{
		var label = _rowPool.Rent();
		label.Text = line.DisplayText;
		label.BindingContext = new LogTailRowState(line.CopyText, line.IsInlineProgress, line.SourceText);
		label.TextColor = RowColorResolver?.Invoke(line.SourceText) ?? RowTextColor;
		return label;
	}

	private bool TryUpdateLastInlineProgressRow(LogTailEntry line)
	{
		if (TailStack.Children.Count == 0)
		{
			return false;
		}

		var child = TailStack.Children[^1];
		if (child is not Label label || label.BindingContext is not LogTailRowState state || !state.IsInlineProgress)
		{
			return false;
		}

		label.Text = line.DisplayText;
		label.BindingContext = new LogTailRowState(line.CopyText, true, line.SourceText);
		label.TextColor = RowColorResolver?.Invoke(line.SourceText) ?? RowTextColor;
		return true;
	}

	private void TrimVisibleRows()
	{
		int safeMaxRows = Math.Max(1, MaxRows);
		while (TailStack.Children.Count > safeMaxRows)
		{
			var child = TailStack.Children[0];
			TailStack.Children.RemoveAt(0);
			if (child is Label label)
			{
				_rowPool.Return(label);
			}
		}
	}

	private void ReturnVisibleRows()
	{
		try
		{
			using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation(ClearOperationName);
			var labels = TailStack.Children
				.OfType<Label>()
				.ToList();

			TailStack.Children.Clear();
			foreach (var label in labels)
			{
				_rowPool.Return(label);
			}
		}
		catch (ObjectDisposedException)
		{
		}
		catch (InvalidOperationException)
		{
		}
	}

	private LogTailEntry FormatLine(string? line)
	{
		string body = string.IsNullOrWhiteSpace(line)
			? string.Empty
			: line.ReplaceLineEndings(" ").Trim();

		if (TryFormatPipRawProgress(body, out var progressEntry))
		{
			return progressEntry;
		}

		string copyBody = ClampWithEllipsis(body, Math.Max(MaxLineLength, MaxCopyLineLength));
		string timestamp = DateTime.Now.ToString("HH:mm:ss");
		int safeMaxLength = Math.Max(40, MaxLineLength);
		string displayBody = ClampWithEllipsis(body, safeMaxLength);

		return new LogTailEntry(
			$"{timestamp}  {displayBody}",
			$"{timestamp}  {copyBody}",
			false,
			body);
	}

	private bool TryFormatPipRawProgress(string body, out LogTailEntry entry)
	{
		entry = default;
		var match = PipRawProgressRegex.Match(body);
		if (!match.Success
			|| !long.TryParse(match.Groups["current"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long current)
			|| !long.TryParse(match.Groups["total"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long total)
			|| total <= 0)
		{
			return false;
		}

		current = Math.Clamp(current, 0, total);
		double progress = current / (double)total;
		string percent = progress.ToString("P1", CultureInfo.InvariantCulture);
		string speedText = FormatProgressSpeed(current);
		string message = string.IsNullOrWhiteSpace(speedText)
			? $"Downloading... {percent} ({FormatBytes(current)} / {FormatBytes(total)})"
			: $"Downloading... {percent} ({FormatBytes(current)} / {FormatBytes(total)}, {speedText})";
		string timestamp = DateTime.Now.ToString("HH:mm:ss");

		entry = new LogTailEntry(
			$"{timestamp}  {message}",
			$"{timestamp}  {message}",
			true,
			body);
		return true;
	}

	private string FormatProgressSpeed(long currentBytes)
	{
		var now = DateTimeOffset.UtcNow;
		string speedText = string.Empty;
		if (_lastPipProgressBytes >= 0 && _lastPipProgressAt != default)
		{
			double seconds = Math.Max(0.001, (now - _lastPipProgressAt).TotalSeconds);
			long deltaBytes = Math.Max(0, currentBytes - _lastPipProgressBytes);
			if (deltaBytes > 0)
			{
				speedText = $"{FormatBytes(deltaBytes / seconds)}/s";
			}
		}

		_lastPipProgressBytes = currentBytes;
		_lastPipProgressAt = now;
		return speedText;
	}

	private static string FormatBytes(double bytes)
	{
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		double value = Math.Max(0, bytes);
		int unitIndex = 0;
		while (value >= 1000 && unitIndex < units.Length - 1)
		{
			value /= 1000;
			unitIndex++;
		}

		return unitIndex == 0
			? $"{value:0} {units[unitIndex]}"
			: $"{value:0.##} {units[unitIndex]}";
	}

	private static Label CreateLogRow()
	{
		var label = new Label
		{
			FontSize = 11,
			FontFamily = "JetBrainsMono",
			LineBreakMode = LineBreakMode.TailTruncation,
			InputTransparent = false,
			TextColor = RowTextColor,
			BackgroundColor = TransparentColor,
			MaxLines = 1
		};

		FlyoutBase.SetContextFlyout(label, CreateLogRowFlyout(label));
		var hover = new PointerGestureRecognizer();
		hover.PointerEntered += OnRowPointerEntered;
		hover.PointerExited += OnRowPointerExited;
		label.GestureRecognizers.Add(hover);
		return label;
	}

	private static void ResetLogRow(Label label)
	{
		label.Text = string.Empty;
		label.FormattedText = null;
		label.BindingContext = null;
		label.TextColor = RowTextColor;
		label.BackgroundColor = TransparentColor;
	}

	private static MenuFlyout CreateLogRowFlyout(Label owner)
	{
		var flyout = new MenuFlyout();
		var copyItem = new MenuFlyoutItem
		{
			Text = LocalizationManager.Text("common.copy")
		};
		copyItem.Clicked += async (_, _) => await CopyLineAsync(
			owner.BindingContext is LogTailRowState state ? state.CopyText : owner.Text);
		flyout.Add(copyItem);

		return flyout;
	}

	private static async Task CopyLineAsync(string? line)
	{
		if (string.IsNullOrEmpty(line))
		{
			return;
		}

		try
		{
			await Clipboard.Default.SetTextAsync(line);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[LOG TAIL] Failed to copy log line");
		}
	}

	private static void OnRowPointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender is not Label label)
		{
			return;
		}

		label.TextColor = RowHoverTextColor;
		label.BackgroundColor = RowHoverBackgroundColor;
	}

	private static void OnRowPointerExited(object? sender, PointerEventArgs e)
	{
		if (sender is not Label label)
		{
			return;
		}

		if (FindLogTailView(label) is { } owner && label.BindingContext is LogTailRowState state)
		{
			label.TextColor = owner.RowColorResolver?.Invoke(state.SourceText) ?? RowTextColor;
		}
		else
		{
			label.TextColor = RowTextColor;
		}
		label.BackgroundColor = TransparentColor;
	}

	private static LogTailView? FindLogTailView(Element element)
	{
		Element? current = element;
		while (current is not null)
		{
			if (current is LogTailView view)
			{
				return view;
			}

			current = current.Parent;
		}

		return null;
	}

	private static string ClampWithEllipsis(string text, int maxLength)
	{
		if (text.Length <= maxLength)
		{
			return text;
		}

		return text[..maxLength] + "...";
	}

	private readonly record struct LogTailEntry(string DisplayText, string CopyText, bool IsInlineProgress, string SourceText);
	private readonly record struct LogTailRowState(string CopyText, bool IsInlineProgress, string SourceText);
}
