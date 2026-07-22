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
	private readonly NexusAppManager _appManager;
	private readonly List<MediaAssetJobPreview> _syncedOutputJobs = [];
	private readonly object _entryCacheGate = new();
	private readonly RailLoadingOverlayController _loadingOverlay;
	private readonly RailDirectoryWatchController _directoryWatcher;
	private readonly NexusOperationController _operations;
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
	private int _mediaGridColumns = DefaultMediaGridColumns;
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
		_appManager = NexusAppManager.Instance;
		_mediaGridColumns = LoadMediaGridColumnsPreference();
		_operations = new NexusOperationController("media-assets", _appManager.BackgroundWorkers);
		OpenAssetCommand = new Command<MediaAssetEntry>(entry => _ = OpenAssetAsync(entry));
		RevealAssetCommand = new Command<MediaAssetEntry>(entry => _ = RevealAssetAsync(entry));
		CopyAssetCommand = new Command<MediaAssetEntry>(entry => _ = BeginCopySelectedAsync(entry));
		MoveAssetCommand = new Command<MediaAssetEntry>(entry => _ = MoveSelectionAsync(entry));
		CopyAssetPathCommand = new Command<MediaAssetEntry>(entry => _ = CopySelectedAssetPathsAsync(entry));
		RenameAssetCommand = new Command<MediaAssetEntry>(entry => _ = RenameSelectionAsync(entry));
		DeleteAssetCommand = new Command<MediaAssetEntry>(entry => _ = DeleteSelectionAsync(entry));
		_fileOperations = new AssetFileOperationService(
			_appManager.Dialogs,
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
		RailSearchClearButtonVisuals.Attach(ClearMediaAssetsSearchButton, ClearMediaAssetsSearchLabel);
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
			_comfyRootPath = _appManager.Paths.ConfiguredComfyPath;
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
			_comfyRootPath = _appManager.Paths.ConfiguredComfyPath;
		}

		UpdateTabState();
		UpdateToolbarState();
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
		_lifetimeCts.Cancel();
		_operations.StopAll();
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
				sourceEntries = await _operations.RunBackgroundAsync(
					NexusBackgroundLane.FileIo,
					"media-source-scan",
					_ =>
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
		_operations.RequestLatest("stale-output-job-cleanup", async lease =>
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

	private void OnOutputMediaAssetsScrolled(object? sender, ScrolledEventArgs e)
		=> UpdateVirtualGrid(GetSurface(MediaAssetScope.Output));

	private void OnInputMediaAssetsScrolled(object? sender, ScrolledEventArgs e)
		=> UpdateVirtualGrid(GetSurface(MediaAssetScope.Input));

	private void OnOutputMediaAssetsViewportSizeChanged(object? sender, EventArgs e)
		=> UpdateVirtualGrid(GetSurface(MediaAssetScope.Output));

	private void OnInputMediaAssetsViewportSizeChanged(object? sender, EventArgs e)
		=> UpdateVirtualGrid(GetSurface(MediaAssetScope.Input));

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

		var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(rootPath);
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

	private int LoadMediaGridColumnsPreference()
		=> Math.Clamp(_appManager.Preferences.Get(PreferenceKeys.MediaAssetsGridColumns, DefaultMediaGridColumns), 1, 3);

	private void SaveMediaGridColumnsPreference(int columnCount)
		=> _appManager.Preferences.Set(PreferenceKeys.MediaAssetsGridColumns, Math.Clamp(columnCount, 1, 3));

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
