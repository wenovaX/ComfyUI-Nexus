using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Rail.Contracts;
using ComfyUI_Nexus.Views.Rail;
using ComfyUI_Nexus.Views.Rail.Tools;
using ComfyUI_Nexus.Views.Rail.Tools.Assets;
using ComfyUI_Nexus.Views.Overlays;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Controls.Shapes;
using Path = System.IO.Path;

namespace ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;

public partial class MediaAssetsView : ContentView, IRailToolView
{
	private enum MediaAssetScope
	{
		Output,
		Input,
	}

	private enum MediaAssetSortDirection
	{
		RecentFirst,
		OldestFirst,
	}

	public sealed class MediaAssetEntry : INotifyPropertyChanged
	{
		private bool _isSelected;
		private string? _thumbnailPath;
		private ImageSource? _thumbnailSource;

		public MediaAssetEntry(
			string name,
			string fullPath,
			string? thumbnailPath,
			DateTime createdAt,
			DateTime modifiedAt,
			int? pixelWidth,
			int? pixelHeight,
			string? jobId,
			bool isBatchInferred,
			string type,
			string subfolder,
			long sizeBytes)
		{
			Name = name;
			FullPath = fullPath;
			CreatedAt = createdAt;
			ModifiedAt = modifiedAt;
			PixelWidth = pixelWidth;
			PixelHeight = pixelHeight;
			JobId = jobId;
			IsBatchInferred = isBatchInferred;
			Type = type;
			Subfolder = subfolder;
			SizeBytes = sizeBytes;
			SetThumbnailPath(thumbnailPath);
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		public string Name { get; }
		public string FullPath { get; }
		public string? ThumbnailPath
		{
			get => _thumbnailPath;
			private set
			{
				if (string.Equals(_thumbnailPath, value, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}

				_thumbnailPath = value;
				OnPropertyChanged();
			}
		}

		public DateTime ModifiedAt { get; }
		public DateTime CreatedAt { get; }
		public int? PixelWidth { get; }
		public int? PixelHeight { get; }
		public string? JobId { get; }
		public bool IsBatchInferred { get; }
		public string Type { get; }
		public string Subfolder { get; }
		public long SizeBytes { get; }
		public ImageSource? ThumbnailSource
		{
			get => _thumbnailSource;
			private set
			{
				if (ReferenceEquals(_thumbnailSource, value))
				{
					return;
				}

				_thumbnailSource = value;
				OnPropertyChanged();
			}
		}

		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				if (_isSelected == value)
				{
					return;
				}

				_isSelected = value;
				OnPropertyChanged();
			}
		}

		public string DetailText => CreatedAt.ToString("g");
		public string ResolutionText => PixelWidth > 0 && PixelHeight > 0
			? $"{PixelWidth} x {PixelHeight}"
			: string.Empty;
		public string TooltipText
			=> string.IsNullOrWhiteSpace(ResolutionText)
				? $"{Name}{Environment.NewLine}{CreatedAt:g}{Environment.NewLine}{FormatSize(SizeBytes)}"
				: $"{Name}{Environment.NewLine}{CreatedAt:g}{Environment.NewLine}{FormatSize(SizeBytes)} · {ResolutionText}";

		internal void SetThumbnailPath(string? thumbnailPath)
		{
			if (string.Equals(ThumbnailPath, thumbnailPath, StringComparison.OrdinalIgnoreCase) &&
				(_thumbnailSource != null || string.IsNullOrWhiteSpace(thumbnailPath)))
			{
				return;
			}

			ThumbnailPath = thumbnailPath;
			ThumbnailSource = !string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath)
				? ImageSource.FromFile(thumbnailPath)
				: null;
		}

		private static string FormatSize(long bytes)
		{
			string[] units = ["B", "KB", "MB", "GB"];
			double value = bytes;
			int unitIndex = 0;

			while (value >= 1024 && unitIndex < units.Length - 1)
			{
				value /= 1024;
				unitIndex++;
			}

			return unitIndex == 0
				? $"{bytes} {units[unitIndex]}"
				: $"{value:0.#} {units[unitIndex]}";
		}

		private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	internal sealed record MediaAssetSourceEntry(
		string Name,
		string FullPath,
		DateTime CreatedAt,
		DateTime ModifiedAt,
		int? PixelWidth,
		int? PixelHeight,
		string? JobId,
		bool IsBatchInferred,
		string Type,
		string Subfolder,
		long SizeBytes);

	private readonly record struct SequenceFilename(
		string Prefix,
		int Number,
		int Width,
		string Suffix);

	private sealed record MediaAssetCacheSnapshot(
		string RootPath,
		IReadOnlyList<MediaAssetEntry> Entries);

	private sealed record OutputJobBuildResult(
		IReadOnlyList<MediaAssetSourceEntry> Entries,
		IReadOnlyList<string> MissingJobIds);

	private sealed class MediaAssetScopeSurface
	{
		internal MediaAssetScopeSurface(
			MediaAssetScope scope,
			ScrollView scrollView,
			Grid surface,
			View spacer,
			AbsoluteLayout canvas)
		{
			Scope = scope;
			ScrollView = scrollView;
			Surface = surface;
			Spacer = spacer;
			Canvas = canvas;
		}

		internal MediaAssetScope Scope { get; }
		internal ScrollView ScrollView { get; }
		internal Grid Surface { get; }
		internal View Spacer { get; }
		internal AbsoluteLayout Canvas { get; }
		internal List<MediaAssetEntry> SourceItems { get; } = [];
		internal ObservableCollection<MediaAssetEntry> Items { get; } = [];
		internal UiObjectPool<MediaAssetCell>? CellPool { get; set; }
		internal Dictionary<int, MediaAssetCell> VisibleCells { get; } = new();
		internal string RenderedSignature { get; set; } = string.Empty;
		internal string VirtualLayoutSignature { get; set; } = string.Empty;
		internal Task? RefreshTask { get; set; }
		internal string? ActiveRefreshKey { get; set; }
		internal bool RefreshRequestedAfterCurrent { get; set; }
		internal bool IsBusy { get; set; }
		internal int RenderVersion;
		internal int AttachVersion;
	}

	private sealed record MovedMediaAsset(
		MediaAssetEntry Entry,
		string SourcePath,
		string DestinationPath);

	private sealed class MediaAssetCell
	{
		private readonly Action<MediaAssetEntry> _open;
		private readonly Action<MediaAssetEntry> _reveal;
		private readonly Action<MediaAssetEntry> _copy;
		private readonly Action<MediaAssetEntry> _move;
		private readonly Action<MediaAssetEntry> _copyPath;
		private readonly Action<MediaAssetEntry> _rename;
		private readonly Action<MediaAssetEntry> _delete;
		private readonly Func<string, bool> _shouldDim;

		internal MediaAssetCell(
			Border root,
			Image thumbnail,
			RowDefinition thumbnailRow,
			Border thumbnailLoadingBadge,
			Label nameLabel,
			Label detailLabel,
			Border textOverlay,
			Border resolutionBadge,
			Label resolutionLabel,
			Action<MediaAssetEntry> open,
			Action<MediaAssetEntry> reveal,
			Action<MediaAssetEntry> copy,
			Action<MediaAssetEntry> move,
			Action<MediaAssetEntry> copyPath,
			Action<MediaAssetEntry> rename,
			Action<MediaAssetEntry> delete,
			Func<string, bool> shouldDim)
		{
			Root = root;
			Thumbnail = thumbnail;
			ThumbnailRow = thumbnailRow;
			ThumbnailLoadingBadge = thumbnailLoadingBadge;
			NameLabel = nameLabel;
			DetailLabel = detailLabel;
			TextOverlay = textOverlay;
			ResolutionBadge = resolutionBadge;
			ResolutionLabel = resolutionLabel;
			_open = open;
			_reveal = reveal;
			_copy = copy;
			_move = move;
			_copyPath = copyPath;
			_rename = rename;
			_delete = delete;
			_shouldDim = shouldDim;
		}

		internal Border Root { get; }
		internal Image Thumbnail { get; }
		internal RowDefinition ThumbnailRow { get; }
		internal Border ThumbnailLoadingBadge { get; }
		internal Label NameLabel { get; }
		internal Label DetailLabel { get; }
		internal Border TextOverlay { get; }
		internal Border ResolutionBadge { get; }
		internal Label ResolutionLabel { get; }
		internal MediaAssetEntry? Entry { get; private set; }
		internal string BindSignature { get; private set; } = string.Empty;

		internal void Bind(MediaAssetEntry entry)
		{
			string nextSignature = BuildBindSignature(entry);
			if (ReferenceEquals(Entry, entry) && string.Equals(BindSignature, nextSignature, StringComparison.Ordinal))
			{
				UpdateVisualState();
				return;
			}

			if (Entry != null)
			{
				Entry.PropertyChanged -= OnEntryPropertyChanged;
			}

			Entry = entry;
			BindSignature = nextSignature;
			Root.BindingContext = entry;
			NameLabel.Text = entry.Name;
			DetailLabel.Text = entry.DetailText;
			ResolutionLabel.Text = entry.ResolutionText;
			ResolutionBadge.IsVisible = !string.IsNullOrWhiteSpace(entry.ResolutionText);
			ToolTipProperties.SetText(Root, entry.TooltipText);
			Thumbnail.Source = entry.ThumbnailSource;
			ThumbnailLoadingBadge.IsVisible = entry.ThumbnailSource == null;
			entry.PropertyChanged += OnEntryPropertyChanged;
			UpdateVisualState();
		}

		internal void Clear()
		{
			if (Entry != null)
			{
				Entry.PropertyChanged -= OnEntryPropertyChanged;
			}

			Entry = null;
			BindSignature = string.Empty;
			Root.BindingContext = null;
			Thumbnail.Source = null;
			ThumbnailLoadingBadge.IsVisible = false;
			NameLabel.Text = string.Empty;
			DetailLabel.Text = string.Empty;
			TextOverlay.IsVisible = false;
			ResolutionLabel.Text = string.Empty;
			ResolutionBadge.IsVisible = false;
			ToolTipProperties.SetText(Root, string.Empty);
			Root.BackgroundColor = MediaCellBackgroundColor;
			Root.Stroke = MediaCellStrokeColor;
			Root.Opacity = 1;
			Root.Scale = 1;
		}

		internal void RefreshVisualState() => UpdateVisualState();

		internal void Resize(double width, double height, bool showMetadataOverlay)
		{
			Root.WidthRequest = width;
			Root.HeightRequest = height;
			ThumbnailRow.Height = new GridLength(height);
			TextOverlay.IsVisible = showMetadataOverlay;
		}

		internal void Open() => Invoke(_open);
		internal void Reveal() => Invoke(_reveal);
		internal void Copy() => Invoke(_copy);
		internal void Move() => Invoke(_move);
		internal void CopyPath() => Invoke(_copyPath);
		internal void Rename() => Invoke(_rename);
		internal void Delete() => Invoke(_delete);

		private void Invoke(Action<MediaAssetEntry> action)
		{
			if (Entry != null)
			{
				action(Entry);
			}
		}

		private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(MediaAssetEntry.ThumbnailSource))
			{
				BindSignature = Entry == null ? string.Empty : BuildBindSignature(Entry);
				Thumbnail.Source = Entry?.ThumbnailSource;
				ThumbnailLoadingBadge.IsVisible = Entry?.ThumbnailSource == null;
			}
			else if (e.PropertyName == nameof(MediaAssetEntry.IsSelected))
			{
				UpdateVisualState();
			}
		}

		private void UpdateVisualState()
		{
			bool isSelected = Entry?.IsSelected == true;
			Root.BackgroundColor = isSelected ? MediaCellSelectedBackgroundColor : MediaCellBackgroundColor;
			Root.Stroke = isSelected ? MediaCellSelectedStrokeColor : MediaCellStrokeColor;
			Root.Opacity = Entry != null && _shouldDim(Entry.FullPath) ? 0.46 : 1;
		}

		private static string BuildBindSignature(MediaAssetEntry entry)
			=> $"{entry.FullPath}|{entry.ThumbnailPath}|{entry.IsSelected}";
	}

	private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png",
		".jpg",
		".jpeg",
		".webp",
		".bmp",
		".gif",
		".tif",
		".tiff",
	};

	private const int MaxVisibleItems = 180;
	private const long MaxPreviewFileBytes = 60L * 1024L * 1024L;
	private const int MediaCreateThumbnailBatchSize = 1;
	private const int MediaCachedThumbnailBatchSize = 12;
	private const int MediaThumbnailReprioritizeInterval = 8;
	private const int MediaGridBufferRows = 2;
	private const int MediaCellPrewarmCount = 50;
	private const int MediaAttachCoalesceDelayMs = 16;
	private const int MediaJobBatchForwardLimit = 10;
	private const int DefaultMediaGridColumns = 1;
	private const double MediaCardGap = 8;
	private const double MediaScrollbarGutter = 8;
	private const string MediaTabActiveBackgroundColor = "#132334";
	private const string MediaTabActiveHoverBackgroundColor = "#173047";
	private const string MediaTabInactiveBackgroundColor = "#090e14";
	private const string MediaTabInactiveHoverBackgroundColor = "#111b29";
	private const string MediaTabActiveStrokeColor = "#2c5f78";
	private const string MediaTabActiveHoverStrokeColor = "#4e9fbd";
	private const string MediaTabInactiveStrokeColor = "#142235";
	private const string MediaTabInactiveHoverStrokeColor = "#2d4962";
	private static readonly Color MediaCellBackgroundColor = Color.FromArgb("#0d141d");
	private static readonly Color MediaCellHoverBackgroundColor = Color.FromArgb("#111c29");
	private static readonly Color MediaCellSelectedBackgroundColor = Color.FromArgb("#17324a");
	private static readonly Color MediaCellStrokeColor = Color.FromArgb("#1a2740");
	private static readonly Color MediaCellHoverStrokeColor = Color.FromArgb("#356580");
	private static readonly Color MediaCellSelectedStrokeColor = Color.FromArgb("#5aa7c7");
	private static readonly Color MediaLoadingBadgeBackgroundColor = Color.FromArgb("#AA08121D");
	private static readonly Color MediaLoadingBadgeStrokeColor = Color.FromArgb("#24445C");
	private static readonly Color MediaLoadingIndicatorColor = Color.FromArgb("#8de7ff");
	private static readonly Color MediaLoadingTextColor = Color.FromArgb("#8dbce0");
	private static readonly Color MediaThumbnailHostBackgroundColor = Color.FromArgb("#070d13");
	private static readonly Color MediaResolutionBadgeBackgroundColor = Color.FromArgb("#CC02060A");
	private static readonly Color MediaTitleTextColor = Color.FromArgb("#f4fbff");
	private static readonly Color MediaDetailTextColor = Color.FromArgb("#a7c3db");
	private static readonly Color MediaPathValidTextColor = Color.FromArgb("#4c647a");
	private static readonly Color MediaOutputPathValidTextColor = Color.FromArgb("#6f8da8");
	private static readonly Color MediaPathInvalidTextColor = Color.FromArgb("#b15f5f");
	private static readonly Color MediaToolbarHoverBackgroundColor = Color.FromArgb("#12283a");
	private static readonly Color MediaTextActionHoverBackgroundColor = Color.FromArgb("#102438");
	private static readonly Color MediaTabActiveTextColor = Color.FromArgb("#ebf9ff");
	private static readonly Color MediaTabInactiveTextColor = Color.FromArgb("#7d96ad");
	private static readonly DirectoryWatcherOptions MediaWatcherOptions = new()
	{
		IncludeSubdirectories = false,
		DebounceIntervalMs = 350,
		StableDelayMs = 150,
	};
	private static readonly Brush MediaTextOverlayBackground = new LinearGradientBrush
	{
		StartPoint = new Point(0, 0),
		EndPoint = new Point(0, 1),
		GradientStops =
		{
			new GradientStop(Color.FromArgb("#00000000"), 0),
			new GradientStop(Color.FromArgb("#D2070D14"), 0.62f),
			new GradientStop(Color.FromArgb("#F2070D14"), 1),
		},
	};

	private readonly AssetSelectionController _selection = new();
	private readonly AssetClipboardController _clipboard = new();
	private readonly AssetFileOperationService _fileOperations;
	private readonly MediaAssetThumbnailCache _thumbnailCache = new();
	private readonly Dictionary<string, MediaAssetCacheSnapshot> _entryCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _dirtyCacheKeys = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<MediaAssetJobPreview> _syncedOutputJobs = [];
	private readonly object _entryCacheGate = new();
	private readonly RailLoadingOverlayController _loadingOverlay;
	private readonly RailDirectoryWatchController _directoryWatcher;
	private MediaAssetScopeSurface? _outputSurface;
	private MediaAssetScopeSurface? _inputSurface;
	private string _comfyRootPath = string.Empty;
	private MediaAssetScope _activeScope = MediaAssetScope.Output;
	private bool _isRenderDeferred = true;
	private bool _isReady;
	private bool _isRailActive;
	private bool _isOutputTabHovered;
	private bool _isInputTabHovered;
	private bool _hasSyncedOutputJobs;
	private bool _isResettingSearchText;
	private bool _deleteSelectionInFlight;
	private string _searchText = string.Empty;
	private RailSearchVisualController? _searchVisuals;
	private NexusEntryTextController? _searchTextController;
	private MediaAssetSortDirection _sortDirection = MediaAssetSortDirection.RecentFirst;
	private int _mediaGridColumns = LoadMediaGridColumnsPreference();
	private CancellationTokenSource _lifetimeCts = new();
	private Func<IReadOnlyList<MediaViewerItem>, Task<bool>>? _deleteHandler;
	private Func<Task>? _outputRefreshHandler;
	private Func<IReadOnlyList<string>, Task>? _staleOutputJobCleanupHandler;

	public Command<MediaAssetEntry> OpenAssetCommand { get; }
	public Command<MediaAssetEntry> RevealAssetCommand { get; }
	public Command<MediaAssetEntry> CopyAssetCommand { get; }
	public Command<MediaAssetEntry> MoveAssetCommand { get; }
	public Command<MediaAssetEntry> CopyAssetPathCommand { get; }
	public Command<MediaAssetEntry> RenameAssetCommand { get; }
	public Command<MediaAssetEntry> DeleteAssetCommand { get; }
	View IRailToolView.View => this;
	bool IRailToolView.IsReady => _isReady;
	bool IRailToolView.IsBusy => GetSurface(MediaAssetScope.Output).IsBusy || GetSurface(MediaAssetScope.Input).IsBusy;

	internal event EventHandler<MediaAssetViewerRequest>? ViewerRequested;

	public MediaAssetsView()
	{
		OpenAssetCommand = new Command<MediaAssetEntry>(entry => _ = OpenAssetAsync(entry));
		RevealAssetCommand = new Command<MediaAssetEntry>(entry => _ = RevealAssetAsync(entry));
		CopyAssetCommand = new Command<MediaAssetEntry>(entry => _ = BeginCopySelectedAsync(entry));
		MoveAssetCommand = new Command<MediaAssetEntry>(entry => _ = MoveSelectionAsync(entry));
		CopyAssetPathCommand = new Command<MediaAssetEntry>(entry => _ = CopySelectedAssetPathsAsync(entry));
		RenameAssetCommand = new Command<MediaAssetEntry>(entry => _ = RenameSelectionAsync(entry));
		DeleteAssetCommand = new Command<MediaAssetEntry>(entry => _ = DeleteSelectionAsync(entry));
		_fileOperations = new AssetFileOperationService(
			_selection,
			_clipboard,
			GetActiveRootPath,
			OpenPathAsync,
			RefreshAfterFileOperation,
			SyncVisibleSelectionState);

		InitializeComponent();
		_directoryWatcher = new RailDirectoryWatchController(
			"MEDIA_WATCHER",
			Dispatcher,
			CanApplyMediaWatcherBatch,
			ApplyMediaWatcherBatch);
		_searchVisuals = new RailSearchVisualController(MediaAssetsSearchBorder, MediaAssetsSearchEntry);
		_searchTextController = new NexusEntryTextController(MediaAssetsSearchEntry, MediaAssetsSearchBorder);
		new RailSearchClearButtonController(ClearMediaAssetsSearchButton, ClearMediaAssetsSearchLabel);
		_outputSurface = new MediaAssetScopeSurface(
			MediaAssetScope.Output,
			OutputMediaAssetsScrollView,
			OutputMediaAssetsVirtualSurface,
			OutputMediaAssetsVirtualSpacer,
			OutputMediaAssetsVirtualCanvas);
		_inputSurface = new MediaAssetScopeSurface(
			MediaAssetScope.Input,
			InputMediaAssetsScrollView,
			InputMediaAssetsVirtualSurface,
			InputMediaAssetsVirtualSpacer,
			InputMediaAssetsVirtualCanvas);
		_outputSurface.CellPool = new UiObjectPool<MediaAssetCell>(CreateMediaAssetCell, ResetMediaAssetCell);
		_inputSurface.CellPool = new UiObjectPool<MediaAssetCell>(CreateMediaAssetCell, ResetMediaAssetCell);
		_loadingOverlay = new RailLoadingOverlayController(MediaAssetsLoadingOverlay);
		BindingContext = this;
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	internal void SetDeleteHandler(Func<IReadOnlyList<MediaViewerItem>, Task<bool>> deleteHandler)
	{
		_deleteHandler = deleteHandler;
	}

	internal bool TryCreateViewerRequest(string fullPath, out MediaAssetViewerRequest request)
	{
		request = new MediaAssetViewerRequest([], -1);
		if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
		{
			return false;
		}

		var surface = GetSurfaceContaining(fullPath);
		if (surface == null)
		{
			return false;
		}

		var items = surface.Items
			.Where(item => File.Exists(item.FullPath))
			.Select(ToMediaViewerItem)
			.ToList();
		int startIndex = items.FindIndex(item => string.Equals(item.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
		if (items.Count == 0 || startIndex < 0)
		{
			return false;
		}

		request = new MediaAssetViewerRequest(items, startIndex);
		return true;
	}

	internal void SetOutputRefreshHandler(Func<Task> outputRefreshHandler)
	{
		_outputRefreshHandler = outputRefreshHandler;
	}

	internal void SetStaleOutputJobCleanupHandler(Func<IReadOnlyList<string>, Task> staleOutputJobCleanupHandler)
	{
		_staleOutputJobCleanupHandler = staleOutputJobCleanupHandler;
	}

	internal void SetComfyRootPath(string rootPath)
	{
		if (string.Equals(_comfyRootPath, rootPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		string previousOutputRoot = GetRootPath(MediaAssetScope.Output);
		string previousInputRoot = GetRootPath(MediaAssetScope.Input);

		_comfyRootPath = rootPath;
		MarkCacheDirty(previousOutputRoot);
		MarkCacheDirty(previousInputRoot);
		MarkCacheDirty(GetRootPath(MediaAssetScope.Output));
		MarkCacheDirty(GetRootPath(MediaAssetScope.Input));
		MarkCacheDirty(GetActiveRootPath());
		ConfigureMediaWatcher();
		if (_hasSyncedOutputJobs)
		{
			_syncedOutputJobs.Clear();
			_hasSyncedOutputJobs = false;
		}

		_selection.Clear();
		if (_isRailActive)
		{
			_ = RefreshActiveScopeAfterRootChangeAsync();
		}
		UpdateTabState();
	}

	internal void RefreshAssets() => QueueRefresh();

	internal void SyncOutputJobs(IReadOnlyList<MediaAssetJobPreview> jobs)
	{
		_syncedOutputJobs.Clear();
		_syncedOutputJobs.AddRange(jobs);
		_hasSyncedOutputJobs = true;
		NexusLog.Trace($"[MEDIA_ASSETS] Synced output jobs: {jobs.Count}");
		MarkCacheDirty(GetRootPath(MediaAssetScope.Output));
		_ = RefreshScopeAsync(MediaAssetScope.Output, CancellationToken.None, showLoadingOverlay: _activeScope == MediaAssetScope.Output, forceRefresh: true);
	}

	internal bool TryHandleKeyboardShortcut(NexusKey key, bool ctrl, bool shift)
	{
		if (!CanHandleKeyboardShortcut(key, ctrl, shift))
		{
			return false;
		}

		var entry = GetPrimarySelectedEntry();
		if (ctrl && !shift && key == NexusKey.A)
		{
			if (MediaAssetsSearchEntry.IsFocused)
			{
				MediaAssetsSearchEntry.CursorPosition = 0;
				MediaAssetsSearchEntry.SelectionLength = MediaAssetsSearchEntry.Text?.Length ?? 0;
			}
			else
			{
				SelectAllVisibleAssets();
			}

			return true;
		}

		if (entry == null)
		{
			return false;
		}

		if (!ctrl && !shift && key == NexusKey.Enter)
		{
			_ = OpenAssetAsync(entry);
			return true;
		}

		if (ctrl && !shift && key == NexusKey.C)
		{
			_ = BeginCopySelectedAsync(entry);
			return true;
		}

		if (ctrl && shift && key == NexusKey.C)
		{
			_ = CopySelectedAssetPathsAsync(entry);
			return true;
		}

		if (!ctrl && !shift && key == NexusKey.F2)
		{
			_ = RenameSelectionAsync(entry);
			return true;
		}

		if (!ctrl && !shift && key == NexusKey.Delete)
		{
			_ = DeleteSelectionAsync(entry);
			return true;
		}

		return false;
	}

	public bool CanHandleKeyboardShortcut(NexusKey key, bool ctrl, bool shift)
	{
		if (ctrl && !shift && key == NexusKey.A)
		{
			return true;
		}

		if (GetPrimarySelectedEntry() == null)
		{
			return false;
		}

		return (!ctrl && !shift && key is NexusKey.Enter or NexusKey.F2 or NexusKey.Delete) ||
			(ctrl && !shift && key == NexusKey.C) ||
			(ctrl && shift && key == NexusKey.C);
	}

	async Task IRailToolView.PrewarmAsync(CancellationToken cancellationToken)
	{
		using var prewarmCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
		CancellationToken prewarmToken = prewarmCts.Token;
		prewarmToken.ThrowIfCancellationRequested();

		// Pool-only prewarm keeps app boot deterministic; file scans and thumbnail work
		// start from OpenAsync or explicit refresh after the rail is visible.
		if (string.IsNullOrWhiteSpace(_comfyRootPath))
		{
			_comfyRootPath = ComfyPathResolver.ResolveConfiguredComfyPath();
		}

		ConfigureMediaWatcher();
		UpdateTabState();
		UpdateToolbarState();
		UpdateStatus(GetActiveRootPath());
		await GetSurface(MediaAssetScope.Output).CellPool!.PrewarmAsync(MediaCellPrewarmCount, MediaCachedThumbnailBatchSize, prewarmToken);
		await Task.Yield();
		prewarmToken.ThrowIfCancellationRequested();
		await GetSurface(MediaAssetScope.Input).CellPool!.PrewarmAsync(MediaCellPrewarmCount, MediaCachedThumbnailBatchSize, prewarmToken);
	}

	void IRailToolView.PrepareOpenShell()
	{
		_isReady = false;
		_loadingOverlay.Show();
	}

	async Task IRailToolView.OpenAsync(CancellationToken cancellationToken)
	{
		var perf = RailPerformanceDiagnostics.Start();
		_isReady = false;
		_isRailActive = true;
		_isRenderDeferred = false;
		ConfigureMediaWatcher();
		RailPerformanceDiagnostics.Mark("MediaOpenActiveRefreshStart", perf, $"scope={_activeScope}");
		await RefreshAsync(cancellationToken, showLoadingOverlay: true);
		RailPerformanceDiagnostics.Mark("MediaOpenActiveRefreshCompleted", perf, $"scope={_activeScope}");
		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}

		_isReady = true;
		_directoryWatcher.MarkReconciled();
		_directoryWatcher.SetActive(true);
		RailPerformanceDiagnostics.Mark("MediaOpenReady", perf);
		_ = RefreshInactiveScopeAfterOpenAsync(GetInactiveScope(), perf);
	}

	private MediaAssetScope GetInactiveScope()
		=> _activeScope == MediaAssetScope.Output
			? MediaAssetScope.Input
			: MediaAssetScope.Output;

	private async Task RefreshInactiveScopeAfterOpenAsync(MediaAssetScope scope, long parentPerf)
	{
		try
		{
			RailPerformanceDiagnostics.Mark("MediaOpenInactiveRefreshQueued", parentPerf, $"scope={scope}");
			await RefreshScopeAsync(scope, CancellationToken.None, showLoadingOverlay: false);
			RailPerformanceDiagnostics.Mark("MediaOpenInactiveRefreshCompleted", parentPerf, $"scope={scope}");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Warning($"Media inactive refresh failed: {ex.Message}");
		}
	}

	void IRailToolView.ResetPresentation()
	{
		_isReady = false;
		_isRailActive = false;
		_directoryWatcher.SetActive(false);
		ClearSelection();
		SetRenderDeferred(true);
	}

	internal void SetRenderDeferred(bool isDeferred)
	{
		if (_isRenderDeferred == isDeferred)
		{
			return;
		}

		_isRenderDeferred = isDeferred;
		if (isDeferred)
		{
			_directoryWatcher.SetActive(false);
		}

		if (!isDeferred)
		{
			_ = RefreshAsync(CancellationToken.None, showLoadingOverlay: true);
		}
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
		if (_lifetimeCts.IsCancellationRequested)
		{
			_lifetimeCts.Dispose();
			_lifetimeCts = new CancellationTokenSource();
		}

		if (string.IsNullOrWhiteSpace(_comfyRootPath))
		{
			_comfyRootPath = ComfyPathResolver.ResolveConfiguredComfyPath();
		}

		UpdateTabState();
		UpdateToolbarState();
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
		_lifetimeCts.Cancel();
		_isRailActive = false;
		_directoryWatcher.Dispose();
	}

	private Task RefreshAsync(CancellationToken externalCancellationToken, bool showLoadingOverlay = true, bool forceRefresh = false)
	{
		return RefreshScopeAsync(_activeScope, externalCancellationToken, showLoadingOverlay, forceRefresh);
	}

	private Task RefreshScopeAsync(
		MediaAssetScope scope,
		CancellationToken externalCancellationToken,
		bool showLoadingOverlay = true,
		bool forceRefresh = false)
	{
		var surface = GetSurface(scope);
		string rootPath = GetRootPath(scope);
		string cacheKey = BuildCacheKey(rootPath);
		if (surface.IsBusy)
		{
			if (forceRefresh || IsCacheDirty(cacheKey))
			{
				surface.RefreshRequestedAfterCurrent = true;
				MarkCacheDirty(rootPath);
			}

			return surface.RefreshTask ?? Task.CompletedTask;
		}

		if (!forceRefresh &&
			surface.RefreshTask is { IsCompleted: false } &&
			string.Equals(surface.ActiveRefreshKey, cacheKey, StringComparison.OrdinalIgnoreCase))
		{
			return surface.RefreshTask;
		}

		surface.ActiveRefreshKey = cacheKey;
		surface.RefreshTask = RefreshCoreAsync(surface, rootPath, cacheKey, externalCancellationToken, showLoadingOverlay, forceRefresh);
		return surface.RefreshTask;
	}

	private async Task RefreshCoreAsync(
		MediaAssetScopeSurface surface,
		string rootPath,
		string cacheKey,
		CancellationToken externalCancellationToken,
		bool showLoadingOverlay,
		bool forceRefresh)
	{
		CancellationToken cancellationToken = externalCancellationToken;
		bool shouldShowOverlay = showLoadingOverlay && surface.Scope == _activeScope;
		var perf = RailPerformanceDiagnostics.Start();

		try
		{
			surface.IsBusy = true;
			RailPerformanceDiagnostics.Mark("MediaRefreshStart", perf, $"scope={surface.Scope}, force={forceRefresh}, overlay={shouldShowOverlay}");
			if (shouldShowOverlay)
			{
				await _loadingOverlay.ShowAsync();
				RailPerformanceDiagnostics.Mark("MediaRefreshOverlayShown", perf, $"scope={surface.Scope}");
			}

			IReadOnlyList<MediaAssetEntry> entries;
			IReadOnlyList<MediaAssetSourceEntry> sourceEntries;
			IReadOnlyList<string> missingOutputJobIds = [];
			if (!forceRefresh && TryGetCachedEntries(cacheKey, rootPath, out entries))
			{
				RailPerformanceDiagnostics.Mark("MediaRefreshCacheHit", perf, $"scope={surface.Scope}, entries={entries.Count}");
				entries = PruneMissingEntries(entries);
				sourceEntries = ToSourceEntries(entries);
				SetCachedEntries(cacheKey, rootPath, entries);
			}
			else
			{
				RailPerformanceDiagnostics.Mark("MediaRefreshSourceScanStart", perf, $"scope={surface.Scope}");
				MediaAssetSortDirection sortDirection = _sortDirection;
				bool useSyncedJobs = surface.Scope == MediaAssetScope.Output && _hasSyncedOutputJobs;
				var syncedJobs = useSyncedJobs ? _syncedOutputJobs.ToArray() : [];
				sourceEntries = await Task.Run(
					() =>
					{
						if (surface.Scope != MediaAssetScope.Output)
						{
							return ScanMediaFiles(rootPath, sortDirection, cancellationToken);
						}

						var result = BuildSourceEntriesFromJobs(rootPath, syncedJobs, sortDirection, cancellationToken);
						missingOutputJobIds = result.MissingJobIds;
						return result.Entries;
					},
					cancellationToken);
				RailPerformanceDiagnostics.Mark("MediaRefreshSourceScanCompleted", perf, $"scope={surface.Scope}, sources={sourceEntries.Count}");
				entries = await _thumbnailCache.BuildEntriesAsync(sourceEntries, cancellationToken);
				RailPerformanceDiagnostics.Mark("MediaRefreshEntriesBuilt", perf, $"scope={surface.Scope}, entries={entries.Count}");
				SetCachedEntries(cacheKey, rootPath, entries);
				await _thumbnailCache.CleanupStaleThumbnailsAsync(cancellationToken);
				RailPerformanceDiagnostics.Mark("MediaRefreshThumbnailCleanupCompleted", perf, $"scope={surface.Scope}");
			}

			entries = SortEntries(entries);
			sourceEntries = SortSourceEntries(sourceEntries);

			if (cancellationToken.IsCancellationRequested || !IsSurfaceCacheKey(surface, cacheKey))
			{
				surface.RefreshRequestedAfterCurrent = true;
				return;
			}

			if (surface.Scope == _activeScope)
			{
				UpdateStatus(rootPath);
			}

			RailPerformanceDiagnostics.Mark("MediaRenderStart", perf, $"scope={surface.Scope}, entries={entries.Count}");
			await RenderItemsAsync(surface, entries, Interlocked.Increment(ref surface.RenderVersion));
			RailPerformanceDiagnostics.Mark("MediaRenderCompleted", perf, $"scope={surface.Scope}, entries={entries.Count}");
			CleanupStaleOutputJobs(missingOutputJobIds);
			if (shouldShowOverlay)
			{
				await _loadingOverlay.HideAsync();
				RailPerformanceDiagnostics.Mark("MediaRefreshOverlayHidden", perf, $"scope={surface.Scope}");
			}

			await LoadThumbnailsAsync(surface, cacheKey, sourceEntries, entries, cancellationToken);
			RailPerformanceDiagnostics.Mark("MediaThumbnailsQueuedCompleted", perf, $"scope={surface.Scope}, entries={entries.Count}");
		}
		catch (OperationCanceledException)
		{
			// Refreshes are intentionally cancelled when the user switches tabs or closes the rail.
			RailPerformanceDiagnostics.Mark("MediaRefreshCanceled", perf, $"scope={surface.Scope}");
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"Media assets refresh failed: {ex.Message}");
			if (surface.Scope == _activeScope)
			{
				UpdateStatus(GetActiveRootPath());
			}

			await RenderItemsAsync(surface, [], Interlocked.Increment(ref surface.RenderVersion));
		}
		finally
		{
			surface.IsBusy = false;
			if (shouldShowOverlay)
			{
				await _loadingOverlay.HideAsync();
			}

			if (string.Equals(surface.ActiveRefreshKey, cacheKey, StringComparison.OrdinalIgnoreCase))
			{
				surface.RefreshTask = null;
				surface.ActiveRefreshKey = null;
			}

			if (surface.RefreshRequestedAfterCurrent && !externalCancellationToken.IsCancellationRequested)
			{
				surface.RefreshRequestedAfterCurrent = false;
				_ = RefreshScopeAsync(surface.Scope, CancellationToken.None, showLoadingOverlay: surface.Scope == _activeScope, forceRefresh: true);
				RailPerformanceDiagnostics.Mark("MediaRefreshPendingRestartQueued", perf, $"scope={surface.Scope}");
			}
		}
	}

	private bool TryGetCachedEntries(string cacheKey, string rootPath, out IReadOnlyList<MediaAssetEntry> entries)
	{
		entries = [];
		lock (_entryCacheGate)
		{
			if (_dirtyCacheKeys.Contains(cacheKey) ||
				!Directory.Exists(rootPath) ||
				!_entryCache.TryGetValue(cacheKey, out var snapshot))
			{
				return false;
			}

			entries = snapshot.Entries;
			return true;
		}
	}

	private void CleanupStaleOutputJobs(IReadOnlyList<string> jobIds)
	{
		if (jobIds.Count == 0 || _staleOutputJobCleanupHandler == null)
		{
			return;
		}

		var stableJobIds = jobIds
			.Where(jobId => !string.IsNullOrWhiteSpace(jobId))
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (stableJobIds.Count == 0)
		{
			return;
		}

		NexusLog.Trace($"[MEDIA_ASSETS] Stale output jobs detected: {stableJobIds.Count}");
		_ = Task.Run(async () =>
		{
			try
			{
				await _staleOutputJobCleanupHandler(stableJobIds);
			}
			catch (Exception ex)
			{
				NexusLog.Warning($"Media assets stale job cleanup failed: {ex.GetType().Name} - {ex.Message}");
			}
		});
	}

	private void SetCachedEntries(string cacheKey, string rootPath, IReadOnlyList<MediaAssetEntry> entries)
	{
		lock (_entryCacheGate)
		{
			_entryCache[cacheKey] = new MediaAssetCacheSnapshot(rootPath, entries);
			_dirtyCacheKeys.Remove(cacheKey);
		}
	}

	private bool IsCacheDirty(string cacheKey)
	{
		lock (_entryCacheGate)
		{
			return _dirtyCacheKeys.Contains(cacheKey);
		}
	}

	private static IReadOnlyList<MediaAssetEntry> PruneMissingEntries(IReadOnlyList<MediaAssetEntry> entries)
	{
		var aliveEntries = new List<MediaAssetEntry>(entries.Count);
		foreach (var entry in entries)
		{
			if (!File.Exists(entry.FullPath))
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(entry.ThumbnailPath) && !File.Exists(entry.ThumbnailPath))
			{
				entry.SetThumbnailPath(null);
			}

			aliveEntries.Add(entry);
		}

		return aliveEntries;
	}

	private static IReadOnlyList<MediaAssetSourceEntry> ToSourceEntries(IReadOnlyList<MediaAssetEntry> entries)
		=> entries
			.Select(entry => new MediaAssetSourceEntry(
				entry.Name,
				entry.FullPath,
				entry.CreatedAt,
				entry.ModifiedAt,
				entry.PixelWidth,
				entry.PixelHeight,
				entry.JobId,
				entry.IsBatchInferred,
				entry.Type,
				entry.Subfolder,
				entry.SizeBytes))
			.ToList();

	private static string BuildCacheKey(string rootPath)
		=> string.IsNullOrWhiteSpace(rootPath) ? string.Empty : Path.GetFullPath(rootPath);

	private bool IsSurfaceCacheKey(MediaAssetScopeSurface surface, string cacheKey)
		=> string.Equals(cacheKey, BuildCacheKey(GetRootPath(surface.Scope)), StringComparison.OrdinalIgnoreCase);

	private void MarkCacheDirty(string rootPath)
	{
		string cacheKey = BuildCacheKey(rootPath);
		if (!string.IsNullOrWhiteSpace(cacheKey))
		{
			lock (_entryCacheGate)
			{
				_dirtyCacheKeys.Add(cacheKey);
			}
		}
	}

	private MediaAssetCell CreateMediaAssetCell()
	{
		var thumbnail = new Image
		{
			Aspect = Aspect.AspectFill,
			Opacity = 0.96,
		};

		var loadingBadge = new Border
		{
			BackgroundColor = MediaLoadingBadgeBackgroundColor,
			Stroke = MediaLoadingBadgeStrokeColor,
			StrokeThickness = 1,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
			Padding = new Thickness(6, 3),
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
			Content = new HorizontalStackLayout
			{
				Spacing = 5,
				Children =
				{
					new ActivityIndicator
					{
						IsRunning = true,
						Color = MediaLoadingIndicatorColor,
						WidthRequest = 14,
						HeightRequest = 14,
						VerticalOptions = LayoutOptions.Center,
					},
					new Label
					{
						Text = "Loading",
						TextColor = MediaLoadingTextColor,
						FontSize = 8,
						FontAttributes = FontAttributes.Bold,
						VerticalTextAlignment = TextAlignment.Center,
					},
				},
			},
		};

		var thumbnailHost = new Border
		{
			BackgroundColor = MediaThumbnailHostBackgroundColor,
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(9) },
		};

		var nameLabel = new Label
		{
			TextColor = MediaTitleTextColor,
			FontSize = 11,
			FontAttributes = FontAttributes.Bold,
			LineBreakMode = LineBreakMode.TailTruncation,
		};

		var detailLabel = new Label
		{
			TextColor = MediaDetailTextColor,
			FontSize = 9,
			LineBreakMode = LineBreakMode.TailTruncation,
		};

		var textHost = new Border
		{
			Background = MediaTextOverlayBackground,
			StrokeThickness = 0,
			Padding = new Thickness(8, 18, 8, 7),
			VerticalOptions = LayoutOptions.End,
			Content = new VerticalStackLayout
			{
				Spacing = 1,
				Children =
				{
					nameLabel,
					detailLabel,
				},
			},
		};

		var resolutionLabel = new Label
		{
			TextColor = NexusColors.TextSoft,
			FontSize = 9,
			FontAttributes = FontAttributes.Bold,
			LineBreakMode = LineBreakMode.TailTruncation,
			HorizontalTextAlignment = TextAlignment.Center,
			VerticalTextAlignment = TextAlignment.Center,
		};

		var resolutionBadge = new Border
		{
			BackgroundColor = MediaResolutionBadgeBackgroundColor,
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
			Padding = new Thickness(6, 3),
			Margin = new Thickness(0),
			HorizontalOptions = LayoutOptions.End,
			VerticalOptions = LayoutOptions.Start,
			IsVisible = false,
			Content = resolutionLabel,
		};

		var imageOverlay = new Grid
		{
			Children =
			{
				thumbnail,
				loadingBadge,
				resolutionBadge,
				textHost,
			},
		};

		var thumbnailRow = new RowDefinition { Height = new GridLength(96) };
		var content = new Grid
		{
			RowDefinitions =
			{
				thumbnailRow,
			},
		};
		thumbnailHost.Content = imageOverlay;
		content.Add(thumbnailHost, 0, 0);

		var root = new Border
		{
			BackgroundColor = MediaCellBackgroundColor,
			Stroke = MediaCellStrokeColor,
			StrokeThickness = 1,
			Padding = 0,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(9) },
			Content = content,
		};

		var cell = new MediaAssetCell(
			root,
			thumbnail,
			thumbnailRow,
			loadingBadge,
			nameLabel,
			detailLabel,
			textHost,
			resolutionBadge,
			resolutionLabel,
			entry => _ = OpenAssetAsync(entry),
			entry => _ = RevealAssetAsync(entry),
			entry => _ = BeginCopySelectedAsync(entry),
			entry => _ = MoveSelectionAsync(entry),
			entry => _ = CopySelectedAssetPathsAsync(entry),
			entry => _ = RenameSelectionAsync(entry),
			entry => _ = DeleteSelectionAsync(entry),
			path => _clipboard.ShouldDim(path));

		root.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => OnPooledAssetTapped(cell)) });
		root.GestureRecognizers.Add(new TapGestureRecognizer
		{
			NumberOfTapsRequired = 2,
			Command = new Command(() => _ = OpenAssetAsync(cell.Entry)),
		});
		var pointerGesture = new PointerGestureRecognizer();
		pointerGesture.PointerEntered += (_, _) => ApplyPooledAssetHover(cell, true);
		pointerGesture.PointerExited += (_, _) => ApplyPooledAssetHover(cell, false);
		root.GestureRecognizers.Add(pointerGesture);

		return cell;
	}

	private static void ResetMediaAssetCell(MediaAssetCell cell) => cell.Clear();

	private static MenuFlyout CreateMediaAssetFlyout(MediaAssetCell cell)
	{
		var flyout = new MenuFlyout();
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("common.open"), (_, _) => cell.Open()));
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("context_menu.reveal_in_explorer"), (_, _) => cell.Reveal()));
		flyout.Add(new MenuFlyoutSeparator());
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("common.copy"), (_, _) => cell.Copy()));
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move_to"), (_, _) => cell.Move()));
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("context_menu.copy_path"), (_, _) => cell.CopyPath()));
		flyout.Add(new MenuFlyoutSeparator());
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("common.rename"), (_, _) => cell.Rename()));
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("common.delete"), (_, _) => cell.Delete()));
		return flyout;
	}

	private static MenuFlyoutItem CreateFlyoutItem(string text, EventHandler handler)
	{
		var item = new MenuFlyoutItem { Text = text };
		item.Clicked += handler;
		return item;
	}

	private void OnPooledAssetTapped(MediaAssetCell cell)
	{
		if (cell.Entry == null)
		{
			return;
		}

		SelectFromPointer(cell.Entry.FullPath);
	}

	private static void ApplyPooledAssetHover(MediaAssetCell cell, bool isHovered)
	{
		if (cell.Entry?.IsSelected == true)
		{
			return;
		}

		cell.Root.BackgroundColor = isHovered ? MediaCellHoverBackgroundColor : MediaCellBackgroundColor;
		cell.Root.Stroke = isHovered ? MediaCellHoverStrokeColor : MediaCellStrokeColor;
	}

	private async Task RenderItemsAsync(MediaAssetScopeSurface surface, IReadOnlyList<MediaAssetEntry> entries, int renderVersion)
	{
		try
		{
			if (renderVersion != surface.RenderVersion)
			{
				return;
			}

			string nextSignature = BuildRenderSignature(entries);
			surface.SourceItems.Clear();
			surface.SourceItems.AddRange(entries);
			await RenderProjectedItemsAsync(surface, renderVersion, nextSignature);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MEDIA_ASSETS] Failed to render media assets");
			if (renderVersion == surface.RenderVersion)
			{
				surface.SourceItems.Clear();
				surface.Items.Clear();
				surface.RenderedSignature = string.Empty;
				UpdateVirtualGrid(surface);
			}
		}
	}

	private async Task RenderProjectedItemsAsync(MediaAssetScopeSurface surface, int renderVersion, string? sourceSignature = null)
	{
		if (renderVersion != surface.RenderVersion)
		{
			return;
		}

		var entries = ProjectEntries(surface.SourceItems);
		string nextSignature = $"{sourceSignature ?? BuildRenderSignature(surface.SourceItems)}\nFILTER:{_searchText}\n{BuildRenderSignature(entries)}";
		if (string.Equals(surface.RenderedSignature, nextSignature, StringComparison.Ordinal))
		{
			SyncVisibleSelectionState();
			UpdateVirtualGrid(surface);
			return;
		}

		try
		{
			surface.Items.Clear();
			for (int index = 0; index < entries.Count; index++)
			{
				if (renderVersion != surface.RenderVersion)
				{
					return;
				}

				surface.Items.Add(entries[index]);
				if ((index + 1) % MediaCachedThumbnailBatchSize == 0)
				{
					await Task.Yield();
				}
			}

			surface.RenderedSignature = nextSignature;
			_selection.Normalize();
			SyncVisibleSelectionState();
			UpdateVirtualGrid(surface);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MEDIA_ASSETS] Failed to render projected media assets");
			if (renderVersion == surface.RenderVersion)
			{
				surface.Items.Clear();
				surface.RenderedSignature = string.Empty;
				UpdateVirtualGrid(surface);
			}
		}
	}

	private static string BuildRenderSignature(IReadOnlyList<MediaAssetEntry> entries)
		=> string.Join(
			"\n",
			entries.Select(entry => $"{entry.FullPath}|{entry.CreatedAt.Ticks}|{entry.ModifiedAt.Ticks}|{entry.SizeBytes}"));

	private IReadOnlyList<MediaAssetEntry> ProjectEntries(IReadOnlyList<MediaAssetEntry> entries)
	{
		IEnumerable<MediaAssetEntry> projected = entries;
		if (!string.IsNullOrWhiteSpace(_searchText))
		{
			projected = projected.Where(entry => entry.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
		}

		return SortEntries(projected.ToList());
	}

	private MediaAssetScopeSurface GetActiveSurface() => GetSurface(_activeScope);

	private MediaAssetScopeSurface GetSurface(MediaAssetScope scope)
		=> scope == MediaAssetScope.Output
			? _outputSurface ?? throw new InvalidOperationException("Output media surface is not initialized.")
			: _inputSurface ?? throw new InvalidOperationException("Input media surface is not initialized.");

	private void UpdateVirtualGrid(MediaAssetScopeSurface surface)
	{
		UpdateActiveSurfaceVisibility();
		if (surface.Items.Count == 0)
		{
			surface.AttachVersion++;
			ReturnAllVisibleCells(surface);
			surface.Spacer.HeightRequest = 0;
			surface.Canvas.HeightRequest = 0;
			surface.Canvas.TranslationY = 0;
			surface.Surface.ClearValue(HeightRequestProperty);
			surface.VirtualLayoutSignature = string.Empty;
			return;
		}

		int columnCount = Math.Clamp(_mediaGridColumns, 1, 3);
		double viewportHeight = Math.Max(1, surface.ScrollView.Height);
		double viewportWidth = Math.Max(1, surface.ScrollView.Width - MediaScrollbarGutter);
		double cellWidth = Math.Max(58, (viewportWidth - MediaCardGap * (columnCount - 1)) / columnCount);
		double cardHeight = cellWidth;
		int rowCount = (int)Math.Ceiling(surface.Items.Count / (double)columnCount);
		double stride = cardHeight + MediaCardGap;
		double totalHeight = Math.Max(0, rowCount * cardHeight + Math.Max(0, rowCount - 1) * MediaCardGap);
		surface.Spacer.WidthRequest = viewportWidth;
		surface.Canvas.WidthRequest = viewportWidth;
		surface.Surface.WidthRequest = viewportWidth;
		surface.Spacer.HeightRequest = totalHeight;

		double maxScrollY = Math.Max(0, totalHeight - viewportHeight);
		double scrollY = Math.Clamp(Math.Max(0, surface.ScrollView.ScrollY), 0, maxScrollY);
		int firstRow = Math.Min(rowCount - 1, Math.Max(0, (int)Math.Floor(scrollY / stride) - MediaGridBufferRows));
		int lastRow = Math.Min(rowCount - 1, Math.Max(firstRow, (int)Math.Ceiling((scrollY + viewportHeight) / stride) + MediaGridBufferRows));
		double renderTop = firstRow * stride;
		double renderHeight = Math.Max(cardHeight, (lastRow - firstRow) * stride + cardHeight);
		surface.Canvas.HeightRequest = renderHeight;
		surface.Canvas.TranslationY = renderTop;
		surface.Surface.ClearValue(HeightRequestProperty);

		string layoutSignature = $"{surface.Items.Count}|{columnCount}|{firstRow}|{lastRow}|{viewportWidth:0.###}|{viewportHeight:0.###}|{cellWidth:0.###}|{cardHeight:0.###}|{renderTop:0.###}|{renderHeight:0.###}";
		if (string.Equals(surface.VirtualLayoutSignature, layoutSignature, StringComparison.Ordinal))
		{
			return;
		}

		surface.VirtualLayoutSignature = layoutSignature;
		int attachVersion = ++surface.AttachVersion;

		var requiredIndices = new HashSet<int>();
		for (int row = firstRow; row <= lastRow; row++)
		{
			for (int column = 0; column < columnCount; column++)
			{
				int itemIndex = row * columnCount + column;
				if (itemIndex < surface.Items.Count)
				{
					requiredIndices.Add(itemIndex);
				}
			}
		}

		foreach (int index in surface.VisibleCells.Keys.Where(index => !requiredIndices.Contains(index)).ToList())
		{
			var cell = surface.VisibleCells[index];
			surface.Canvas.Children.Remove(cell.Root);
			cell.Root.CancelAnimations();
			surface.CellPool!.Return(cell);
			surface.VisibleCells.Remove(index);
		}

		var missingIndices = new List<int>();
		foreach (int index in requiredIndices.OrderBy(index => index))
		{
			if (surface.VisibleCells.TryGetValue(index, out var cell))
			{
				BindMediaAssetCell(surface, cell, index, columnCount, cellWidth, cardHeight, renderTop);
				continue;
			}

			missingIndices.Add(index);
		}

		// First paint is attached synchronously so the rail never flashes a single
		// center-prioritized cell before the rest of the visible grid arrives.
		if (missingIndices.Count > 0)
		{
			if (surface.VisibleCells.Count == 0)
			{
				AttachMissingMediaCellsImmediately(
					surface,
					missingIndices,
					columnCount,
					cellWidth,
					cardHeight,
					renderTop,
					attachVersion);
				return;
			}

			_ = AttachMissingMediaCellsAsync(
				surface,
				PrioritizeMediaAttachIndexes(missingIndices, scrollY, viewportHeight, columnCount, cardHeight),
				columnCount,
				cellWidth,
				cardHeight,
				renderTop,
				attachVersion,
				MediaAttachCoalesceDelayMs);
		}
	}

	private void AttachMissingMediaCellsImmediately(
		MediaAssetScopeSurface surface,
		IReadOnlyList<int> indices,
		int columnCount,
		double cellWidth,
		double cardHeight,
		double renderTop,
		int attachVersion)
	{
		foreach (int index in indices)
		{
			if (attachVersion != surface.AttachVersion || index < 0 || index >= surface.Items.Count)
			{
				return;
			}

			if (surface.VisibleCells.ContainsKey(index))
			{
				continue;
			}

			var cell = surface.CellPool!.Rent();
			cell.Root.CancelAnimations();
			cell.Root.Opacity = 1;
			cell.Root.Scale = 1;
			surface.VisibleCells[index] = cell;
			BindMediaAssetCell(surface, cell, index, columnCount, cellWidth, cardHeight, renderTop);
			surface.Canvas.Children.Add(cell.Root);
			EnsureMediaAssetFlyout(cell);
		}
	}

	private async Task AttachMissingMediaCellsAsync(
		MediaAssetScopeSurface surface,
		IReadOnlyList<int> indices,
		int columnCount,
		double cellWidth,
		double cardHeight,
		double renderTop,
		int attachVersion,
		int coalesceDelayMs)
	{
		if (coalesceDelayMs > 0)
		{
			await Task.Delay(coalesceDelayMs);
			if (attachVersion != surface.AttachVersion)
			{
				return;
			}
		}

		foreach (int index in indices)
		{
			if (attachVersion != surface.AttachVersion || index < 0 || index >= surface.Items.Count)
			{
				return;
			}

			if (surface.VisibleCells.ContainsKey(index))
			{
				continue;
			}

			var cell = surface.CellPool!.Rent();
			cell.Root.CancelAnimations();
			cell.Root.Opacity = 0;
			cell.Root.Scale = 0.985;
			surface.VisibleCells[index] = cell;
			BindMediaAssetCell(surface, cell, index, columnCount, cellWidth, cardHeight, renderTop);
			surface.Canvas.Children.Add(cell.Root);
			EnsureMediaAssetFlyout(cell);
			_ = RevealMediaAssetCellAsync(surface, cell, attachVersion);
			await Task.Yield();
		}
	}

	private async Task RevealMediaAssetCellAsync(MediaAssetScopeSurface surface, MediaAssetCell cell, int attachVersion)
	{
		try
		{
			await Task.WhenAll(
				cell.Root.FadeToAsync(1, 90, Easing.CubicOut),
				cell.Root.ScaleToAsync(1, 90, Easing.CubicOut));
		}
		finally
		{
			if (attachVersion == surface.AttachVersion)
			{
				cell.Root.Opacity = 1;
				cell.Root.Scale = 1;
			}
		}
	}

	private static IReadOnlyList<int> PrioritizeMediaAttachIndexes(
		IReadOnlyList<int> indices,
		double scrollY,
		double viewportHeight,
		int columnCount,
		double cardHeight)
	{
		double stride = cardHeight + MediaCardGap;
		double viewportCenter = scrollY + viewportHeight / 2;
		return indices
			.OrderBy(index => IsMediaIndexInsideViewport(index, scrollY, viewportHeight, columnCount, stride) ? 0 : 1)
			.ThenBy(index => Math.Abs((index / columnCount) * stride + cardHeight / 2 - viewportCenter))
			.ToArray();
	}

	private static bool IsMediaIndexInsideViewport(int index, double scrollY, double viewportHeight, int columnCount, double stride)
	{
		double top = (index / columnCount) * stride;
		double bottom = top + stride;
		return bottom >= scrollY && top <= scrollY + viewportHeight;
	}

	private static void BindMediaAssetCell(
		MediaAssetScopeSurface surface,
		MediaAssetCell cell,
		int index,
		int columnCount,
		double cellWidth,
		double cardHeight,
		double renderTop)
	{
		int row = index / columnCount;
		int column = index % columnCount;
		cell.Bind(surface.Items[index]);
		double x = column * (cellWidth + MediaCardGap);
		double y = row * (cardHeight + MediaCardGap) - renderTop;
		cell.Resize(cellWidth, cardHeight, showMetadataOverlay: columnCount < 3);
		AbsoluteLayout.SetLayoutBounds(cell.Root, new Rect(x, y, cellWidth, cardHeight));
		AbsoluteLayout.SetLayoutFlags(cell.Root, AbsoluteLayoutFlags.None);
	}

	private static void EnsureMediaAssetFlyout(MediaAssetCell cell)
	{
		if (FlyoutBase.GetContextFlyout(cell.Root) == null)
		{
			FlyoutBase.SetContextFlyout(cell.Root, CreateMediaAssetFlyout(cell));
		}
	}

	private void ReturnAllVisibleCells(MediaAssetScopeSurface surface)
	{
		surface.AttachVersion++;
		foreach (var cell in surface.VisibleCells.Values)
		{
			surface.Canvas.Children.Remove(cell.Root);
			cell.Root.CancelAnimations();
			surface.CellPool!.Return(cell);
		}

		surface.VisibleCells.Clear();
	}

	private async Task LoadThumbnailsAsync(
		MediaAssetScopeSurface surface,
		string cacheKey,
		IReadOnlyList<MediaAssetSourceEntry> sourceEntries,
		IReadOnlyList<MediaAssetEntry> entries,
		CancellationToken cancellationToken)
	{
		if (sourceEntries.Count == 0 || entries.Count == 0)
		{
			return;
		}

		var entryByPath = entries.ToDictionary(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase);

		var missingThumbnailSources = new List<MediaAssetSourceEntry>();
		var cachedThumbnailSources = OrderSourcesByViewport(cacheKey, sourceEntries, entries);
		for (int index = 0; index < cachedThumbnailSources.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsSurfaceCacheKey(surface, cacheKey))
			{
				surface.RefreshRequestedAfterCurrent = true;
				return;
			}

			var sourceEntry = cachedThumbnailSources[index];
			if (!entryByPath.TryGetValue(sourceEntry.FullPath, out var entry) || !File.Exists(sourceEntry.FullPath))
			{
				continue;
			}

			string? existingThumbnailPath = _thumbnailCache.GetExistingThumbnailPath(sourceEntry);
			if (!string.IsNullOrWhiteSpace(existingThumbnailPath))
			{
				await MainThread.InvokeOnMainThreadAsync(() => entry.SetThumbnailPath(existingThumbnailPath));

				if ((index + 1) % MediaCachedThumbnailBatchSize == 0)
				{
					await Task.Yield();
				}

				continue;
			}

			missingThumbnailSources.Add(sourceEntry);
		}

		int createdCount = 0;
		while (missingThumbnailSources.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsSurfaceCacheKey(surface, cacheKey))
			{
				surface.RefreshRequestedAfterCurrent = true;
				return;
			}

			if (createdCount % MediaThumbnailReprioritizeInterval == 0)
			{
				missingThumbnailSources = OrderSourcesByViewport(cacheKey, missingThumbnailSources, entries);
			}

			var sourceEntry = missingThumbnailSources[0];
			missingThumbnailSources.RemoveAt(0);
			if (!entryByPath.TryGetValue(sourceEntry.FullPath, out var entry) || !File.Exists(sourceEntry.FullPath))
			{
				continue;
			}

			string? thumbnailPath = await Task.Run(
				() => _thumbnailCache.EnsureThumbnailAsync(sourceEntry, cancellationToken),
				cancellationToken);

			if (!string.IsNullOrWhiteSpace(thumbnailPath))
			{
				await MainThread.InvokeOnMainThreadAsync(() => entry.SetThumbnailPath(thumbnailPath));
			}

			createdCount++;
			if (createdCount % MediaCreateThumbnailBatchSize == 0)
			{
				await Task.Yield();
			}
		}

	}

	private List<MediaAssetSourceEntry> OrderSourcesByViewport(
		string cacheKey,
		IReadOnlyList<MediaAssetSourceEntry> sourceEntries,
		IReadOnlyList<MediaAssetEntry> entries)
	{
		MediaAssetScopeSurface? surface = TryGetSurfaceForCacheKey(cacheKey);
		if (surface == null || surface.VisibleCells.Count == 0)
		{
			return sourceEntries.ToList();
		}

		var visiblePaths = new HashSet<string>(
			surface.VisibleCells.Values
				.Select(cell => cell.Entry?.FullPath)
				.Where(path => !string.IsNullOrWhiteSpace(path))!,
			StringComparer.OrdinalIgnoreCase);

		if (visiblePaths.Count == 0)
		{
			return sourceEntries.ToList();
		}

		var entryOrder = entries
			.Select((entry, index) => (entry.FullPath, Index: index))
			.ToDictionary(item => item.FullPath, item => item.Index, StringComparer.OrdinalIgnoreCase);

		return sourceEntries
			.OrderByDescending(entry => visiblePaths.Contains(entry.FullPath))
			.ThenBy(entry => entryOrder.TryGetValue(entry.FullPath, out int index) ? index : int.MaxValue)
			.ToList();
	}

	private MediaAssetScopeSurface? TryGetSurfaceForCacheKey(string cacheKey)
	{
		string outputKey = BuildCacheKey(GetRootPath(MediaAssetScope.Output));
		if (string.Equals(cacheKey, outputKey, StringComparison.OrdinalIgnoreCase))
		{
			return GetSurface(MediaAssetScope.Output);
		}

		string inputKey = BuildCacheKey(GetRootPath(MediaAssetScope.Input));
		return string.Equals(cacheKey, inputKey, StringComparison.OrdinalIgnoreCase)
			? GetSurface(MediaAssetScope.Input)
			: null;
	}

	private static IReadOnlyList<MediaAssetSourceEntry> ScanMediaFiles(
		string rootPath,
		MediaAssetSortDirection sortDirection,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
		{
			return [];
		}

		var entries = new List<MediaAssetSourceEntry>();
		SearchOption searchOption = rootPath.EndsWith(
			$"{Path.DirectorySeparatorChar}{ComfyPathOptions.InputDirectoryName}",
			StringComparison.OrdinalIgnoreCase)
			? SearchOption.TopDirectoryOnly
			: SearchOption.AllDirectories;

		foreach (string filePath in Directory.EnumerateFiles(rootPath, "*", searchOption))
		{
			if (cancellationToken.IsCancellationRequested)
			{
				break;
			}

			if (!SupportedImageExtensions.Contains(Path.GetExtension(filePath)) || !File.Exists(filePath))
			{
				continue;
			}

			try
			{
				var info = new FileInfo(filePath);
				if (!info.Exists || info.Length > MaxPreviewFileBytes)
				{
					continue;
				}

				entries.Add(new MediaAssetSourceEntry(
					info.Name,
					info.FullName,
					info.CreationTime,
					info.LastWriteTime,
					TryReadImageDimensions(info.FullName, out int pixelWidth, out int pixelHeight) ? pixelWidth : null,
					pixelWidth > 0 && pixelHeight > 0 ? pixelHeight : null,
					null,
					false,
					"output",
					string.Empty,
					info.Length));
			}
			catch (IOException)
			{
				// Files may still be writing; the watcher will schedule another refresh shortly.
			}
			catch (UnauthorizedAccessException)
			{
				// Keep the browser resilient when a generated file or folder is locked.
			}
		}

		IOrderedEnumerable<MediaAssetSourceEntry> orderedEntries = sortDirection == MediaAssetSortDirection.RecentFirst
			? entries
				.OrderByDescending(entry => entry.CreatedAt)
				.ThenByDescending(entry => entry.ModifiedAt)
				.ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
			: entries
				.OrderBy(entry => entry.CreatedAt)
				.ThenBy(entry => entry.ModifiedAt)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

		return orderedEntries.Take(MaxVisibleItems).ToList();
	}

	private static OutputJobBuildResult BuildSourceEntriesFromJobs(
		string outputRootPath,
		IReadOnlyList<MediaAssetJobPreview> jobs,
		MediaAssetSortDirection sortDirection,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(outputRootPath) || !Directory.Exists(outputRootPath))
		{
			return new OutputJobBuildResult([], []);
		}

		var entries = new List<MediaAssetSourceEntry>();
		var missingJobIds = new List<string>();
		var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var jobFileKeys = jobs
			.Where(job => IsOutputJob(job))
			.Select(GetJobFileKey)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var job in jobs.Where(IsOutputJob))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!AddJobFileIfExists(outputRootPath, job, entries, addedPaths, isBatchInferred: false) &&
				!string.IsNullOrWhiteSpace(job.JobId))
			{
				missingJobIds.Add(job.JobId);
				continue;
			}

			foreach (var batchJob in ExpandForwardBatch(outputRootPath, job, jobFileKeys, cancellationToken))
			{
				AddJobFileIfExists(outputRootPath, batchJob, entries, addedPaths, isBatchInferred: true);
			}
		}

		IOrderedEnumerable<MediaAssetSourceEntry> orderedEntries = sortDirection == MediaAssetSortDirection.RecentFirst
			? entries
				.OrderByDescending(entry => entry.CreatedAt)
				.ThenByDescending(entry => entry.ModifiedAt)
				.ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
			: entries
				.OrderBy(entry => entry.CreatedAt)
				.ThenBy(entry => entry.ModifiedAt)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

		return new OutputJobBuildResult(
			orderedEntries.Take(MaxVisibleItems).ToList(),
			missingJobIds.Distinct(StringComparer.Ordinal).ToList());
	}

	private static bool IsOutputJob(MediaAssetJobPreview job)
		=> string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(job.Type, "output", StringComparison.OrdinalIgnoreCase) &&
			!string.IsNullOrWhiteSpace(job.Filename);

	private static IEnumerable<MediaAssetJobPreview> ExpandForwardBatch(
		string outputRootPath,
		MediaAssetJobPreview job,
		HashSet<string> jobFileKeys,
		CancellationToken cancellationToken)
	{
		var parsed = ParseSequenceFilename(job.Filename);
		if (parsed == null)
		{
			yield break;
		}

		for (int offset = 1; offset <= MediaJobBatchForwardLimit; offset++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int nextNumber = parsed.Value.Number + offset;
			string filename = $"{parsed.Value.Prefix}{nextNumber.ToString().PadLeft(parsed.Value.Width, '0')}{parsed.Value.Suffix}";
			var candidate = job with { JobId = string.Empty, Filename = filename };
			if (jobFileKeys.Contains(GetJobFileKey(candidate)))
			{
				continue;
			}

			string? fullPath = ResolveJobOutputPath(outputRootPath, candidate);
			if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
			{
				continue;
			}

			yield return candidate;
		}
	}

	private static string GetJobFileKey(MediaAssetJobPreview job)
		=> $"{job.Type}|{job.Subfolder}|{job.Filename}";

	private static SequenceFilename? ParseSequenceFilename(string filename)
	{
		var match = Regex.Match(filename, @"^(.*?)(\d+)(_?\.[^.]+)$", RegexOptions.CultureInvariant);
		if (!match.Success || !int.TryParse(match.Groups[2].Value, out int number))
		{
			return null;
		}

		return new SequenceFilename(
			match.Groups[1].Value,
			number,
			match.Groups[2].Value.Length,
			match.Groups[3].Value);
	}

	private static bool AddJobFileIfExists(
		string outputRootPath,
		MediaAssetJobPreview job,
		List<MediaAssetSourceEntry> entries,
		HashSet<string> addedPaths,
		bool isBatchInferred)
	{
		string? fullPath = ResolveJobOutputPath(outputRootPath, job);
		if (string.IsNullOrWhiteSpace(fullPath) || !addedPaths.Add(fullPath))
		{
			return false;
		}

		if (TryCreateSourceEntry(fullPath, out var sourceEntry))
		{
			entries.Add(sourceEntry with
			{
				JobId = isBatchInferred ? null : job.JobId,
				IsBatchInferred = isBatchInferred,
				Type = job.Type,
				Subfolder = job.Subfolder
			});
			return true;
		}

		return false;
	}

	private static string? ResolveJobOutputPath(string outputRootPath, MediaAssetJobPreview job)
	{
		try
		{
			string relativePath = string.IsNullOrWhiteSpace(job.Subfolder)
				? job.Filename
				: Path.Combine(job.Subfolder, job.Filename);
			string fullPath = Path.GetFullPath(Path.Combine(outputRootPath, relativePath));
			string rootPath = Path.GetFullPath(outputRootPath);
			return fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return null;
		}
	}

	private static bool TryCreateSourceEntry(string filePath, out MediaAssetSourceEntry sourceEntry)
	{
		sourceEntry = new MediaAssetSourceEntry(string.Empty, filePath, DateTime.MinValue, DateTime.MinValue, null, null, null, false, "output", string.Empty, 0);
		try
		{
			var info = new FileInfo(filePath);
			if (!info.Exists || info.Length > MaxPreviewFileBytes || !SupportedImageExtensions.Contains(info.Extension))
			{
				return false;
			}

			sourceEntry = new MediaAssetSourceEntry(
				info.Name,
				info.FullName,
				info.CreationTime,
				info.LastWriteTime,
				TryReadImageDimensions(info.FullName, out int pixelWidth, out int pixelHeight) ? pixelWidth : null,
				pixelWidth > 0 && pixelHeight > 0 ? pixelHeight : null,
				null,
				false,
				"output",
				string.Empty,
				info.Length);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool TryReadImageDimensions(string path, out int width, out int height)
	{
		width = 0;
		height = 0;

		try
		{
			Span<byte> header = stackalloc byte[32];
			using var stream = File.OpenRead(path);
			int read = stream.Read(header);
			if (read < 24)
			{
				return false;
			}

			if (header[0] == 0x89 && header[1] == (byte)'P' && header[2] == (byte)'N' && header[3] == (byte)'G')
			{
				width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
				height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
				return width > 0 && height > 0;
			}

			if (header[0] == 0xFF && header[1] == 0xD8)
			{
				return TryReadJpegDimensions(stream, out width, out height);
			}

			if (header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
				header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
			{
				return TryReadWebpDimensions(header, stream, out width, out height);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}

		return false;
	}

	private static bool TryReadJpegDimensions(Stream stream, out int width, out int height)
	{
		width = 0;
		height = 0;
		stream.Position = 2;
		byte[] lengthBuffer = new byte[2];

		while (stream.Position + 9 < stream.Length)
		{
			int markerPrefix = stream.ReadByte();
			if (markerPrefix != 0xFF)
			{
				continue;
			}

			int marker = stream.ReadByte();
			while (marker == 0xFF)
			{
				marker = stream.ReadByte();
			}

			if (marker < 0)
			{
				return false;
			}

			if (stream.Read(lengthBuffer) != 2)
			{
				return false;
			}

			int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBuffer);
			if (segmentLength < 2 || stream.Position + segmentLength - 2 > stream.Length)
			{
				return false;
			}

			bool isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
			if (isStartOfFrame)
			{
				Span<byte> frame = stackalloc byte[5];
				if (stream.Read(frame) != 5)
				{
					return false;
				}

				height = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(1, 2));
				width = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(3, 2));
				return width > 0 && height > 0;
			}

			stream.Position += segmentLength - 2;
		}

		return false;
	}

	private static bool TryReadWebpDimensions(ReadOnlySpan<byte> header, Stream stream, out int width, out int height)
	{
		width = 0;
		height = 0;
		string chunkType = $"{(char)header[12]}{(char)header[13]}{(char)header[14]}{(char)header[15]}";
		if (chunkType == "VP8X" && header.Length >= 30)
		{
			width = 1 + header[24] + (header[25] << 8) + (header[26] << 16);
			height = 1 + header[27] + (header[28] << 8) + (header[29] << 16);
			return width > 0 && height > 0;
		}

		if (chunkType == "VP8L" && header.Length >= 25)
		{
			uint bits = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(21, 4));
			width = (int)((bits & 0x3FFF) + 1);
			height = (int)(((bits >> 14) & 0x3FFF) + 1);
			return width > 0 && height > 0;
		}

		if (chunkType == "VP8 ")
		{
			stream.Position = 26;
			Span<byte> size = stackalloc byte[4];
			if (stream.Read(size) != 4)
			{
				return false;
			}

			width = BinaryPrimitives.ReadUInt16LittleEndian(size.Slice(0, 2)) & 0x3FFF;
			height = BinaryPrimitives.ReadUInt16LittleEndian(size.Slice(2, 2)) & 0x3FFF;
			return width > 0 && height > 0;
		}

		return false;
	}

	private IReadOnlyList<MediaAssetEntry> SortEntries(IReadOnlyList<MediaAssetEntry> entries)
		=> _sortDirection == MediaAssetSortDirection.RecentFirst
			? entries
				.OrderByDescending(entry => entry.CreatedAt)
				.ThenByDescending(entry => entry.ModifiedAt)
				.ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ToList()
			: entries
				.OrderBy(entry => entry.CreatedAt)
				.ThenBy(entry => entry.ModifiedAt)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

	private IReadOnlyList<MediaAssetSourceEntry> SortSourceEntries(IReadOnlyList<MediaAssetSourceEntry> entries)
		=> _sortDirection == MediaAssetSortDirection.RecentFirst
			? entries
				.OrderByDescending(entry => entry.CreatedAt)
				.ThenByDescending(entry => entry.ModifiedAt)
				.ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ToList()
			: entries
				.OrderBy(entry => entry.CreatedAt)
				.ThenBy(entry => entry.ModifiedAt)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

	private string GetActiveRootPath()
		=> GetRootPath(_activeScope);

	private string GetRootPath(MediaAssetScope scope)
	{
		string directoryName = scope == MediaAssetScope.Output
			? ComfyPathOptions.OutputDirectoryName
			: ComfyPathOptions.InputDirectoryName;

		return string.IsNullOrWhiteSpace(_comfyRootPath)
			? string.Empty
			: Path.Combine(_comfyRootPath, directoryName);
	}

	private void ConfigureMediaWatcher()
	{
		string rootPath = GetActiveRootPath();
		_directoryWatcher.ConfigureRoot(rootPath, MediaWatcherOptions);
	}

	private async Task RefreshActiveScopeAfterRootChangeAsync()
	{
		try
		{
			await RefreshAsync(CancellationToken.None, showLoadingOverlay: false, forceRefresh: true);
			if (_isRailActive && _isReady && !_isRenderDeferred)
			{
				_directoryWatcher.MarkReconciled();
				_directoryWatcher.SetActive(true);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Warning($"Media root refresh failed: {ex.Message}");
		}
	}

	private bool CanApplyMediaWatcherBatch(RailDirectoryWatchBatch batch)
	{
		return _isRailActive
			&& _isReady
			&& !_isRenderDeferred
			&& IsVisible
			&& Handler is not null
			&& string.Equals(batch.RootPath, GetActiveRootPath(), StringComparison.OrdinalIgnoreCase)
			&& _directoryWatcher.IsCurrent(batch.RootPath, batch.Generation);
	}

	private bool ApplyMediaWatcherBatch(RailDirectoryWatchBatch batch)
	{
		if (!CanApplyMediaWatcherBatch(batch))
		{
			return false;
		}

		MarkCacheDirty(batch.RootPath);
		_ = RefreshScopeAsync(
			_activeScope,
			CancellationToken.None,
			showLoadingOverlay: false,
			forceRefresh: true);
		return true;
	}

	private void OnOutputTabTapped(object? sender, TappedEventArgs e)
	{
		SwitchScope(MediaAssetScope.Output);
	}

	private void OnInputTabTapped(object? sender, TappedEventArgs e)
	{
		SwitchScope(MediaAssetScope.Input);
	}

	private void SwitchScope(MediaAssetScope scope)
	{
		_ = SwitchScopeAsync(scope);
	}

	private async Task SwitchScopeAsync(MediaAssetScope scope)
	{
		if (_activeScope == scope)
		{
			return;
		}

		ClearSelection();
		_directoryWatcher.SetActive(false);
		_activeScope = scope;
		ResetSearchText();
		ApplyProjectionToCurrentSurface();
		UpdateTabState();
		ConfigureMediaWatcher();
		var surface = GetActiveSurface();
		surface.VirtualLayoutSignature = string.Empty;
		_ = surface.ScrollView.ScrollToAsync(0, 0, animated: false);
		UpdateVirtualGrid(surface);
		try
		{
			// A scope change is an explicit user refresh. Reconcile the selected directory
			// before re-enabling its watcher, even when no filesystem event was raised.
			await RefreshAsync(CancellationToken.None, showLoadingOverlay: true, forceRefresh: true);
			if (_isRailActive && _isReady && !_isRenderDeferred)
			{
				_directoryWatcher.MarkReconciled();
				_directoryWatcher.SetActive(true);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Warning($"Media scope refresh failed: {ex.Message}");
		}
	}

	private void OnOutputMediaAssetsScrolled(object? sender, ScrolledEventArgs e)
		=> UpdateVirtualGrid(GetSurface(MediaAssetScope.Output));

	private void OnInputMediaAssetsScrolled(object? sender, ScrolledEventArgs e)
		=> UpdateVirtualGrid(GetSurface(MediaAssetScope.Input));

	private void OnOutputMediaAssetsViewportSizeChanged(object? sender, EventArgs e)
		=> UpdateVirtualGrid(GetSurface(MediaAssetScope.Output));

	private void OnInputMediaAssetsViewportSizeChanged(object? sender, EventArgs e)
		=> UpdateVirtualGrid(GetSurface(MediaAssetScope.Input));

	private void OnMediaAssetsSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (_isResettingSearchText)
		{
			return;
		}

		_searchText = e.NewTextValue?.Trim() ?? string.Empty;
		ClearMediaAssetsSearchButton.IsVisible = _searchText.Length > 0;
		ClearSelection();
		_ = ResetActiveScrollAsync();
		ApplyProjectionToCurrentSurface();
	}

	private void OnClearMediaAssetsSearchTapped(object? sender, TappedEventArgs e)
	{
		if (!string.IsNullOrEmpty(MediaAssetsSearchEntry.Text))
		{
			MediaAssetsSearchEntry.Text = string.Empty;
		}
	}

	private void OnMediaAssetsSearchPointerEntered(object? sender, PointerEventArgs e)
		=> _searchVisuals?.SetHovered(true);

	private void OnMediaAssetsSearchPointerExited(object? sender, PointerEventArgs e)
		=> _searchVisuals?.SetHovered(false);

	private void OnMediaAssetsSearchEntryFocused(object? sender, FocusEventArgs e)
	{
		_searchVisuals?.SetFocused(true);
		_searchTextController?.RefreshNativeSelectionColors();
	}

	private void OnMediaAssetsSearchEntryUnfocused(object? sender, FocusEventArgs e)
		=> _searchVisuals?.SetFocused(false);

	private void OnSelectAllMediaAssetsTapped(object? sender, TappedEventArgs e)
	{
		SelectAllVisibleAssets();
	}

	private void OnDeselectAllMediaAssetsTapped(object? sender, TappedEventArgs e)
	{
		ClearSelection();
	}

	private void ResetSearchText()
	{
		_isResettingSearchText = true;
		_searchText = string.Empty;
		MediaAssetsSearchEntry.Text = string.Empty;
		ClearMediaAssetsSearchButton.IsVisible = false;
		_isResettingSearchText = false;
	}

	private async void OnAssetDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (sender is BindableObject { BindingContext: MediaAssetEntry entry })
		{
			await OpenAssetAsync(entry);
		}
	}

	private void OnAssetTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not BindableObject { BindingContext: MediaAssetEntry entry })
		{
			return;
		}

		SelectFromPointer(entry.FullPath);
	}

	private void SelectFromPointer(string path)
	{
		if (PlatformManager.Current.Keyboard.IsShiftPressed())
		{
			SelectRange(path);
			return;
		}

		if (PlatformManager.Current.Keyboard.IsCtrlPressed())
		{
			ToggleSelection(path);
			return;
		}

		SelectSingle(path);
	}

	private void SelectSingle(string path)
	{
		_selection.ReplaceWithSingle(path);
		SyncVisibleSelectionState();
	}

	private void ToggleSelection(string path)
	{
		if (_selection.Contains(path))
		{
			var remaining = _selection.Paths
				.Where(selectedPath => !string.Equals(selectedPath, path, StringComparison.OrdinalIgnoreCase))
				.ToList();
			_selection.ReplaceAll(remaining, remaining.LastOrDefault());
		}
		else
		{
			var replacement = _selection.Paths.Append(path).ToList();
			_selection.ReplaceAll(replacement, path);
		}

		SyncVisibleSelectionState();
	}

	private void SelectRange(string path)
	{
		var visiblePaths = GetActiveSurface()
			.Items
			.Select(item => item.FullPath)
			.ToList();
		_selection.SelectRange(path, visiblePaths, _ => { }, _ => { });
		SyncVisibleSelectionState();
	}

	private void SelectAllVisibleAssets()
	{
		var visiblePaths = GetActiveSurface()
			.Items
			.Where(item => File.Exists(item.FullPath))
			.Select(item => item.FullPath)
			.ToList();
		if (visiblePaths.Count == 0)
		{
			return;
		}

		_selection.ReplaceAll(visiblePaths, visiblePaths[0]);
		SyncVisibleSelectionState();
	}

	private async Task OpenAssetAsync(MediaAssetEntry? entry)
	{
		if (entry == null || !File.Exists(entry.FullPath))
		{
			return;
		}

		if (TryRaiseViewerRequest(entry))
		{
			return;
		}

		var result = await PlatformManager.Current.Shell.OpenPathAsync(entry.FullPath);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Failed to open media asset: {result.Message}");
		}
	}

	private bool TryRaiseViewerRequest(MediaAssetEntry entry)
	{
		if (ViewerRequested == null || !TryCreateViewerRequest(entry.FullPath, out var request))
		{
			return false;
		}

		ViewerRequested.Invoke(this, request);
		return true;
	}

	private static MediaViewerItem ToMediaViewerItem(MediaAssetEntry item)
		=> new(
			item.Name,
			item.FullPath,
			JobId: item.JobId,
			Type: item.Type,
			Subfolder: item.Subfolder,
			IsBatchInferred: item.IsBatchInferred);

	private MediaAssetScopeSurface? GetSurfaceContaining(string path)
	{
		if (GetSurface(MediaAssetScope.Output).Items.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)))
		{
			return GetSurface(MediaAssetScope.Output);
		}

		if (GetSurface(MediaAssetScope.Input).Items.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)))
		{
			return GetSurface(MediaAssetScope.Input);
		}

		return null;
	}

	private MediaAssetEntry? GetPrimarySelectedEntry()
	{
		string? primaryPath = _selection.GetPrimarySelectedPath();
		if (string.IsNullOrWhiteSpace(primaryPath))
		{
			return null;
		}

		return GetSurface(MediaAssetScope.Output).Items
			.Concat(GetSurface(MediaAssetScope.Input).Items)
			.FirstOrDefault(item => string.Equals(item.FullPath, primaryPath, StringComparison.OrdinalIgnoreCase));
	}

	private async Task RevealAssetAsync(MediaAssetEntry? entry)
	{
		if (entry == null)
		{
			return;
		}

		PrepareContextSelection(entry);
		await _fileOperations.RevealInExplorerAsync(entry.FullPath);
	}

	private async Task BeginCopySelectedAsync(MediaAssetEntry? entry)
	{
		PrepareContextSelection(entry);
		var paths = GetSelectedExistingPaths();
		_fileOperations.BeginCopySelected(paths);
		await CopyFilesToSystemClipboardAsync(paths);
	}

	private async Task MoveSelectionAsync(MediaAssetEntry? entry)
	{
		PrepareContextSelection(entry);
		var selectedItems = GetSelectedExistingEntries();
		if (selectedItems.Count == 0)
		{
			return;
		}

		var folderResult = await PlatformManager.Current.FilePicker.PickFolderAsync(
			LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move_picker_title"));
		if (!folderResult.IsSuccess || string.IsNullOrWhiteSpace(folderResult.Value))
		{
			if (!string.IsNullOrWhiteSpace(folderResult.Message))
			{
				NexusLog.Warning($"[MEDIA_ASSETS] Move target selection failed: {folderResult.Message}");
			}

			return;
		}

		string destinationDirectory = folderResult.Value;
		if (!Directory.Exists(destinationDirectory))
		{
			NexusLog.Warning($"[MEDIA_ASSETS] Move target directory was not found: {destinationDirectory}");
			return;
		}

		var movableItems = selectedItems
			.Where(item => !string.Equals(Path.GetDirectoryName(item.FullPath), destinationDirectory, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (movableItems.Count == 0)
		{
			return;
		}

		bool hasConflict = HasMoveNameConflict(movableItems, destinationDirectory);
		string message = LocalizationManager.Format(
			"views.rail.tools.media_assets.media_assets_view.move_confirm_message",
			movableItems.Count,
			destinationDirectory);
		if (hasConflict)
		{
			message = $"{message}{Environment.NewLine}{Environment.NewLine}{LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move_conflict_message")}";
		}

		bool shouldMove = await NexusDialogService.ConfirmAsync(
			LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move_confirm_title"),
			message,
			LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move"),
			LocalizationManager.Text("common.cancel"));
		if (!shouldMove)
		{
			return;
		}

		try
		{
			var movedItems = await Task.Run(() => MoveMediaItems(movableItems, destinationDirectory));
			if (movedItems.Count == 0)
			{
				return;
			}

			ClearSelection();
			RefreshAfterFileOperation(GetMoveTouchedDirectories(movedItems).ToArray());
			await CleanupMovedOutputJobsAsync(movedItems);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MEDIA_ASSETS] Failed to move selected media files");
			throw;
		}
	}

	private static bool HasMoveNameConflict(IReadOnlyList<MediaAssetEntry> items, string destinationDirectory)
	{
		var reservedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in items)
		{
			string destinationPath = Path.Combine(destinationDirectory, item.Name);
			if (!reservedDestinations.Add(destinationPath) || File.Exists(destinationPath))
			{
				return true;
			}
		}

		return false;
	}

	private static List<MovedMediaAsset> MoveMediaItems(IReadOnlyList<MediaAssetEntry> items, string destinationDirectory)
	{
		var movedItems = new List<MovedMediaAsset>();
		var reservedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in items)
		{
			if (!File.Exists(item.FullPath))
			{
				continue;
			}

			string destinationPath = GetUniqueMoveDestinationPath(
				Path.Combine(destinationDirectory, item.Name),
				reservedDestinations);
			reservedDestinations.Add(destinationPath);
			if (string.Equals(item.FullPath, destinationPath, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			MoveFileWithFallback(item.FullPath, destinationPath);
			movedItems.Add(new MovedMediaAsset(item, item.FullPath, destinationPath));
		}

		return movedItems;
	}

	private static string GetUniqueMoveDestinationPath(string destinationPath, ISet<string> reservedDestinations)
	{
		if (!File.Exists(destinationPath) &&
			!reservedDestinations.Contains(destinationPath))
		{
			return destinationPath;
		}

		string directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
		string fileName = Path.GetFileNameWithoutExtension(destinationPath);
		string extension = Path.GetExtension(destinationPath);
		int suffix = 1;

		while (true)
		{
			string candidate = Path.Combine(directory, $"{fileName} - Copy {suffix}{extension}");
			if (!File.Exists(candidate) &&
				!reservedDestinations.Contains(candidate))
			{
				return candidate;
			}

			suffix++;
		}
	}

	private static void MoveFileWithFallback(string sourcePath, string destinationPath)
	{
		try
		{
			File.Move(sourcePath, destinationPath);
		}
		catch (IOException)
		{
			File.Copy(sourcePath, destinationPath, overwrite: false);
			File.Delete(sourcePath);
		}
	}

	private static IEnumerable<string?> GetMoveTouchedDirectories(IReadOnlyList<MovedMediaAsset> movedItems)
	{
		foreach (var item in movedItems)
		{
			yield return Path.GetDirectoryName(item.SourcePath);
			yield return Path.GetDirectoryName(item.DestinationPath);
		}
	}

	private async Task CleanupMovedOutputJobsAsync(IReadOnlyList<MovedMediaAsset> movedItems)
	{
		if (_staleOutputJobCleanupHandler == null)
		{
			return;
		}

		var jobIds = movedItems
			.Select(item => item.Entry.JobId)
			.Where(jobId => !string.IsNullOrWhiteSpace(jobId))
			.Select(jobId => jobId!)
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (jobIds.Count == 0)
		{
			return;
		}

		try
		{
			await _staleOutputJobCleanupHandler(jobIds);
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[MEDIA_ASSETS] Failed to clean moved output jobs: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private async Task CopySelectedAssetPathsAsync(MediaAssetEntry? entry)
	{
		PrepareContextSelection(entry);
		var paths = GetSelectedExistingPaths();
		if (paths.Count == 0)
		{
			return;
		}

		try
		{
			await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(
				string.Join(Environment.NewLine, paths));
		}
		catch
		{
		}
	}

	private async Task RenameSelectionAsync(MediaAssetEntry? entry)
	{
		PrepareContextSelection(entry);
		await _fileOperations.RenameSelectionAsync(GetSelectedExistingPaths());
	}

	private async Task DeleteSelectionAsync(MediaAssetEntry? entry)
	{
		if (_deleteSelectionInFlight)
		{
			return;
		}

		PrepareContextSelection(entry);
		var selectedItems = GetSelectedExistingEntries();
		if (selectedItems.Count == 0)
		{
			return;
		}

		string message = selectedItems.Count == 1
			? LocalizationManager.Format(
				"views.overlays.media_viewer.delete_media_message",
				Path.GetFileName(selectedItems[0].FullPath))
			: LocalizationManager.Format(
				"views.rail.tools.media_assets.media_assets_view.delete_selected_message",
				selectedItems.Count);

		_deleteSelectionInFlight = true;
		try
		{
			await NexusDialogService.ConfirmAsync(
				LocalizationManager.Text("views.overlays.media_viewer.delete_media_title"),
				message,
				LocalizationManager.Text("common.delete"),
				LocalizationManager.Text("common.cancel"),
				onOk: async () =>
			{
				try
				{
					var viewerItems = selectedItems.Select(ToMediaViewerItem).ToList();
					if (_deleteHandler != null)
					{
						bool deleted = await _deleteHandler(viewerItems);
						if (!deleted)
						{
							return NexusDialogActionResult.KeepOpen;
						}
					}
					else
					{
						var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						foreach (var item in selectedItems)
						{
							string? parentDirectory = Path.GetDirectoryName(item.FullPath);
							if (!string.IsNullOrWhiteSpace(parentDirectory))
							{
								touchedDirectories.Add(parentDirectory);
							}

							if (File.Exists(item.FullPath))
							{
								File.Delete(item.FullPath);
							}
						}

						RefreshAfterFileOperation(touchedDirectories.ToArray());
					}

					_selection.Clear();
				}
				catch (Exception ex)
				{
					NexusLog.Exception(ex, "[MEDIA_ASSETS] Failed to delete selected media files");
					throw;
				}

				return NexusDialogActionResult.Close;
			},
			returnFocusTarget: NexusDialogReturnFocusTarget.App);
		}
		finally
		{
			_deleteSelectionInFlight = false;
		}
	}

	private static async Task CopyFilesToSystemClipboardAsync(IReadOnlyList<string> paths)
	{
		if (paths.Count == 0)
		{
			return;
		}

		var result = await PlatformManager.Current.FileClipboard.SetFilesAsync(paths, cut: false);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"[MEDIA_ASSETS] Failed to set OS file clipboard: {result.Message}");
		}
	}

	private void PrepareContextSelection(MediaAssetEntry? entry)
	{
		if (entry == null)
		{
			return;
		}

		if (_selection.Contains(entry.FullPath))
		{
			_selection.EnsureAnchor(entry.FullPath);
		}
		else
		{
			_selection.ReplaceWithSingle(entry.FullPath);
		}

		SyncVisibleSelectionState();
	}

	private List<string> GetSelectedExistingPaths()
		=> GetSurface(MediaAssetScope.Output).Items
			.Concat(GetSurface(MediaAssetScope.Input).Items)
			.Select(item => item.FullPath)
			.Where(_selection.Contains)
			.Where(File.Exists)
			.ToList();

	private List<MediaAssetEntry> GetSelectedExistingEntries()
		=> GetSurface(MediaAssetScope.Output).Items
			.Concat(GetSurface(MediaAssetScope.Input).Items)
			.Where(item => _selection.Contains(item.FullPath))
			.Where(item => File.Exists(item.FullPath))
			.ToList();

	private void ClearSelection()
	{
		_selection.Clear();
		SyncVisibleSelectionState();
	}

	private void SyncVisibleSelectionState()
	{
		foreach (var item in GetSurface(MediaAssetScope.Output).Items)
		{
			item.IsSelected = _selection.Contains(item.FullPath);
		}

		foreach (var item in GetSurface(MediaAssetScope.Input).Items)
		{
			item.IsSelected = _selection.Contains(item.FullPath);
		}

		foreach (var cell in GetSurface(MediaAssetScope.Output).VisibleCells.Values)
		{
			cell.RefreshVisualState();
		}

		foreach (var cell in GetSurface(MediaAssetScope.Input).VisibleCells.Values)
		{
			cell.RefreshVisualState();
		}
	}

	private async Task OpenPathAsync(string path)
	{
		var result = await PlatformManager.Current.Shell.OpenPathAsync(path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Failed to open media assets path: {result.Message}");
		}
	}

	private async void OnRefreshTapped(object? sender, TappedEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(_searchText))
		{
			await ResetActiveScrollAsync();
			ApplyProjectionToCurrentSurface();
			return;
		}

		if (_activeScope == MediaAssetScope.Output)
		{
			if (_outputRefreshHandler != null)
			{
				await _outputRefreshHandler();
			}
			else
			{
				MarkCacheDirty(GetRootPath(MediaAssetScope.Output));
				_ = RefreshScopeAsync(MediaAssetScope.Output, CancellationToken.None, showLoadingOverlay: true, forceRefresh: true);
			}

			return;
		}

		_ = RefreshAsync(CancellationToken.None, showLoadingOverlay: true, forceRefresh: true);
	}

	private async void OnOpenFolderTapped(object? sender, TappedEventArgs e)
	{
		string rootPath = GetActiveRootPath();
		if (!Directory.Exists(rootPath))
		{
			return;
		}

		var result = await PlatformManager.Current.Shell.OpenPathAsync(rootPath);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Failed to open media assets folder: {result.Message}");
		}
	}

	private void OnSortRecentTapped(object? sender, TappedEventArgs e)
	{
		_sortDirection = _sortDirection == MediaAssetSortDirection.RecentFirst
			? MediaAssetSortDirection.OldestFirst
			: MediaAssetSortDirection.RecentFirst;
		UpdateToolbarState();
		_ = ResetActiveScrollAsync();
		ApplyProjectionToCurrentSurface();
	}

	private async Task ResetActiveScrollAsync()
	{
		try
		{
			await GetActiveSurface().ScrollView.ScrollToAsync(0, 0, animated: false);
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"Media assets scroll reset skipped: {ex.Message}");
		}
	}

	private void ApplyProjectionToCurrentSurface()
	{
		var surface = GetActiveSurface();
		surface.RenderedSignature = string.Empty;
		surface.VirtualLayoutSignature = string.Empty;
		_ = RenderProjectedItemsAsync(surface, Interlocked.Increment(ref surface.RenderVersion));
	}

	private void OnViewModeTapped(object? sender, TappedEventArgs e)
	{
		SetThumbnailColumnCount(_mediaGridColumns >= 3 ? 1 : _mediaGridColumns + 1);
	}

	private void SetThumbnailColumnCount(int columnCount)
	{
		columnCount = Math.Clamp(columnCount, 1, 3);
		if (_mediaGridColumns == columnCount)
		{
			return;
		}

		_mediaGridColumns = columnCount;
		SaveMediaGridColumnsPreference(_mediaGridColumns);
		foreach (var surface in new[] { GetSurface(MediaAssetScope.Output), GetSurface(MediaAssetScope.Input) })
		{
			surface.VirtualLayoutSignature = string.Empty;
			UpdateVirtualGrid(surface);
		}

		UpdateToolbarState();
	}

	private static int LoadMediaGridColumnsPreference()
		=> Math.Clamp(PortablePreferences.Get(PreferenceKeys.MediaAssetsGridColumns, DefaultMediaGridColumns), 1, 3);

	private static void SaveMediaGridColumnsPreference(int columnCount)
		=> PortablePreferences.Set(PreferenceKeys.MediaAssetsGridColumns, Math.Clamp(columnCount, 1, 3));

	private void UpdateTabState()
	{
		bool outputActive = _activeScope == MediaAssetScope.Output;
		OutputTabButton.BackgroundColor = Color.FromArgb(GetTabBackgroundColor(isActive: outputActive, isHovered: _isOutputTabHovered));
		InputTabButton.BackgroundColor = Color.FromArgb(GetTabBackgroundColor(isActive: !outputActive, isHovered: _isInputTabHovered));
		OutputTabButton.Stroke = Color.FromArgb(GetTabStrokeColor(isActive: outputActive, isHovered: _isOutputTabHovered));
		InputTabButton.Stroke = Color.FromArgb(GetTabStrokeColor(isActive: !outputActive, isHovered: _isInputTabHovered));
		OutputTabLabel.TextColor = outputActive ? MediaTabActiveTextColor : MediaTabInactiveTextColor;
		InputTabLabel.TextColor = outputActive ? MediaTabInactiveTextColor : MediaTabActiveTextColor;
		UpdateActiveSurfaceVisibility();
		UpdateStatus(GetActiveRootPath());
		UpdateVirtualGrid(GetActiveSurface());
	}

	private void UpdateActiveSurfaceVisibility()
	{
		bool outputActive = _activeScope == MediaAssetScope.Output;
		var activeSurface = GetActiveSurface();
		bool hasItems = activeSurface.Items.Count > 0;
		OutputMediaAssetsScrollView.IsVisible = outputActive && hasItems;
		InputMediaAssetsScrollView.IsVisible = !outputActive && hasItems;
		MediaAssetsEmptyView.IsVisible = !hasItems;
	}

	private static string GetTabBackgroundColor(bool isActive, bool isHovered)
		=> isActive
			? isHovered ? MediaTabActiveHoverBackgroundColor : MediaTabActiveBackgroundColor
			: isHovered ? MediaTabInactiveHoverBackgroundColor : MediaTabInactiveBackgroundColor;

	private static string GetTabStrokeColor(bool isActive, bool isHovered)
		=> isActive
			? isHovered ? MediaTabActiveHoverStrokeColor : MediaTabActiveStrokeColor
			: isHovered ? MediaTabInactiveHoverStrokeColor : MediaTabInactiveStrokeColor;

	private void UpdateStatus(string rootPath)
	{
		if (_activeScope == MediaAssetScope.Output)
		{
			MediaAssetsStatusTitleLabel.Text = LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.output_feed_title");
			MediaAssetsPathLabel.Text = LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.output_feed_description");
			ToolTipProperties.SetText(MediaAssetsPathLabel, rootPath);
			MediaAssetsPathLabel.TextColor = Directory.Exists(rootPath)
				? MediaOutputPathValidTextColor
				: MediaPathInvalidTextColor;
			return;
		}

		MediaAssetsStatusTitleLabel.Text = LocalizationManager.Text("views.rail.tools.assets.assets_browser_view.current_location");
		MediaAssetsPathLabel.Text = rootPath;
		ToolTipProperties.SetText(MediaAssetsPathLabel, rootPath);
		MediaAssetsPathLabel.TextColor = Directory.Exists(rootPath)
			? MediaPathValidTextColor
			: MediaPathInvalidTextColor;
	}

	private void UpdateToolbarState()
	{
		SortRecentIcon.Source = _sortDirection == MediaAssetSortDirection.RecentFirst
			? "media_sort_desc.png"
			: "media_sort_asc.png";
		ViewModeIcon.Source = _mediaGridColumns switch
		{
			1 => "media_columns_1.png",
			2 => "media_columns_2.png",
			_ => "media_columns_3.png",
		};
		ViewModeColumnLabel.Text = _mediaGridColumns.ToString();
		ToolTipProperties.SetText(
			SortRecentButton,
			LocalizationManager.Text(
				_sortDirection == MediaAssetSortDirection.RecentFirst
					? "views.rail.tools.media_assets.media_assets_view.sort_recent_desc"
					: "views.rail.tools.media_assets.media_assets_view.sort_recent_asc"));
		ToolTipProperties.SetText(
			ViewModeButton,
			$"{LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.view_mode")}: {_mediaGridColumns}");
		SetToolbarOptionState(SortRecentButton, isActive: true);
		SetToolbarOptionState(ViewModeButton, isActive: true);
	}

	private static void SetToolbarOptionState(Border border, bool isActive)
	{
		border.BackgroundColor = Colors.Transparent;
	}

	private void QueueRefresh(bool forceRefresh = false)
	{
		if (_isRenderDeferred)
		{
			return;
		}

		_ = RefreshAsync(CancellationToken.None, showLoadingOverlay: true, forceRefresh);
	}

	private void RefreshAfterFileOperation(string?[] touchedDirectories)
	{
		foreach (string? directory in touchedDirectories)
		{
			if (!string.IsNullOrWhiteSpace(directory))
			{
				MarkCacheDirty(directory);
			}
		}

		MarkCacheDirty(GetActiveRootPath());
		_directoryWatcher.NotifyMutation(touchedDirectories);
	}

	private static void OnToolbarButtonPointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			border.BackgroundColor = MediaToolbarHoverBackgroundColor;
		}
	}

	private void OnToolbarButtonPointerExited(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			if (ReferenceEquals(border, SortRecentButton) ||
				ReferenceEquals(border, ViewModeButton))
			{
				UpdateToolbarState();
				return;
			}

			border.BackgroundColor = Colors.Transparent;
		}
	}

	private static void OnTextActionPointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			border.BackgroundColor = MediaTextActionHoverBackgroundColor;
		}
	}

	private static void OnTextActionPointerExited(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			border.BackgroundColor = Colors.Transparent;
		}
	}

	private void OnOutputTabPointerEntered(object? sender, PointerEventArgs e)
	{
		_isOutputTabHovered = true;
		UpdateTabState();
	}

	private void OnOutputTabPointerExited(object? sender, PointerEventArgs e)
	{
		_isOutputTabHovered = false;
		UpdateTabState();
	}

	private void OnInputTabPointerEntered(object? sender, PointerEventArgs e)
	{
		_isInputTabHovered = true;
		UpdateTabState();
	}

	private void OnInputTabPointerExited(object? sender, PointerEventArgs e)
	{
		_isInputTabHovered = false;
		UpdateTabState();
	}

	private static void OnAssetPointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			if (border.BindingContext is MediaAssetEntry { IsSelected: true })
			{
				return;
			}

			border.BackgroundColor = MediaCellHoverBackgroundColor;
			border.Stroke = MediaCellHoverStrokeColor;
		}
	}

	private static void OnAssetPointerExited(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			if (border.BindingContext is MediaAssetEntry { IsSelected: true })
			{
				return;
			}

			border.BackgroundColor = MediaCellBackgroundColor;
			border.Stroke = MediaCellStrokeColor;
		}
	}
}
