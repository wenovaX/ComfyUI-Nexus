namespace ComfyUI_Nexus.Views.Overlays;

using System.Globalization;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Settings;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Diagnostics;
using ComfyUI_Nexus.Setup.Diagnostics.Nodes;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Ui.Popups;
using Microsoft.Maui.Controls.Shapes;

public partial class SettingsOverlayView : ContentView, INexusPopupSurface
{
	private const double PanelViewportScale = 0.8;
	private const double MinimumPanelWidth = 980;
	private const double MinimumPanelHeight = 680;
	private const double ScaleThresholdWidth = 1280;
	private const double ScaleThresholdHeight = 720;
	private const double HiddenPanelScale = 0.98;
	private const double HiddenPanelOffsetY = 12;
	private const double HidePanelOffsetY = 10;
	private const uint ShowAnimationLength = 170;
	private const uint ContentFadeInAnimationLength = 120;
	private const uint HideAnimationLength = 120;
	private const uint OperationBlockerShowAnimationLength = 110;
	private const uint OperationBlockerHideAnimationLength = 90;
	private static readonly Color SettingsDisabledTextColor = Color.FromArgb("#55748c");
	private static readonly Color SettingsInfoTextColor = Color.FromArgb("#cbefff");
	private static readonly Color SettingsInfoActionTextColor = Color.FromArgb("#8fdcff");
	private static readonly Color SettingsMutedTextColor = Color.FromArgb("#9fb8c8");
	private static readonly Color SettingsCardTitleColor = Color.FromArgb("#e8f8ff");
	private static readonly Color SettingsCardSurfaceColor = Color.FromArgb("#0Dffffff");
	private static readonly Color SettingsCardAccentStrokeColor = Color.FromArgb("#1831d8ff");
	private static readonly Color SettingsActivePillColor = Color.FromArgb("#2631d8ff");
	private static readonly Color SettingsInactivePillColor = Color.FromArgb("#1431d8ff");
	private static readonly Color SettingsChangedPillColor = Color.FromArgb("#1F31d8ff");
	private static readonly Color SettingsSavedPillColor = Color.FromArgb("#168fffd6");
	private static readonly Color SettingsSavedTextColor = Color.FromArgb("#d8fff4");
	private static readonly Color SettingsWarningTextColor = Color.FromArgb("#fff2d0");
	private static readonly Color SettingsRequiredTextColor = Color.FromArgb("#fff2a8");
	private static readonly Color SettingsSuccessTextColor = Color.FromArgb("#8fffd6");
	private static readonly Color SettingsFailureTextColor = Color.FromArgb("#ff8f8f");
	private static readonly Color ComfyOperationRunningStrokeColor = Color.FromArgb("#3031d8ff");
	private static readonly Color ComfyOperationSuccessStrokeColor = Color.FromArgb("#558fffd6");
	private static readonly Color ComfyOperationFailureStrokeColor = Color.FromArgb("#66ff6b6b");
	private static readonly Color ComfyOperationFailureTextColor = Color.FromArgb("#ffd8d8");

	internal event EventHandler? CloseRequested;
	internal event EventHandler<SettingsRestartRequestedEventArgs>? RestartServerRequested;
	internal event EventHandler? RuntimePurgeRequested;
	internal event EventHandler<RuntimeRestoreRequestedEventArgs>? RuntimeRestoreRequested;

	public string PopupKey => "Settings";
	public string PopupGroup => "Overlay";
	public VisualElement PopupRoot => this;

	private readonly SettingsEditorService _editor;
	private readonly NexusAppManager _appManager;
	private ComfyInstallService ComfyInstall => _appManager.ComfyInstall;
	private NexusServerProcessController ServerProcesses => _appManager.ServerProcesses;
	private SetupSettingsService SettingsService => _appManager.Settings;
	private readonly List<GpuDeviceInfo> _gpuDevices = new();
	private readonly List<ManagedExtensionOption> _managedExtensionOptions = new();
	private readonly HashSet<string> _activeOperations = new(StringComparer.Ordinal);
	private bool _isRefreshing;
	private bool _isUpdatingLanguagePicker;
	private bool _isUpdatingGpuPicker;
	private bool _isPendingBootChecklistExpanded;
	private bool _isComfyActionBusy;
	private bool _isMaintenanceBusy;
	private bool _repositoryRestartRequired;
	private bool _venvRestartRequired;
	private bool _modelLibrariesRestartRequired;
	private bool _runtimeBackupModelsSelected = true;
	private bool _runtimeBackupCustomNodesSelected = true;
	private bool _runtimeBackupInputSelected = true;
	private bool _runtimeBackupOutputSelected = true;
	private bool _runtimeBackupWorkflowsSelected = true;
	private RuntimeBackupAnalysis? _runtimeBackupAnalysis;
	private int _runtimeBackupAnalysisGeneration;
	private ModelDuplicateScanResult? _modelDuplicateScanResult;
	private int _modelDuplicateGroupIndex;
	private int _modelDuplicateScanGeneration;
	private CancellationTokenSource? _modelDuplicateScanCancellation;
	private CancellationTokenSource _lifetimeCts = new();
	private int _comfyUpdatesAvailable;
	private readonly Queue<string> _comfyOperationLogLines = new();
	private readonly List<string> _comfyOperationFullLogLines = new();
	private string _lastCustomComfyPath = string.Empty;
	private int _toolVersionProbeId;
	private int _extensionsProbeId;
	private int _completedToolVersionProbeId;
	private Task? _toolVersionProbeTask;
	private bool _isUnloaded;
	private bool _isLayoutPrewarmed;
	private string _lastOpenStructureSignature = string.Empty;

	private sealed class LanguageOption(string code, string displayName)
	{
		public string Code { get; } = code;
		public string DisplayName { get; } = displayName;
	}

	private readonly List<LanguageOption> _languageOptions =
	[
		new("en", "English"),
		new("ko", "Korean"),
		new("zh-Hans", "Chinese (Simplified)"),
		new("zh-Hant", "Chinese (Traditional)")
	];

	private sealed class ManagedExtensionOption
	{
		internal ManagedExtensionOption(string folder, string displayName, bool isInstalled, string revision)
		{
			Folder = folder;
			DisplayName = displayName;
			IsInstalled = isInstalled;
			Revision = revision;
			IsSelected = !isInstalled;
		}

		internal string Folder { get; }
		internal string DisplayName { get; }
		internal bool IsInstalled { get; set; }
		internal bool IsSelected { get; set; }
		internal string Revision { get; set; }
	}

	public SettingsOverlayView()
	{
		try
		{
			_appManager = NexusAppManager.Instance;
			_editor = new SettingsEditorService(SettingsService);
			InitializeComponent();
			ProductNameLabel.Text = SafeAppInfo.DisplayName;
			WireRuntimeBackupOptionHover(RuntimeBackupFolderFormatButton, () => IsRuntimeBackupFormat(RuntimeBackupFormats.Folder));
			WireRuntimeBackupOptionHover(RuntimeBackupZipFormatButton, () => IsRuntimeBackupFormat(RuntimeBackupFormats.Zip));
			WireRuntimeBackupOptionHover(RuntimeBackupModelsTargetButton, () => _runtimeBackupModelsSelected);
			WireRuntimeBackupOptionHover(RuntimeBackupCustomNodesTargetButton, () => _runtimeBackupCustomNodesSelected);
			WireRuntimeBackupOptionHover(RuntimeBackupInputTargetButton, () => _runtimeBackupInputSelected);
			WireRuntimeBackupOptionHover(RuntimeBackupOutputTargetButton, () => _runtimeBackupOutputSelected);
			WireRuntimeBackupOptionHover(RuntimeBackupWorkflowsTargetButton, () => _runtimeBackupWorkflowsSelected);
			SizeChanged += OnSizeChanged;
			InitializeLanguagePicker();
			Loaded += OnSettingsOverlayLoaded;
			Unloaded += OnSettingsOverlayUnloaded;
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] SettingsOverlayView InitializeComponent failed");
			throw;
		}
	}

	private void InitializeLanguagePicker()
	{
		LanguagePicker.ItemsSource = _languageOptions;
		LanguagePicker.ItemDisplayBinding = new Binding(nameof(LanguageOption.DisplayName));
	}

	private void OnSettingsOverlayLoaded(object? sender, EventArgs e)
	{
		_isUnloaded = false;
		if (_lifetimeCts.IsCancellationRequested)
		{
			_lifetimeCts.Dispose();
			_lifetimeCts = new CancellationTokenSource();
		}
	}

	private void OnSettingsOverlayUnloaded(object? sender, EventArgs e)
	{
		_isUnloaded = true;
		_lifetimeCts.Cancel();
		_modelDuplicateScanCancellation?.Cancel();
	}

	internal bool IsOverlayVisible => IsVisible;

	public bool IsShown(bool isVisible)
		=> IsVisible == isVisible && Math.Abs(Opacity - (isVisible ? 1 : 0)) < 0.01;

	internal async Task PrewarmLayoutAsync()
	{
		if (_isLayoutPrewarmed)
		{
			return;
		}

		try
		{
			_editor.Reload();
			string structureSignature = CreateOpenStructureSignature(_editor.Draft);
			bool rebuildStaticLists = !string.Equals(
				_lastOpenStructureSignature,
				structureSignature,
				StringComparison.Ordinal);
			Refresh(startProbes: false, rebuildStaticLists: rebuildStaticLists);
			_lastOpenStructureSignature = structureSignature;
			UpdatePanelSize();
			SettingsPanelHost.InvalidateMeasure();
			SettingsPanelBorder.InvalidateMeasure();
			await NexusUiFrame.AwaitDispatcherTurnAsync(this, "SETTINGS:Prewarm");
			_isLayoutPrewarmed = true;
		}
		catch (Exception ex)
		{
			NexusLog.Trace($"[SETTINGS] Layout prewarm skipped: {ex.Message}");
		}
	}

	private void OnSizeChanged(object? sender, EventArgs e)
		=> UpdatePanelSize();

	private void UpdatePanelSize()
	{
		if (Width <= 0 || Height <= 0)
		{
			return;
		}

		double designWidth = Math.Max(MinimumPanelWidth, Width * PanelViewportScale);
		double designHeight = Math.Max(MinimumPanelHeight, Height * PanelViewportScale);

		SettingsPanelBorder.WidthRequest = designWidth;
		SettingsPanelBorder.HeightRequest = designHeight;
		SettingsPanelBorder.AnchorX = 0;
		SettingsPanelBorder.AnchorY = 0;
		SettingsPanelBorder.Scale = Math.Min(
			1,
			Math.Min(Width / ScaleThresholdWidth, Height / ScaleThresholdHeight));
		SettingsPanelHost.WidthRequest = designWidth;
		SettingsPanelHost.HeightRequest = designHeight;
		SettingsPanelBorder.TranslationX = designWidth * (1 - SettingsPanelBorder.Scale) / 2;
		SettingsPanelBorder.TranslationY = designHeight * (1 - SettingsPanelBorder.Scale) / 2;
	}

	internal void Refresh(bool startProbes = true, bool rebuildStaticLists = true)
	{
		_isRefreshing = true;
		var settings = _editor.Draft;
		UpdateLanguagePicker(settings.LanguageCode);
		InstallModeValueLabel.Text = GetInstallModeDisplay(settings);
		ComfyPathValueLabel.Text = GetEffectiveComfyPath(settings);
		GitPathValueLabel.Text = GetToolPathDisplay(settings.GitPath);
		PythonPathValueLabel.Text = GetToolPathDisplay(settings.PythonPath);
		if (!string.IsNullOrWhiteSpace(settings.ComfyPath))
		{
			_lastCustomComfyPath = settings.ComfyPath;
		}

		HostEntry.Text = settings.ListenAddress;
		PortEntry.Text = settings.ServerPort.ToString(CultureInfo.InvariantCulture);
		ExtensionsValueLabel.Text = $"{settings.EssentialNodes.Count} essential node repository target(s)";
		VersionValueLabel.Text = $"Version {SafeAppInfo.VersionString}";
		ServerUrlValueLabel.Text = $"Server http://{settings.ListenAddress}:{settings.ServerPort}";
		SettingsStateValueLabel.Text = settings.LastLaunchSuccessful
			? $"Last launch succeeded on port {(settings.LastActivePort is int lastActivePort ? lastActivePort.ToString(CultureInfo.InvariantCulture) : "unknown")}"
			: "Last launch is not marked successful";
		ExtensionsValueLabel.Text = $"{settings.EssentialNodes.Count + 2} Nexus-managed extension target(s)";
		ExtensionsStatusValueLabel.Text = LocalizationManager.Format(
			"views.overlays.settings_overlay_view.custom_nodes_folder_status",
			GetCustomNodesPath());
		ExtensionsStatusValueLabel.TextColor = SettingsInfoTextColor;
		if (rebuildStaticLists)
		{
			RebuildManagedExtensionOptions();
			RebuildManagedExtensionSelectionList();
		}
		UpdateComfyModeButtons();
		if (rebuildStaticLists)
		{
			RebuildModelLibrariesList();
		}
		UpdateModelDuplicateScanUi();
		RefreshRuntimeBackupCard();
		UpdateToolButtons();
		UpdatePythonModeButtons();
		RefreshGpuOptionsFromKnownDevices();
		UpdatePipCacheCard();
		_isRefreshing = false;
		UpdateStateChrome();
		if (!startProbes)
		{
			return;
		}

		RequestToolVersionProbe();
	}

	public void PrepareShowShell(NexusPopupOpenContext context)
	{
		UpdatePanelSize();
		ResetScrollPosition();
		IsVisible = true;
		Opacity = 0;
		InputTransparent = true;
		SettingsPanelPlaceholder.IsVisible = true;
		SettingsPanelPlaceholder.Opacity = 1;
		SettingsContentRoot.IsVisible = false;
		SettingsContentRoot.Opacity = 0;
		SettingsContentRoot.InputTransparent = true;
		Scale = HiddenPanelScale;
		TranslationY = HiddenPanelOffsetY;
	}

	public void ActivateInput(NexusPopupOpenContext context)
	{
		InputTransparent = false;
	}

	private static string CreateOpenStructureSignature(SetupSettings settings)
		=> string.Join(
			"|",
			[
				LocalizationManager.ActiveLanguage,
				settings.LanguageCode,
				settings.InstallMode,
				settings.ComfyPath,
				settings.GitSource,
				settings.GitPath,
				settings.PythonSource,
				settings.PythonPath,
				settings.PipCacheMode,
				settings.PipCachePath,
				settings.ServerPythonMode,
				settings.ListenAddress,
				settings.ServerPort.ToString(CultureInfo.InvariantCulture),
				string.Join(";", settings.ModelLibraryRoots.Select(ExtraModelPathsService.NormalizeFileSystemPath)),
				string.Join(";", settings.EssentialNodes.Select(node => $"{node.Folder}>{node.Url}"))
			]);

	public void PrepareHide()
	{
		InputTransparent = true;
		SettingsContentRoot.InputTransparent = true;
	}

	public void ResetHiddenState()
	{
		IsVisible = false;
		Opacity = 0;
		Scale = 1;
		TranslationY = 0;
		SettingsPanelPlaceholder.IsVisible = true;
		SettingsPanelPlaceholder.Opacity = 1;
		SettingsContentRoot.IsVisible = false;
		SettingsContentRoot.Opacity = 0;
		SettingsContentRoot.InputTransparent = true;
	}

	public Task AnimateShowAsync(NexusPopupOpenContext context)
		=> AnimateShowCoreAsync();

	public async Task RefreshContentAsync(NexusPopupOpenContext context)
	{
		try
		{
			_editor.Reload();
			string structureSignature = CreateOpenStructureSignature(_editor.Draft);
			bool rebuildStaticLists = !string.Equals(
				_lastOpenStructureSignature,
				structureSignature,
				StringComparison.Ordinal);
			if (rebuildStaticLists)
			{
				Refresh(startProbes: false, rebuildStaticLists: true);
				_lastOpenStructureSignature = structureSignature;
			}
			else
			{
				RefreshVolatileOpenState();
			}

			StartDeferredProbes();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS] Popup content refresh failed.");
		}

		await RevealContentAsync();
	}

	private async Task RevealContentAsync()
	{
		SettingsContentRoot.IsVisible = true;
		SettingsContentRoot.Opacity = 0;
		SettingsContentRoot.InputTransparent = true;
		await NexusUiFrame.AwaitShellReadyAsync(SettingsContentRoot, "SETTINGS:Content");
		await Task.WhenAll(
			SafeAnimation.FadeToAsync(SettingsContentRoot, 1, ContentFadeInAnimationLength, Easing.CubicOut, "Settings.Content"),
			SafeAnimation.FadeToAsync(SettingsPanelPlaceholder, 0, ContentFadeInAnimationLength, Easing.CubicOut, "Settings.Placeholder"));
		SettingsContentRoot.InputTransparent = false;
		SettingsPanelPlaceholder.IsVisible = false;
	}

	private void RefreshVolatileOpenState()
	{
		UpdateStateChrome();
		UpdateToolButtons();
		UpdatePythonModeButtons();
		UpdateGpuSelectionVisuals();
		UpdatePipCacheCard();
	}

	private Task AnimateShowCoreAsync()
		=> AnimatePanelShowAsync();

	private Task AnimatePanelShowAsync()
		=> SafeAnimation.FadeTranslateScaleToAsync(this, "Settings.Show", 1, 0, 1, ShowAnimationLength, Easing.CubicOut, "Settings.Show");

	public Task AnimateHideAsync(NexusPopupOpenContext context)
	{
		return SafeAnimation.FadeTranslateScaleToAsync(this, "Settings.Hide", 0, HidePanelOffsetY, HiddenPanelScale, HideAnimationLength, Easing.CubicIn, "Settings.Hide");
	}

	private Task ScrollToSectionAsync(VisualElement section)
		=> SettingsScrollView.ScrollToAsync(section, ScrollToPosition.Start, true);

	private void ResetScrollPosition()
	{
		_ = UiThread.InvokeAsync(async () =>
		{
			await Task.Yield();
			await SettingsScrollView.ScrollToAsync(0, 0, false);
		}, "SETTINGS:RESET_SCROLL");
	}

	private void ApplyDraftInputs()
	{
		if (_isRefreshing)
		{
			return;
		}

		var draft = _editor.Draft;
		draft.ListenAddress = HostEntry.Text?.Trim() ?? string.Empty;
		if (int.TryParse(PortEntry.Text?.Trim(), out int port))
		{
			draft.ServerPort = port;
		}

		ServerUrlValueLabel.Text = $"Server http://{draft.ListenAddress}:{draft.ServerPort}";
		UpdateStateChrome();
	}

	private void UpdateLanguagePicker(string languageCode)
	{
		_isUpdatingLanguagePicker = true;
		try
		{
			string normalizedCode = GetEffectiveLanguageCode(languageCode);
			int index = _languageOptions
				.Select((option, optionIndex) => new { option, optionIndex })
				.FirstOrDefault(item => string.Equals(item.option.Code, normalizedCode, StringComparison.OrdinalIgnoreCase))
				?.optionIndex ?? 0;
			LanguagePicker.SelectedIndex = index;
		}
		finally
		{
			_isUpdatingLanguagePicker = false;
		}
	}

	private async Task RefreshToolVersionsAsync(int probeId, CancellationToken cancellationToken = default)
	{
		var draft = _editor.Draft;
		string gitSource = draft.GitSource;
		string configuredGitPath = draft.GitPath;
		string pythonSource = draft.PythonSource;
		string configuredPythonPath = draft.PythonPath;
		string gitPath = ResolveToolProbePath(gitSource, configuredGitPath, "git");
		string pythonPath = ResolveToolProbePath(pythonSource, configuredPythonPath, "python");

		GitVersionValueLabel.Text = "checking version...";
		PythonVersionValueLabel.Text = "checking version...";
		GitPathValueLabel.Text = "resolving executable...";
		PythonPathValueLabel.Text = "resolving executable...";

		string? gitVersion;
		string? pythonVersion;
		string gitDisplayPath;
		string pythonDisplayPath;
		try
		{
			var result = await Task.Run(async () =>
			{
				string? detectedGitVersion = await DiagnosticNodeHelpers.TryGetCommandVersionAsync(
					gitPath,
					"--version",
					"git version ",
					cancellationToken);
				string? detectedPythonVersion = await DiagnosticNodeHelpers.TryGetCommandVersionAsync(
					pythonPath,
					"--version",
					"Python ",
					cancellationToken);
				string detectedGitPath = await ResolveToolDisplayPathAsync(
					gitSource,
					configuredGitPath,
					"git",
					cancellationToken);
				string detectedPythonPath = await ResolveToolDisplayPathAsync(
					pythonSource,
					configuredPythonPath,
					"python",
					cancellationToken);
				return (detectedGitVersion, detectedPythonVersion, detectedGitPath, detectedPythonPath);
			}, cancellationToken);
			gitVersion = result.detectedGitVersion;
			pythonVersion = result.detectedPythonVersion;
			gitDisplayPath = result.detectedGitPath;
			pythonDisplayPath = result.detectedPythonPath;
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (probeId != _toolVersionProbeId || !IsVisible)
		{
			return;
		}

		GitVersionValueLabel.Text = $"{GetSourceDisplay(gitSource)} / {(gitVersion == null ? "not available" : $"v{gitVersion}")}";
		PythonVersionValueLabel.Text = $"{GetSourceDisplay(pythonSource)} / {(pythonVersion == null ? "not available" : $"v{pythonVersion}")}";
		GitPathValueLabel.Text = gitDisplayPath;
		PythonPathValueLabel.Text = pythonDisplayPath;
	}

	private bool SaveDraft(bool refresh = true)
	{
		ApplyDraftInputs();
		if (string.Equals(_editor.Draft.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal))
		{
			_editor.Draft.PendingVenvDelete = false;
			_editor.Draft.PendingBootTasks.RemoveAll(task =>
				string.Equals(task.Id, PendingBootTaskIds.VenvDelete, StringComparison.Ordinal));
		}

		string? validationError = _editor.ValidateDraft();
		if (validationError != null)
		{
			_ = ShowValidationAlertAsync(validationError);
			return false;
		}

		bool appliedModelLibraryPaths = false;
		ExtraModelPathsTransaction? yamlTransaction = null;
		if (_editor.RequiresModelLibraryApply())
		{
			string comfyPath = GetEffectiveComfyPath(_editor.Draft);
			ExtraModelPathsResult yamlResult = ExtraModelPathsService.TryApply(
				_editor.Draft,
				comfyPath,
				out yamlTransaction);
			if (!yamlResult.IsSuccess)
			{
				_ = ShowValidationAlertAsync(yamlResult.Message);
				return false;
			}

			appliedModelLibraryPaths = true;
		}

		if (!_editor.Save())
		{
			yamlTransaction?.Rollback();
			_ = ShowValidationAlertAsync(LocalizationManager.Text("settings.model_libraries.settings_save_failed"));
			return false;
		}

		SyncActiveComfyPathPreference(_editor.Draft);
		yamlTransaction?.Commit();
		if (appliedModelLibraryPaths)
		{
			_modelLibrariesRestartRequired = true;
		}

		if (refresh)
		{
			Refresh();
		}

		return true;
	}

	private async Task<bool> QueueBootTaskAsync(
		string taskId,
		string blockerTitle,
		string blockerDetail,
		bool saveDraft,
		IEnumerable<string>? targetFolders = null,
		string action = "",
		Action? afterRefresh = null,
		bool showBlocker = true)
	{
		if (showBlocker)
		{
			await SetSettingsOperationBlockerVisibleAsync(true, blockerTitle, blockerDetail);
		}
		try
		{
			await Task.Yield();

			if (saveDraft && !SaveDraft(refresh: false))
			{
				return false;
			}

			SettingsService.EnqueueBootTask(taskId, targetFolders: targetFolders, action: action);
			SettingsService.Reload();
			_editor.Reload();
			Refresh(startProbes: false);
			afterRefresh?.Invoke();
			TryUpdateUi(UpdateStateChrome);
			return true;
		}
		finally
		{
			if (showBlocker)
			{
				await SetSettingsOperationBlockerVisibleAsync(false);
			}
		}
	}

	private async Task ShowValidationAlertAsync(string message)
	{
		var page = Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page != null)
		{
			await page.DisplayAlertAsync(
				LocalizationManager.Text("settings.validation.invalid_settings_title"),
				message,
				LocalizationManager.Text("common.ok"));
		}
	}

	private async Task<bool> ShowConfirmationAsync(string title, string message, string accept, string cancel)
	{
		var page = Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;
		return page != null && await page.DisplayAlertAsync(title, message, accept, cancel);
	}

	private void UpdateStateChrome()
	{
		SettingsEditorState state = _editor.Evaluate();
		var restartReasons = state.RestartReasons.ToList();
		bool hasActiveOperations = _activeOperations.Count > 0 || _isComfyActionBusy;
		if (_repositoryRestartRequired)
		{
			restartReasons.Add("ComfyUI repository updated");
		}

		if (_venvRestartRequired)
		{
			restartReasons.Add("Virtual environment changed");
		}

		if (_modelLibrariesRestartRequired)
		{
			restartReasons.Add(LocalizationManager.Text("settings.model_libraries.restart_reason"));
		}

		var pendingTasks = GetEffectivePendingBootTasks();
		if (pendingTasks.Count > 0)
		{
			restartReasons.Add(LocalizationManager.Format(
				"settings.pending_tasks.restart_reason",
				string.Join(", ", pendingTasks.Select(SettingsStatusPresenter.GetPendingBootTaskTitle))));
		}

		UpdatePendingBootChecklist(pendingTasks);
		UpdateSectionQueueHints(pendingTasks);

		RestartRequiredBanner.IsVisible = state.RequiresServerRestart
			|| _repositoryRestartRequired
			|| _venvRestartRequired
			|| _modelLibrariesRestartRequired
			|| pendingTasks.Count > 0;
		RestartReasonLabel.Text = restartReasons.Count == 0
			? string.Empty
			: $"Changed: {string.Join(", ", restartReasons)}";
		RestartServerButton.Text = state.HasUnsavedChanges
			? LocalizationManager.Text("settings.actions.save_restart")
			: pendingTasks.Count > 0
				? LocalizationManager.Text("settings.actions.run_tasks_restart")
				: LocalizationManager.Text("common.restart_server");

		DiscardButton.IsEnabled = state.HasUnsavedChanges;
		SaveButton.IsEnabled = state.HasUnsavedChanges;
		DiscardButton.TextColor = state.HasUnsavedChanges ? NexusColors.TextSoft : SettingsDisabledTextColor;
		SaveButton.TextColor = state.HasUnsavedChanges ? NexusColors.TextStrong : SettingsDisabledTextColor;
		SaveButton.BackgroundColor = state.HasUnsavedChanges ? SettingsActivePillColor : SettingsInactivePillColor;
		DiscardButton.IsEnabled = DiscardButton.IsEnabled && !hasActiveOperations;
		SaveButton.IsEnabled = SaveButton.IsEnabled && !hasActiveOperations;
		SyncModelLibraryStructureButton.IsEnabled = !hasActiveOperations
			&& !state.HasUnsavedChanges
			&& SettingsService.Settings.ModelLibraryRoots.Count > 0;
		ScanModelDuplicatesButton.IsEnabled = !hasActiveOperations;
		MaintenanceDeleteBackupButton.IsEnabled = !hasActiveOperations && GetRuntimeBackupDeleteFolders().Count > 0;
		UpdateModelDuplicateScanButtons(hasActiveOperations);
		UpdateSettingsStatus(state, restartReasons, hasActiveOperations, IsDraftLanguageChanged());
	}

	private void UpdateSettingsStatus(
		SettingsEditorState state,
		IReadOnlyList<string> restartReasons,
		bool hasActiveOperations,
		bool languageChanged)
	{
		if (hasActiveOperations)
		{
			SetSettingsStatus(
				LocalizationManager.Text("settings.status.working"),
				NexusColors.AccentStroke,
				SettingsInfoTextColor,
				LocalizationManager.Format("settings.status.running_detail", GetActiveOperationDisplay()));
			return;
		}

		if (languageChanged)
		{
			SetSettingsStatus(
				state.HasUnsavedChanges
					? LocalizationManager.Text("settings.status.language_changed")
					: LocalizationManager.Text("settings.status.app_restart"),
				NexusColors.AccentStroke,
				SettingsInfoTextColor,
				state.HasUnsavedChanges
					? LocalizationManager.Text("settings.status.language_changed_detail")
					: LocalizationManager.Text("settings.status.app_restart_detail"));
			return;
		}

		if (restartReasons.Count > 0)
		{
			SetSettingsStatus(
				state.HasUnsavedChanges
					? LocalizationManager.Text("settings.status.changed_restart")
					: LocalizationManager.Text("settings.status.restart_required"),
				NexusColors.WarningSoft,
				SettingsWarningTextColor,
				state.HasUnsavedChanges
					? LocalizationManager.Text("settings.status.changed_restart_detail")
					: LocalizationManager.Format("settings.status.restart_required_detail", string.Join(", ", restartReasons)));
			return;
		}

		if (state.HasUnsavedChanges)
		{
			SetSettingsStatus(
				LocalizationManager.Text("settings.status.changed"),
				SettingsChangedPillColor,
				NexusColors.TextSoft,
				LocalizationManager.Text("settings.status.changed_detail"));
			return;
		}

		SetSettingsStatus(
			LocalizationManager.Text("settings.status.saved"),
			SettingsSavedPillColor,
			SettingsSavedTextColor,
			LocalizationManager.Text("settings.status.saved_detail"));
	}

	private void SetSettingsStatus(string label, Color backgroundColor, Color textColor, string detail)
	{
		SettingsStatusLabel.Text = label;
		SettingsStatusLabel.TextColor = textColor;
		SettingsStatusPill.BackgroundColor = backgroundColor;
		SettingsStatusDetailLabel.Text = detail;
	}

	private void UpdatePendingBootChecklist(IReadOnlyList<PendingBootTask> pendingTasks)
	{
		PendingBootChecklistPanel.IsVisible = pendingTasks.Count > 0;
		PendingBootTaskList.Children.Clear();
		if (pendingTasks.Count == 0)
		{
			_isPendingBootChecklistExpanded = false;
			PendingBootChecklistSummaryLabel.Text = string.Empty;
			PendingBootChecklistPreviewLabel.Text = string.Empty;
			PendingBootChecklistToggleButton.Text = LocalizationManager.Text("settings.pending_tasks.expand");
			return;
		}

		PendingBootChecklistSummaryLabel.Text = LocalizationManager.Format("settings.pending_tasks.queued_count", pendingTasks.Count);
		PendingBootChecklistPreviewLabel.Text = FormatPendingBootTaskPreview(pendingTasks);
		PendingBootChecklistPreviewLabel.IsVisible = !_isPendingBootChecklistExpanded;
		PendingBootTaskList.IsVisible = _isPendingBootChecklistExpanded;
		PendingBootChecklistToggleButton.Text = _isPendingBootChecklistExpanded
			? LocalizationManager.Text("settings.pending_tasks.collapse")
			: LocalizationManager.Text("settings.pending_tasks.expand");
		foreach (PendingBootTask task in pendingTasks)
		{
			PendingBootTaskList.Children.Add(CreatePendingBootTaskCard(task));
		}
	}

	private static string FormatPendingBootTaskPreview(IReadOnlyList<PendingBootTask> pendingTasks)
		=> string.Join(" / ", pendingTasks.Select(SettingsStatusPresenter.GetPendingBootTaskTitle));

	private IReadOnlyList<PendingBootTask> GetEffectivePendingBootTasks()
		=> _editor.Draft.PendingBootTasks
			.Where(task => !string.IsNullOrWhiteSpace(task.Id))
			.OrderBy(task => GetPendingBootTaskOrder(task.Id))
			.ThenBy(task => task.CreatedAtUtc)
			.ToList();

	private Border CreatePendingBootTaskCard(PendingBootTask task)
	{
		bool isInProgress = string.Equals(task.State, PendingBootTaskStates.InProgress, StringComparison.Ordinal);
		bool isRequiredVenvCreate = IsRequiredVenvCreateTask(task.Id);
		var titleLabel = new Label
		{
			Text = SettingsStatusPresenter.GetPendingBootTaskTitle(task),
			TextColor = SettingsCardTitleColor,
			FontSize = 11,
			FontAttributes = FontAttributes.Bold
		};
		var detailLabel = new Label
		{
			Text = SettingsStatusPresenter.GetPendingBootTaskDetail(task),
			TextColor = NexusColors.TextDim,
			FontSize = 10,
			LineBreakMode = LineBreakMode.WordWrap
		};
		var cancelButton = new Button
		{
			Text = isInProgress
				? LocalizationManager.Text("settings.pending_tasks.running")
				: isRequiredVenvCreate
					? LocalizationManager.Text("settings.pending_tasks.required")
					: LocalizationManager.Text("common.cancel"),
			BackgroundColor = isInProgress || isRequiredVenvCreate ? Color.FromArgb("#0817222f") : NexusColors.SurfaceSubtle,
			TextColor = isInProgress || isRequiredVenvCreate ? SettingsDisabledTextColor : SettingsInfoActionTextColor,
			FontSize = 9,
			FontAttributes = FontAttributes.Bold,
			CornerRadius = 8,
			HeightRequest = 26,
			Padding = new Thickness(10, 0),
			VerticalOptions = LayoutOptions.Center,
			IsEnabled = !isInProgress && !isRequiredVenvCreate
		};
		if (Resources.TryGetValue("SettingsInlineButtonStyle", out object styleResource) && styleResource is Style inlineButtonStyle)
		{
			cancelButton.Style = inlineButtonStyle;
		}

		cancelButton.Clicked += (_, _) => CancelPendingBootTask(task.Id);
		var textStack = new VerticalStackLayout { Spacing = 2 };
		textStack.Children.Add(titleLabel);
		textStack.Children.Add(detailLabel);
		var layout = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto)
			},
			ColumnSpacing = 10
		};
		layout.Children.Add(textStack);
		layout.Children.Add(cancelButton);
		Grid.SetColumn(cancelButton, 1);

		return new Border
		{
			BackgroundColor = SettingsCardSurfaceColor,
			Stroke = SettingsCardAccentStrokeColor,
			StrokeThickness = 1,
			Padding = new Thickness(10, 8),
			StrokeShape = new RoundRectangle { CornerRadius = 10 },
			Content = layout
		};
	}

	private void CancelPendingBootTask(string taskId)
	{
		if (IsRequiredVenvCreateTask(taskId))
		{
			return;
		}

		if (!SettingsService.HasBootTask(taskId) && HasDraftBootTask(taskId))
		{
			RemoveDraftBootTask(taskId);
			UpdatePythonModeButtons();
			UpdateStateChrome();
			return;
		}

		switch (taskId)
		{
			case PendingBootTaskIds.RuntimePurge:
				SettingsService.ClearRuntimePurgeFlags();
				break;
			case PendingBootTaskIds.VenvDelete:
				SettingsService.CancelPendingVenvDelete();
				break;
			default:
				SettingsService.CancelBootTask(taskId);
				break;
		}

		SettingsService.Reload();
		_editor.Reload();
		Refresh();
	}

	private void UpdateSectionQueueHints(IReadOnlyList<PendingBootTask> pendingTasks)
	{
		PythonRuntimeQueueLabel.Text = SettingsStatusPresenter.FormatQueuedTasks(
			pendingTasks,
			task => task.Id is PendingBootTaskIds.VenvCreate
				or PendingBootTaskIds.VenvRebuild
				or PendingBootTaskIds.VenvDelete
				or PendingBootTaskIds.RuntimeRepair);

		MaintenanceQueueValueLabel.Text = SettingsStatusPresenter.FormatQueuedTasks(
			pendingTasks,
			task => task.Id is PendingBootTaskIds.RuntimePurge
				or PendingBootTaskIds.ResetSetup
				or PendingBootTaskIds.ResetAll);

		ExtensionsStatusValueLabel.Text = pendingTasks.Any(task => task.Id == PendingBootTaskIds.ExtensionRepair)
			? LocalizationManager.Text("settings.extensions.repair_queued")
			: ExtensionsStatusValueLabel.Text;
	}

	private string GetActiveOperationDisplay()
	{
		if (_activeOperations.Count == 0)
		{
			return LocalizationManager.Text("settings.operations.settings_operation");
		}

		return string.Join(", ", _activeOperations.Select(SettingsStatusPresenter.GetOperationLabel));
	}

	private static int GetPendingBootTaskOrder(string taskId)
		=> taskId switch
		{
			PendingBootTaskIds.RuntimePurge => 0,
			PendingBootTaskIds.ResetAll => 1,
			PendingBootTaskIds.ResetSetup => 2,
			PendingBootTaskIds.ComfyUpdate => 10,
			PendingBootTaskIds.VenvDelete => 30,
			PendingBootTaskIds.VenvRebuild => 31,
			PendingBootTaskIds.VenvCreate => 32,
			PendingBootTaskIds.RuntimeRepair => 40,
			PendingBootTaskIds.ExtensionRepair => 50,
			_ => 100
		};

	private void RebuildManagedExtensionOptions()
	{
		string customNodesPath = GetCustomNodesPath();
		var previousSelection = _managedExtensionOptions
			.Where(option => option.IsSelected)
			.Select(option => option.Folder)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		_managedExtensionOptions.Clear();
		AddManagedExtensionOption(HudBridgeInstaller.ManagerExtensionFolderName, HudBridgeInstaller.ManagerExtensionFolderName, customNodesPath, previousSelection);
		AddManagedExtensionOption(HudBridgeInstaller.HudExtensionFolderName, HudBridgeInstaller.HudExtensionFolderName, customNodesPath, previousSelection);
		AddManagedExtensionOption(HudBridgeInstaller.NexusBridgeExtensionFolderName, HudBridgeInstaller.NexusBridgeExtensionFolderName, customNodesPath, previousSelection);
		foreach (CustomNodeSetting node in _editor.Draft.EssentialNodes)
		{
			AddManagedExtensionOption(node.Folder, node.Folder, customNodesPath, previousSelection);
		}
	}

	private void AddManagedExtensionOption(
		string folder,
		string displayName,
		string customNodesPath,
		IReadOnlySet<string> previousSelection)
	{
		string path = System.IO.Path.Combine(customNodesPath, folder);
		bool isInstalled = string.Equals(folder, HudBridgeInstaller.NexusBridgeExtensionFolderName, StringComparison.OrdinalIgnoreCase)
			? HudBridgeInstaller.IsNexusBridgeExtensionHealthy(customNodesPath)
			: IsManagedExtensionInstalled(path);
		var option = new ManagedExtensionOption(folder, displayName, isInstalled, GetInitialManagedExtensionRevision(path, isInstalled));
		if (previousSelection.Contains(folder))
		{
			option.IsSelected = true;
		}

		_managedExtensionOptions.Add(option);
	}

	private static bool IsManagedExtensionInstalled(string path)
		=> Directory.Exists(path);

	private static string GetInitialManagedExtensionRevision(string path, bool isInstalled)
	{
		if (!isInstalled)
		{
			return "not installed";
		}

		return Directory.Exists(System.IO.Path.Combine(path, ".git"))
			? "installed - scan for git status"
			: "local package";
	}

	private static string RunGitMetadata(
		string workingDirectory,
		string arguments,
		string gitExecutable)
	{
#if WINDOWS
		try
		{
			var processInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = gitExecutable,
				Arguments = arguments,
				WorkingDirectory = workingDirectory,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			using var process = System.Diagnostics.Process.Start(processInfo);
			if (process == null)
			{
				return string.Empty;
			}

			if (!process.WaitForExit(800))
			{
				process.Kill(entireProcessTree: true);
				return string.Empty;
			}

			string output = process.StandardOutput.ReadToEnd().Trim();
			return process.ExitCode == 0 ? output : string.Empty;
		}
		catch
		{
			return string.Empty;
		}
#else
		return string.Empty;
#endif
	}

	private void RebuildManagedExtensionSelectionList()
	{
		ManagedExtensionSelectionList.Children.Clear();
		foreach (ManagedExtensionOption option in _managedExtensionOptions)
		{
			var checkBox = new CheckBox
			{
				IsChecked = option.IsSelected,
				Color = NexusColors.Accent,
				VerticalOptions = LayoutOptions.Center,
				Scale = 0.78
			};
			checkBox.CheckedChanged += (_, args) => option.IsSelected = args.Value;

			var title = new Label
			{
				Text = option.DisplayName,
				TextColor = SettingsCardTitleColor,
				FontSize = 10,
				FontAttributes = FontAttributes.Bold,
				VerticalTextAlignment = TextAlignment.Center,
				LineBreakMode = LineBreakMode.TailTruncation
			};
			var state = new Label
			{
				Text = option.IsInstalled ? "INSTALLED" : "MISSING",
				TextColor = option.IsInstalled ? SettingsSuccessTextColor : SettingsRequiredTextColor,
				FontSize = 9,
				FontAttributes = FontAttributes.Bold,
				VerticalTextAlignment = TextAlignment.Center,
				HorizontalTextAlignment = TextAlignment.End
			};
			var revision = new Label
			{
				Text = option.Revision,
				TextColor = option.IsInstalled ? NexusColors.TextDim : SettingsRequiredTextColor,
				FontSize = 9,
				VerticalTextAlignment = TextAlignment.Center,
				HorizontalTextAlignment = TextAlignment.End,
				LineBreakMode = LineBreakMode.TailTruncation
			};

			var row = new Grid
			{
				ColumnDefinitions =
				{
					new ColumnDefinition(GridLength.Auto),
					new ColumnDefinition(GridLength.Star),
					new ColumnDefinition(new GridLength(84)),
					new ColumnDefinition(new GridLength(180))
				},
				ColumnSpacing = 10,
				Padding = new Thickness(7, 2),
				MinimumHeightRequest = 30
			};
			row.Children.Add(checkBox);
			row.Children.Add(title);
			row.Children.Add(state);
			row.Children.Add(revision);
			Grid.SetColumn(title, 1);
			Grid.SetColumn(state, 2);
			Grid.SetColumn(revision, 3);

			ManagedExtensionSelectionList.Children.Add(new Border
			{
				BackgroundColor = SettingsCardSurfaceColor,
				Stroke = Color.FromArgb(option.IsInstalled ? "#1431d8ff" : "#33ffaa00"),
				StrokeThickness = 1,
				StrokeShape = new RoundRectangle { CornerRadius = 8 },
				Content = row
			});
		}
	}

	private string GetEffectiveComfyPath(SetupSettings settings)
	{
		if (string.Equals(settings.InstallMode, SetupInstallModes.ExistingComfyPath, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(settings.ComfyPath))
		{
			return settings.ComfyPath;
		}

		string activePath = _appManager.Paths.ActiveComfyPath;
		return string.IsNullOrWhiteSpace(activePath) ? _appManager.Paths.ManagedComfyPath : activePath;
	}

	private void SyncActiveComfyPathPreference(SetupSettings settings)
	{
		string comfyPath = string.Equals(settings.InstallMode, SetupInstallModes.ExistingComfyPath, StringComparison.Ordinal)
			? settings.ComfyPath
			: _appManager.Paths.ManagedComfyPath;
		if (string.IsNullOrWhiteSpace(comfyPath))
		{
			_appManager.Preferences.Remove(PreferenceKeys.ComfyUIPath);
			return;
		}

		_appManager.Preferences.Set(PreferenceKeys.ComfyUIPath, comfyPath);
	}

	private static string GetToolPathDisplay(string path)
		=> string.IsNullOrWhiteSpace(path) ? "(not configured)" : path;

	private static string GetSourceDisplay(string source)
		=> source switch
		{
			DiagnosticNodeHelpers.SystemOption => "System",
			DiagnosticNodeHelpers.BuiltInOption => "Built-in",
			DiagnosticNodeHelpers.CustomOption => "Custom",
			_ => "Unknown"
		};

	private static string ResolveToolProbePath(string source, string path, string fallback)
		=> string.Equals(source, DiagnosticNodeHelpers.SystemOption, StringComparison.Ordinal)
			? fallback
			: string.IsNullOrWhiteSpace(path) ? fallback : path;

	private void BeginComfyOperationLog(string title)
	{
		RunOnUiThread(() =>
		{
			_comfyOperationLogLines.Clear();
			_comfyOperationFullLogLines.Clear();
			ComfyOperationLogPanel.IsVisible = true;
			ComfyOperationLogTitleLabel.Text = title;
			ComfyOperationLogModalTitleLabel.Text = title;
			ComfyOperationLogStateLabel.Text = "RUNNING";
			ComfyOperationLogStateLabel.TextColor = SettingsInfoActionTextColor;
			ComfyOperationLogPanel.Stroke = ComfyOperationRunningStrokeColor;
			ComfyOperationLogPreviewLabel.TextColor = SettingsInfoTextColor;
			ComfyOperationLogPreviewLabel.Text = "Waiting for process output...";
			ComfyOperationLogFullLabel.Text = "Waiting for process output...";
		});
	}

	private void AppendComfyOperationLog(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return;
		}

		RunOnUiThread(() =>
		{
			_comfyOperationFullLogLines.Add(line.Trim());
			_comfyOperationLogLines.Enqueue(line.Trim());
			while (_comfyOperationLogLines.Count > 5)
			{
				_comfyOperationLogLines.Dequeue();
			}

			ComfyOperationLogPreviewLabel.Text = string.Join(Environment.NewLine, _comfyOperationLogLines);
			ComfyOperationLogFullLabel.Text = string.Join(Environment.NewLine, _comfyOperationFullLogLines);
		});
	}

	private void CompleteComfyOperationLog(bool isSuccess, string summary)
	{
		RunOnUiThread(() =>
		{
			ComfyOperationLogPanel.IsVisible = true;
			ComfyOperationLogStateLabel.Text = isSuccess ? "SUCCESS" : "FAILED";
			ComfyOperationLogStateLabel.TextColor = isSuccess ? SettingsSuccessTextColor : SettingsFailureTextColor;
			ComfyOperationLogPanel.Stroke = isSuccess ? ComfyOperationSuccessStrokeColor : ComfyOperationFailureStrokeColor;
			ComfyOperationLogPreviewLabel.TextColor = isSuccess ? SettingsSavedTextColor : ComfyOperationFailureTextColor;
			if (_comfyOperationLogLines.Count == 0)
			{
				ComfyOperationLogPreviewLabel.Text = summary;
				ComfyOperationLogFullLabel.Text = summary;
			}
		});
	}

	private void ResetMaintenanceBackupProgress(string message)
	{
		RunOnUiThread(() =>
		{
			MaintenanceBackupProgressBar.Progress = 0;
			RuntimeBackupAnalysisLabel.Text = message;
			RuntimeBackupAnalysisLabel.TextColor = SettingsInfoTextColor;
		});
	}

	private void UpdateMaintenanceBackupProgress(double progress, string message)
	{
		RunOnUiThread(() =>
		{
			MaintenanceBackupProgressBar.Progress = Math.Clamp(progress, 0, 1);
			RuntimeBackupAnalysisLabel.Text = string.IsNullOrWhiteSpace(message)
				? $"{Math.Clamp(progress, 0, 1):P0}"
				: $"{message} {Math.Clamp(progress, 0, 1):P0}";
			RuntimeBackupAnalysisLabel.TextColor = SettingsInfoTextColor;
		});
	}

	private void SetMaintenanceStatus(string message)
	{
		TryUpdateUi(() => MaintenanceStatusValueLabel.Text = message);
	}

	private bool CanUpdateUi()
		=> !_isUnloaded && Handler is not null;

	private bool TryUpdateUi(Action action)
	{
		if (!CanUpdateUi())
		{
			return false;
		}

		try
		{
			action();
			return true;
		}
		catch (System.Runtime.InteropServices.COMException ex)
		{
			NexusLog.Trace($"[SETTINGS:UI] Ignored stale UI update after unload: {ex.Message}");
			return false;
		}
		catch (ObjectDisposedException ex)
		{
			NexusLog.Trace($"[SETTINGS:UI] Ignored disposed UI update: {ex.Message}");
			return false;
		}
		catch (InvalidOperationException ex)
		{
			NexusLog.Trace($"[SETTINGS:UI] Ignored invalid UI update: {ex.Message}");
			return false;
		}
	}

	private void RunOnUiThread(Action action)
	{
		UiThread.TryBeginInvoke(() => TryUpdateUi(action), "SETTINGS:UI");
	}

	private bool TryBeginOperation(string operationId)
	{
		if (_activeOperations.Contains(operationId))
		{
			return false;
		}

		_activeOperations.Add(operationId);
		TryUpdateUi(UpdateStateChrome);
		return true;
	}

	private void EndOperation(string operationId)
	{
		_activeOperations.Remove(operationId);
		TryUpdateUi(UpdateStateChrome);
	}

	private async Task SetSettingsOperationBlockerVisibleAsync(
		bool isVisible,
		string title = "",
		string detail = "",
		bool allowCancel = false,
		double? progress = null)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Settings.OperationBlocker.Visibility");
		if (!CanUpdateUi())
		{
			return;
		}

		try
		{
			if (isVisible)
			{
				SettingsOperationBlockerTitleLabel.Text = title;
				SettingsOperationBlockerDetailLabel.Text = detail;
				SettingsOperationBlockerCancelButton.IsVisible = allowCancel;
				UpdateSettingsOperationBlockerProgress(progress);
				SettingsOperationBlocker.IsVisible = true;
				SettingsOperationBlocker.Opacity = 0;
				await SafeAnimation.FadeToAsync(SettingsOperationBlocker, 1, OperationBlockerShowAnimationLength, Easing.CubicOut, "Settings.OperationBlocker");
				return;
			}

			if (!SettingsOperationBlocker.IsVisible)
			{
				return;
			}

			await SafeAnimation.FadeToAsync(SettingsOperationBlocker, 0, OperationBlockerHideAnimationLength, Easing.CubicIn, "Settings.OperationBlocker");
			if (CanUpdateUi())
			{
				SettingsOperationBlocker.IsVisible = false;
				SettingsOperationBlockerCancelButton.IsVisible = false;
				SettingsOperationBlockerProgressGrid.IsVisible = false;
			}
		}
		catch (System.Runtime.InteropServices.COMException ex)
		{
			NexusLog.Trace($"[SETTINGS:UI] Ignored stale blocker update after unload: {ex.Message}");
		}
		catch (ObjectDisposedException ex)
		{
			NexusLog.Trace($"[SETTINGS:UI] Ignored disposed blocker update: {ex.Message}");
		}
		catch (InvalidOperationException ex)
		{
			NexusLog.Trace($"[SETTINGS:UI] Ignored invalid blocker update: {ex.Message}");
		}
	}

	private void UpdateSettingsOperationBlockerProgress(double? progress)
	{
		if (!CanUpdateUi())
		{
			return;
		}

		if (progress is null)
		{
			SettingsOperationBlockerProgressGrid.IsVisible = false;
			SettingsOperationBlockerProgressBar.Progress = 0;
			SettingsOperationBlockerProgressLabel.Text = string.Empty;
			return;
		}

		double safeProgress = Math.Clamp(progress.Value, 0, 1);
		SettingsOperationBlockerProgressGrid.IsVisible = true;
		SettingsOperationBlockerProgressBar.Progress = safeProgress;
		SettingsOperationBlockerProgressLabel.Text = $"{safeProgress:P0}";
	}

	private void OnSettingsOperationBlockerCancelClicked(object? sender, EventArgs e)
		=> _modelDuplicateScanCancellation?.Cancel();

	private void SetComfyActionBusy(bool isBusy)
	{
		_isComfyActionBusy = isBusy;
		bool isLocal = string.Equals(_editor.Draft.InstallMode, SetupInstallModes.LocalRuntime, StringComparison.Ordinal);
		UseLocalRuntimeButton.IsEnabled = !isBusy;
		UseCustomComfyButton.IsEnabled = !isBusy;
		UseRemoteComfyCoreButton.IsEnabled = !isBusy && isLocal;
		UseBuiltInComfyCoreButton.IsEnabled = !isBusy && isLocal;
		OpenComfyFolderButton.IsEnabled = !isBusy;
		ChangeComfyPathButton.IsEnabled = !isBusy && !isLocal;
		CheckComfyUpdatesButton.IsEnabled = !isBusy;
		ComfyApplyUpdateButton.IsEnabled = !isBusy && _comfyUpdatesAvailable > 0;
		UpdateStateChrome();
	}

	private void SetMaintenanceBusy(bool isBusy)
	{
		TryUpdateUi(() =>
		{
			_isMaintenanceBusy = isBusy;
			MaintenanceClearServerLogButton.IsEnabled = !isBusy;
			MaintenanceResetSettingsButton.IsEnabled = !isBusy;
			MaintenanceBackupRuntimeButton.IsEnabled = !isBusy;
			MaintenanceRestoreRuntimeButton.IsEnabled = !isBusy;
			MaintenanceDeleteBackupButton.IsEnabled = !isBusy;
			MaintenanceOpenBackupFolderButton.IsEnabled = !isBusy;
			RuntimeBackupChangePathButton.IsEnabled = !isBusy;
			RuntimeBackupOpenPathButton.IsEnabled = !isBusy;
			RuntimeBackupFolderFormatButton.IsEnabled = !isBusy;
			RuntimeBackupZipFormatButton.IsEnabled = !isBusy;
			RuntimeBackupModelsTargetButton.IsEnabled = !isBusy;
			RuntimeBackupCustomNodesTargetButton.IsEnabled = !isBusy;
			RuntimeBackupInputTargetButton.IsEnabled = !isBusy;
			RuntimeBackupOutputTargetButton.IsEnabled = !isBusy;
			RuntimeBackupWorkflowsTargetButton.IsEnabled = !isBusy;
			MaintenancePurgeRuntimeButton.IsEnabled = !isBusy;
			UpdateRuntimeBackupOptionVisuals();
			UpdateStateChrome();
		});
	}

	private void SetExtensionsBusy(bool isBusy, string title = "", string detail = "")
	{
		ExtensionsScanButton.IsEnabled = !isBusy;
		ExtensionsSyncUpdateButton.IsEnabled = !isBusy;
		ExtensionsReinstallButton.IsEnabled = !isBusy;
		ExtensionsOpenFolderButton.IsEnabled = !isBusy;
		ManagedExtensionsBlockerTitleLabel.Text = title;
		ManagedExtensionsBlockerDetailLabel.Text = detail;
		ManagedExtensionsActivity.IsRunning = isBusy;
		ManagedExtensionsBlocker.IsVisible = isBusy;
		UpdateStateChrome();
	}

	private void SetHudSamplesBusy(bool isBusy)
	{
		ExtensionsRestoreHudSamplesButton.IsEnabled = !isBusy;
		UpdateStateChrome();
	}

	private static async Task<string> ResolveToolDisplayPathAsync(
		string source,
		string path,
		string fallback,
		CancellationToken cancellationToken = default)
	{
		if (!string.Equals(source, DiagnosticNodeHelpers.SystemOption, StringComparison.Ordinal))
		{
			return GetToolPathDisplay(path);
		}

		try
		{
			var (exitCode, output, _) = await ProcessRunner.RunAsync("where.exe", fallback, null, null, cancellationToken);
			string? firstPath = output
				.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
				.FirstOrDefault();
			return exitCode == 0 && !string.IsNullOrWhiteSpace(firstPath)
				? firstPath
				: $"{fallback} (not found on PATH)";
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			return $"{fallback} (path lookup failed: {ex.Message})";
		}
	}

	private void OnCloseClicked(object? sender, EventArgs e) => CloseRequested?.Invoke(this, e);
	private void OnPendingBootChecklistToggleClicked(object? sender, EventArgs e)
	{
		_isPendingBootChecklistExpanded = !_isPendingBootChecklistExpanded;
		UpdatePendingBootChecklist(GetEffectivePendingBootTasks());
	}

	private void OnViewComfyOperationLogClicked(object? sender, EventArgs e)
	{
		ComfyOperationLogModal.IsVisible = true;
		ComfyOperationLogFullLabel.Text = _comfyOperationFullLogLines.Count == 0
			? ComfyOperationLogPreviewLabel.Text
			: string.Join(Environment.NewLine, _comfyOperationFullLogLines);
	}

	private void OnCloseComfyOperationLogClicked(object? sender, EventArgs e)
	{
		ComfyOperationLogModal.IsVisible = false;
	}

	private async void OnRestartClicked(object? sender, EventArgs e)
	{
		ApplyDraftInputs();
		SettingsEditorState state = _editor.Evaluate();
		var pendingTasks = GetEffectivePendingBootTasks();
		if (state.HasUnsavedChanges)
		{
			string queuedSuffix = pendingTasks.Count == 0
				? string.Empty
				: $"{Environment.NewLine}{Environment.NewLine}{LocalizationManager.Text("settings.pending_tasks.queued_for_next_boot_heading")}:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", pendingTasks.Select(SettingsStatusPresenter.GetPendingBootTaskTitle))}";
			bool confirmed = await ShowConfirmationAsync(
				LocalizationManager.Text("settings.restart.save_restart_title"),
				LocalizationManager.Format("settings.restart.save_restart_message", queuedSuffix),
				LocalizationManager.Text("settings.actions.save_restart"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}
		}
		else if (pendingTasks.Count > 0)
		{
			bool confirmed = await ShowConfirmationAsync(
				LocalizationManager.Text("settings.restart.run_tasks_title"),
				LocalizationManager.Format(
					"settings.restart.run_tasks_message",
					string.Join($"{Environment.NewLine}- ", pendingTasks.Select(SettingsStatusPresenter.GetPendingBootTaskTitle))),
				LocalizationManager.Text("settings.actions.run_restart"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}
		}

		if (SaveDraft(refresh: false))
		{
			bool repairRuntimeBeforeBoot = ShouldRepairRuntimeBeforeSettingsRestart();
			ClearRestartHandoffFlags();
			RestartServerRequested?.Invoke(this, new SettingsRestartRequestedEventArgs(repairRuntimeBeforeBoot));
		}
	}

	private void ClearRestartHandoffFlags()
	{
		_modelLibrariesRestartRequired = false;
		UpdateStateChrome();
	}

	private bool ShouldRepairRuntimeBeforeSettingsRestart()
	{
		if (SettingsService.HasRunnableBootTasks())
		{
			return false;
		}

		if (RuntimePythonModePresenter.HasPendingVenvDelete(SettingsService.Settings)
			|| RuntimePythonModePresenter.HasPendingVenvDelete(_editor.Draft))
		{
			return false;
		}

		if (_venvRestartRequired)
		{
			return true;
		}

		SettingsEditorState state = _editor.Evaluate();
		return state.RestartReasons.Any(reason =>
			reason.Contains("Python", StringComparison.OrdinalIgnoreCase)
			|| reason.Contains("ComfyUI path", StringComparison.OrdinalIgnoreCase));
	}

	private void OnDraftTextChanged(object? sender, TextChangedEventArgs e) => ApplyDraftInputs();
	private void OnDiscardClicked(object? sender, EventArgs e)
	{
		_editor.Discard();
		Refresh();
	}

	private async void OnSaveClicked(object? sender, EventArgs e)
	{
		bool languageChanged = IsDraftLanguageChanged();
		if (SaveDraft())
		{
			await ShowLanguageRestartHintAsync(languageChanged);
		}
	}

	private bool IsDraftLanguageChanged()
		=> !string.Equals(
			GetEffectiveLanguageCode(_editor.Draft.LanguageCode),
			LocalizationManager.NormalizeLanguageCode(LocalizationManager.ActiveLanguage),
			StringComparison.OrdinalIgnoreCase);

	private static string GetEffectiveLanguageCode(string languageCode)
		=> string.IsNullOrWhiteSpace(languageCode)
			? LocalizationManager.ActiveLanguage
			: LocalizationManager.NormalizeLanguageCode(languageCode);

	private async Task ShowLanguageRestartHintAsync(bool languageChanged)
	{
		if (!languageChanged)
		{
			return;
		}

		var page = Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page != null)
		{
			await page.DisplayAlertAsync(
				"Restart Nexus to apply language",
				"Language changes are saved. Close and reopen Nexus to apply the selected language everywhere.",
				"OK");
		}
	}

	private void OnLanguagePickerSelectedIndexChanged(object? sender, EventArgs e)
	{
		if (_isRefreshing || _isUpdatingLanguagePicker || LanguagePicker.SelectedItem is not LanguageOption option)
		{
			return;
		}

		_editor.Draft.LanguageCode = option.Code;
		UpdateStateChrome();
	}

	private void OnGpuPickerSelectedIndexChanged(object? sender, EventArgs e)
	{
		if (_isUpdatingGpuPicker || GpuPicker.SelectedIndex < 0 || GpuPicker.SelectedIndex >= _gpuDevices.Count)
		{
			return;
		}

		SelectGpuDevice(_gpuDevices[GpuPicker.SelectedIndex].Id);
	}

	private enum VenvMaintenanceAction
	{
		Create,
		Reset,
		Delete
	}

	private enum HudSampleRestoreMode
	{
		MissingOnly,
		Replace
	}

	private static string GetProcessError((int ExitCode, string Output, string Error) result)
	{
		string message = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
		message = message.Trim();
		return string.IsNullOrWhiteSpace(message)
			? $"process exited with code {result.ExitCode}"
			: message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? message;
	}

	private async void OnNexusClicked(object? sender, EventArgs e) => await ScrollToSectionAsync(NexusSection);
	private async void OnComfyClicked(object? sender, EventArgs e) => await ScrollToSectionAsync(ComfySection);
	private async void OnToolsClicked(object? sender, EventArgs e) => await ScrollToSectionAsync(ToolsSection);
	private async void OnServerClicked(object? sender, EventArgs e) => await ScrollToSectionAsync(ServerSection);
	private async void OnMaintenanceClicked(object? sender, EventArgs e) => await ScrollToSectionAsync(MaintenanceSection);
	private async void OnExtensionsClicked(object? sender, EventArgs e) => await ScrollToSectionAsync(ExtensionsSection);
}

internal sealed class SettingsRestartRequestedEventArgs : EventArgs
{
	internal SettingsRestartRequestedEventArgs(bool repairRuntimeBeforeBoot)
	{
		RepairRuntimeBeforeBoot = repairRuntimeBeforeBoot;
	}

	internal bool RepairRuntimeBeforeBoot { get; }
}

internal sealed class RuntimeRestoreRequestedEventArgs : EventArgs
{
	internal RuntimeRestoreRequestedEventArgs(RuntimeRestoreRequest request)
	{
		Request = request;
	}

	internal RuntimeRestoreRequest Request { get; }
	internal TaskCompletionSource<RuntimeRestoreResult> Completion { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
}
