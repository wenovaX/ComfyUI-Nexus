namespace ComfyUI_Nexus.Views.Overlays;

using System.Globalization;
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
using Microsoft.Maui.Controls.Shapes;

public partial class SettingsOverlayView : ContentView
{
	private const double PanelViewportScale = 0.8;
	private const double MinimumPanelWidth = 980;
	private const double MinimumPanelHeight = 680;
	private const double ScaleThresholdWidth = 1280;
	private const double ScaleThresholdHeight = 720;
	private const double HiddenPanelScale = 0.98;
	private const double HiddenPanelOffsetY = 12;
	private const double HidePanelOffsetY = 10;
	private const double ShownGlowOpacity = 0.18;
	private const uint ShowAnimationLength = 170;
	private const uint ShowGlowAnimationLength = 260;
	private const uint HideAnimationLength = 120;
	private const uint HideGlowAnimationLength = 150;
	private const uint OperationBlockerShowAnimationLength = 110;
	private const uint OperationBlockerHideAnimationLength = 90;
	private const string OperationBlockerLogoBounceAnimationName = "SettingsOperationBlockerLogoBounce";
	private const uint OperationBlockerLogoBounceLength = 1800;
	private const double OperationBlockerLogoBounceHeight = 12;
	private const int PanelGlowAnimationRate = 16;
	private const string PanelGlowAnimationName = "SettingsPanelGlow";
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

	private readonly SettingsEditorService _editor = new();
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
	private RuntimeBackupAnalysis? _runtimeBackupAnalysis;
	private int _runtimeBackupAnalysisGeneration;
	private ModelDuplicateScanResult? _modelDuplicateScanResult;
	private int _modelDuplicateGroupIndex;
	private int _modelDuplicateScanGeneration;
	private CancellationTokenSource? _modelDuplicateScanCancellation;
	private int _comfyUpdatesAvailable;
	private readonly Queue<string> _comfyOperationLogLines = new();
	private readonly List<string> _comfyOperationFullLogLines = new();
	private string _lastCustomComfyPath = string.Empty;
	private int _toolVersionProbeId;
	private int _gpuProbeId;
	private int _extensionsProbeId;
	private int _completedToolVersionProbeId;
	private int _completedGpuProbeId;
	private Task? _toolVersionProbeTask;
	private Task? _gpuProbeTask;
	private bool _isUnloaded;
	private bool _isLayoutPrewarmed;

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
			InitializeComponent();
			WireRuntimeBackupOptionHover(RuntimeBackupFolderFormatButton, () => IsRuntimeBackupFormat(RuntimeBackupFormats.Folder));
			WireRuntimeBackupOptionHover(RuntimeBackupZipFormatButton, () => IsRuntimeBackupFormat(RuntimeBackupFormats.Zip));
			WireRuntimeBackupOptionHover(RuntimeBackupModelsTargetButton, () => _runtimeBackupModelsSelected);
			WireRuntimeBackupOptionHover(RuntimeBackupCustomNodesTargetButton, () => _runtimeBackupCustomNodesSelected);
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
	}

	private void OnSettingsOverlayUnloaded(object? sender, EventArgs e)
	{
		_isUnloaded = true;
	}

	internal bool IsOverlayVisible => IsVisible;

	internal bool IsShown(bool isVisible)
		=> IsVisible == isVisible && Math.Abs(Opacity - (isVisible ? 1 : 0)) < 0.01;

	internal async Task PrewarmLayoutAsync()
	{
		if (_isLayoutPrewarmed)
		{
			return;
		}

		bool wasVisible = IsVisible;
		bool wasInputTransparent = InputTransparent;
		double previousOpacity = Opacity;
		double previousScale = Scale;
		double previousTranslationY = TranslationY;
		float previousGlowOpacity = SettingsPanelGlow.Opacity;

		try
		{
			PrepareToShow();
			InputTransparent = true;
			Opacity = 0;
			Scale = 1;
			TranslationY = 0;
			await Task.Yield();
			InvalidateMeasure();
			SettingsPanelHost.InvalidateMeasure();
			SettingsPanelBorder.InvalidateMeasure();
			await Task.Yield();
			ResetHiddenState();
			_isLayoutPrewarmed = true;
		}
		finally
		{
			IsVisible = wasVisible;
			InputTransparent = wasInputTransparent;
			Opacity = previousOpacity;
			Scale = previousScale;
			TranslationY = previousTranslationY;
			SettingsPanelGlow.Opacity = previousGlowOpacity;
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

	internal void Refresh(bool startProbes = true)
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
		VersionValueLabel.Text = $"Version {AppInfo.Current.VersionString}";
		ServerUrlValueLabel.Text = $"Server http://{settings.ListenAddress}:{settings.ServerPort}";
		SettingsStateValueLabel.Text = settings.LastLaunchSuccessful
			? $"Last launch succeeded on port {(settings.LastActivePort is int lastActivePort ? lastActivePort.ToString(CultureInfo.InvariantCulture) : "unknown")}"
			: "Last launch is not marked successful";
		ExtensionsValueLabel.Text = $"{settings.EssentialNodes.Count + 2} Nexus-managed extension target(s)";
		ExtensionsStatusValueLabel.Text = LocalizationManager.Format(
			"views.overlays.settings_overlay_view.custom_nodes_folder_status",
			GetCustomNodesPath());
		ExtensionsStatusValueLabel.TextColor = SettingsInfoTextColor;
		RebuildManagedExtensionOptions();
		RebuildManagedExtensionSelectionList();
		UpdateComfyModeButtons();
		RebuildModelLibrariesList();
		UpdateModelDuplicateScanUi();
		RefreshRuntimeBackupCard();
		UpdateToolButtons();
		UpdatePythonModeButtons();
		UpdateGpuSelectionVisuals();
		UpdatePipCacheCard();
		_isRefreshing = false;
		UpdateStateChrome();
		if (!startProbes)
		{
			return;
		}

		RequestToolVersionProbe();
		RequestGpuProbe();
	}

	internal void PrepareToShow()
	{
		_editor.Reload();
		Refresh(startProbes: false);
		UpdatePanelSize();
		ResetScrollPosition();
		IsVisible = true;
		InputTransparent = false;
		SettingsPanelGlow.Opacity = 0;
		Scale = HiddenPanelScale;
		TranslationY = HiddenPanelOffsetY;
	}

	internal void PrepareToHide()
	{
		InputTransparent = true;
	}

	internal void ResetHiddenState()
	{
		IsVisible = false;
		Scale = 1;
		TranslationY = 0;
		SettingsPanelGlow.Opacity = 0;
	}

	internal async Task AnimateShowAsync()
	{
		await AnimateShowCoreAsync();
		StartDeferredProbes();
	}

	private Task AnimateShowCoreAsync()
		=> AnimatePanelShowAsync();

	private async Task AnimatePanelShowAsync()
	{
		await Task.WhenAll(
			this.FadeToAsync(1, ShowAnimationLength, Easing.CubicOut),
			this.TranslateToAsync(0, 0, ShowAnimationLength, Easing.CubicOut),
			this.ScaleToAsync(1, ShowAnimationLength, Easing.CubicOut));
		await AnimatePanelGlowAsync(ShownGlowOpacity, ShowGlowAnimationLength, Easing.CubicOut);
	}

	internal Task AnimateHideAsync()
	{
		return Task.WhenAll(
			AnimatePanelGlowAsync(0, HideGlowAnimationLength, Easing.CubicIn),
			this.FadeToAsync(0, HideAnimationLength, Easing.CubicIn),
			this.TranslateToAsync(0, HidePanelOffsetY, HideAnimationLength, Easing.CubicIn),
			this.ScaleToAsync(HiddenPanelScale, HideAnimationLength, Easing.CubicIn));
	}

	private Task AnimatePanelGlowAsync(double targetOpacity, uint length, Easing easing)
	{
		var completion = new TaskCompletionSource();
		new Animation(value => SettingsPanelGlow.Opacity = (float)value, SettingsPanelGlow.Opacity, targetOpacity, easing)
			.Commit(this, PanelGlowAnimationName, PanelGlowAnimationRate, length, finished: (_, _) => completion.TrySetResult());
		return completion.Task;
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
		}

		if (!_editor.Save())
		{
			yamlTransaction?.Rollback();
			_ = ShowValidationAlertAsync(LocalizationManager.Text("settings.model_libraries.settings_save_failed"));
			return false;
		}

		yamlTransaction?.Commit();
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

			SetupSettingsService.Instance.EnqueueBootTask(taskId, targetFolders: targetFolders, action: action);
			SetupSettingsService.Instance.Reload();
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
			&& SetupSettingsService.Instance.Settings.ModelLibraryRoots.Count > 0;
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

		if (!SetupSettingsService.Instance.HasBootTask(taskId) && HasDraftBootTask(taskId))
		{
			RemoveDraftBootTask(taskId);
			UpdatePythonModeButtons();
			UpdateStateChrome();
			return;
		}

		switch (taskId)
		{
			case PendingBootTaskIds.RuntimePurge:
				SetupSettingsService.Instance.ClearRuntimePurgeFlags();
				break;
			case PendingBootTaskIds.VenvDelete:
				SetupSettingsService.Instance.CancelPendingVenvDelete();
				break;
			default:
				SetupSettingsService.Instance.CancelBootTask(taskId);
				break;
		}

		SetupSettingsService.Instance.Reload();
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

	private static string GetRuntimeBackupLabel(string target)
		=> target switch
		{
			RuntimeBackupTargets.Models => "models",
			RuntimeBackupTargets.CustomNodes => "custom_nodes",
			_ => target
		};

	private List<string> GetSelectedRuntimeBackupTargets()
	{
		var targets = new List<string>();
		if (_runtimeBackupModelsSelected)
		{
			targets.Add(RuntimeBackupTargets.Models);
		}

		if (_runtimeBackupCustomNodesSelected)
		{
			targets.Add(RuntimeBackupTargets.CustomNodes);
		}

		return targets;
	}

	private bool IsRuntimeBackupFormat(string format)
		=> string.Equals(_editor.Draft.RuntimeBackupFormat, format, StringComparison.Ordinal);

	private void RefreshRuntimeBackupCard()
	{
		string path = RuntimeBackupService.GetConfiguredBackupRoot(_editor.Draft);
		RuntimeBackupPathLabel.Text = path;
		if (RuntimeBackupService.TryGetAvailableSpace(path, out long availableBytes, out string error))
		{
			RuntimeBackupFreeSpaceLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.available_space",
				RuntimeBackupService.FormatBytes(availableBytes));
			RuntimeBackupFreeSpaceLabel.TextColor = SettingsMutedTextColor;
		}
		else
		{
			RuntimeBackupFreeSpaceLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.space_unavailable",
				error);
			RuntimeBackupFreeSpaceLabel.TextColor = SettingsWarningTextColor;
		}

		UpdateRuntimeBackupOptionVisuals();
		RebuildRuntimeBackupExternalLibraries();
		if (_runtimeBackupAnalysis is null)
		{
			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Text("settings.runtime_backup.ready_to_calculate");
			RuntimeBackupAnalysisLabel.TextColor = SettingsInfoTextColor;
		}
	}

	private void UpdateRuntimeBackupOptionVisuals()
	{
		ApplyRuntimeBackupOptionVisual(
			RuntimeBackupFolderFormatButton,
			IsRuntimeBackupFormat(RuntimeBackupFormats.Folder));
		ApplyRuntimeBackupOptionVisual(
			RuntimeBackupZipFormatButton,
			IsRuntimeBackupFormat(RuntimeBackupFormats.Zip));
		ApplyRuntimeBackupOptionVisual(RuntimeBackupModelsTargetButton, _runtimeBackupModelsSelected);
		ApplyRuntimeBackupOptionVisual(RuntimeBackupCustomNodesTargetButton, _runtimeBackupCustomNodesSelected);
		MaintenanceBackupRuntimeButton.IsEnabled = !_isMaintenanceBusy
			&& (_runtimeBackupModelsSelected || _runtimeBackupCustomNodesSelected);
	}

	private static void ApplyRuntimeBackupOptionVisual(Button button, bool isSelected)
	{
		if (!button.IsEnabled)
		{
			button.BackgroundColor = Color.FromArgb("#0617222f");
			button.TextColor = Color.FromArgb("#41576a");
			return;
		}

		button.BackgroundColor = isSelected
			? Color.FromArgb("#2631d8ff")
			: Color.FromArgb("#0Affffff");
		button.TextColor = isSelected
			? Color.FromArgb("#f4feff")
			: Color.FromArgb("#7893a8");
	}

	private void WireRuntimeBackupOptionHover(Button button, Func<bool> isSelected)
	{
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) =>
		{
			if (!button.IsEnabled) return;
			button.BackgroundColor = Color.FromArgb("#2031d8ff");
			button.TextColor = Color.FromArgb("#f4feff");
		};
		pointer.PointerPressed += (_, _) =>
		{
			if (!button.IsEnabled) return;
			button.BackgroundColor = Color.FromArgb("#3031d8ff");
			button.TextColor = Colors.White;
		};
		pointer.PointerReleased += (_, _) => ApplyRuntimeBackupOptionVisual(button, isSelected());
		pointer.PointerExited += (_, _) => ApplyRuntimeBackupOptionVisual(button, isSelected());
		button.GestureRecognizers.Add(pointer);
	}

	private void RebuildRuntimeBackupExternalLibraries()
	{
		RuntimeBackupExternalLibrariesList.Children.Clear();
		var roots = _editor.Draft.ModelLibraryRoots
			.Select(ExtraModelPathsService.NormalizeFileSystemPath)
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.ToList();
		RuntimeBackupExternalLibrariesPanel.IsVisible = roots.Count > 0;
		foreach (string path in roots)
		{
			var button = new Button
			{
				Text = path,
				Style = (Style)Resources["SettingsTextButtonStyle"],
				CommandParameter = path,
				IsEnabled = Directory.Exists(path),
				LineBreakMode = LineBreakMode.TailTruncation,
				MaximumWidthRequest = 300,
				Margin = new Thickness(0, 0, 6, 4)
			};
			button.Clicked += OnRuntimeBackupExternalLibraryClicked;
			RuntimeBackupExternalLibrariesList.Children.Add(button);
		}
	}

	private void InvalidateRuntimeBackupAnalysis(string? message = null)
	{
		_runtimeBackupAnalysisGeneration++;
		_runtimeBackupAnalysis = null;
		MaintenanceBackupProgressBar.Progress = 0;
		RuntimeBackupActivity.IsRunning = false;
		RuntimeBackupActivity.IsVisible = false;
		RuntimeBackupAnalysisLabel.Text = message
			?? LocalizationManager.Text("settings.runtime_backup.ready_to_calculate");
		RuntimeBackupAnalysisLabel.TextColor = SettingsInfoTextColor;
	}

	private async void OnRuntimeBackupChangePathClicked(object? sender, EventArgs e)
	{
		var result = await PlatformManager.Current.FilePicker.PickFolderAsync(
			LocalizationManager.Text("settings.runtime_backup.select_destination"));
		if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
		{
			if (!string.IsNullOrWhiteSpace(result.Message))
			{
				SetMaintenanceStatus(result.Message);
			}
			return;
		}

		if (!_editor.SaveRuntimeBackupPreferences(result.Value, _editor.Draft.RuntimeBackupFormat))
		{
			await ShowValidationAlertAsync(LocalizationManager.Text("settings.runtime_backup.preference_save_failed"));
			return;
		}

		InvalidateRuntimeBackupAnalysis(LocalizationManager.Text("settings.runtime_backup.destination_changed"));
		RefreshRuntimeBackupCard();
		UpdateStateChrome();
	}

	private async void OnRuntimeBackupFolderFormatClicked(object? sender, EventArgs e)
		=> await SaveRuntimeBackupFormatAsync(RuntimeBackupFormats.Folder);

	private async void OnRuntimeBackupZipFormatClicked(object? sender, EventArgs e)
		=> await SaveRuntimeBackupFormatAsync(RuntimeBackupFormats.Zip);

	private async Task SaveRuntimeBackupFormatAsync(string format)
	{
		if (IsRuntimeBackupFormat(format))
		{
			return;
		}

		if (!_editor.SaveRuntimeBackupPreferences(_editor.Draft.RuntimeBackupPath, format))
		{
			await ShowValidationAlertAsync(LocalizationManager.Text("settings.runtime_backup.preference_save_failed"));
			return;
		}

		InvalidateRuntimeBackupAnalysis(LocalizationManager.Text("settings.runtime_backup.format_changed"));
		RefreshRuntimeBackupCard();
		UpdateStateChrome();
	}

	private void OnRuntimeBackupModelsTargetClicked(object? sender, EventArgs e)
	{
		_runtimeBackupModelsSelected = !_runtimeBackupModelsSelected;
		InvalidateRuntimeBackupAnalysis();
		UpdateRuntimeBackupOptionVisuals();
	}

	private void OnRuntimeBackupCustomNodesTargetClicked(object? sender, EventArgs e)
	{
		_runtimeBackupCustomNodesSelected = !_runtimeBackupCustomNodesSelected;
		InvalidateRuntimeBackupAnalysis();
		UpdateRuntimeBackupOptionVisuals();
	}

	private async void OnRuntimeBackupExternalLibraryClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: string path } || !Directory.Exists(path))
		{
			return;
		}

		var result = await PlatformManager.Current.Shell.OpenPathAsync(path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			SetMaintenanceStatus(result.Message);
		}
	}

	private static string RuntimeBackupsPath
		=> ComfyInstallService.RuntimeBackupsPath;

	private static IReadOnlyList<RuntimeBackupEntry> GetRuntimeBackupFolders()
		=> ComfyInstallService.Instance.GetRuntimeBackups(includeIncomplete: false);

	private static IReadOnlyList<RuntimeBackupEntry> GetRuntimeBackupDeleteFolders()
		=> ComfyInstallService.Instance.GetRuntimeBackups(includeIncomplete: true);

	private async Task<string?> PickRuntimeRestoreFolderAsync()
	{
		var backupFolders = GetRuntimeBackupFolders();
		var page = Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page is not null)
		{
			string[] options = backupFolders
				.Select(entry => entry.Name)
				.Append(LocalizationManager.Text("settings.runtime_backup.browse_folder"))
				.Append(LocalizationManager.Text("settings.runtime_backup.browse_zip"))
				.ToArray();
			string? selection = await page.DisplayActionSheetAsync(
				LocalizationManager.Text("settings.runtime_backup.restore_title"),
				LocalizationManager.Text("common.cancel"),
				null,
				options);
			if (string.IsNullOrWhiteSpace(selection)
				|| string.Equals(selection, LocalizationManager.Text("common.cancel"), StringComparison.Ordinal))
			{
				return null;
			}

			if (string.Equals(selection, LocalizationManager.Text("settings.runtime_backup.browse_zip"), StringComparison.Ordinal))
			{
				return await PickRuntimeBackupZipAsync();
			}

			if (!string.Equals(selection, LocalizationManager.Text("settings.runtime_backup.browse_folder"), StringComparison.Ordinal))
			{
				return backupFolders.FirstOrDefault(entry =>
					string.Equals(entry.Name, selection, StringComparison.Ordinal))?.Path;
			}
		}

		var result = await PlatformManager.Current.FilePicker.PickFolderAsync(
			LocalizationManager.Text("settings.runtime_backup.select_backup_folder"));
		if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
		{
			return result.Value;
		}

		if (!string.IsNullOrWhiteSpace(result.Message))
		{
			SetMaintenanceStatus(result.Message);
		}

		return null;
	}

	private async Task<string?> PickRuntimeBackupZipAsync()
	{
		var result = await PlatformManager.Current.FilePicker.PickFileAsync(
			LocalizationManager.Text("settings.runtime_backup.select_backup_zip"),
			[".zip"]);
		if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
		{
			return result.Value;
		}

		if (!string.IsNullOrWhiteSpace(result.Message))
		{
			SetMaintenanceStatus(result.Message);
		}

		return null;
	}

	private async Task<string?> PickRuntimeBackupFolderToDeleteAsync()
	{
		var backupFolders = GetRuntimeBackupDeleteFolders();
		var page = Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page is null || backupFolders.Count == 0)
		{
			return null;
		}

		var options = backupFolders.ToDictionary(
			entry => $"{entry.Name} [{LocalizationManager.Text(entry.IsComplete
				? "settings.runtime_backup.status_complete"
				: "settings.runtime_backup.status_incomplete")}]",
			entry => entry.Path,
			StringComparer.Ordinal);
		string? selection = await page.DisplayActionSheetAsync(
			LocalizationManager.Text("settings.runtime_backup.delete_title"),
			LocalizationManager.Text("common.cancel"),
			null,
			options.Keys.ToArray());
		return !string.IsNullOrWhiteSpace(selection) && options.TryGetValue(selection, out string? path)
			? path
			: null;
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

	private void UpdateComfyModeButtons()
	{
		bool isLocal = string.Equals(_editor.Draft.InstallMode, SetupInstallModes.LocalRuntime, StringComparison.Ordinal);
		ApplyChipState(UseLocalRuntimeButton, isLocal);
		ApplyChipState(UseCustomComfyButton, !isLocal);
		InstallModeValueLabel.Text = GetInstallModeDisplay(_editor.Draft);
		ComfyPathValueLabel.Text = GetEffectiveComfyPath(_editor.Draft);
		ChangeComfyPathButton.IsVisible = !isLocal;
		ChangeComfyPathButton.IsEnabled = !isLocal && !_isComfyActionBusy;
	}

	private void UpdatePythonModeButtons()
	{
		bool useVenv = string.Equals(_editor.Draft.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal);
		ApplyChipState(UseVenvButton, useVenv);
		ApplyChipState(UseConfiguredPythonButton, !useVenv);
		PythonRuntimeSummaryLabel.Text = useVenv
			? "Nexus will launch ComfyUI through the managed .venv environment."
			: $"Nexus will launch ComfyUI directly with {GetSourceDisplay(_editor.Draft.PythonSource)} Python.";
		UpdateVenvCard();
		UpdatePipCacheCard();
	}

	private void UpdateVenvCard()
	{
		bool hasVenvDirectory = Directory.Exists(ComfyInstallService.ComfyVenvPath);
		bool hasVenvPython = File.Exists(ComfyInstallService.ComfyVenvPythonExe);
		bool useVenv = string.Equals(_editor.Draft.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal);
		bool pendingCreate = HasDraftBootTask(PendingBootTaskIds.VenvCreate);
		bool pendingRebuild = HasDraftBootTask(PendingBootTaskIds.VenvRebuild);
		bool pendingDelete = !useVenv
			&& (RuntimePythonModePresenter.HasPendingVenvDelete(_editor.Draft)
				|| HasDraftBootTask(PendingBootTaskIds.VenvDelete));

		if (pendingCreate || pendingRebuild)
		{
			VenvStateValueLabel.Text = pendingCreate ? ".venv create scheduled" : ".venv rebuild scheduled";
			VenvPathValueLabel.Text = ComfyInstallService.ComfyVenvPath;
			VenvDetailValueLabel.Text = pendingCreate
				? useVenv
					? "VENV launch is selected, so Nexus must create the managed .venv before the next boot."
					: "Restart the server to create the managed .venv before the next boot."
				: "Restart the server to rebuild the managed .venv before the next boot.";
			CreateVenvButton.IsVisible = false;
			ResetVenvButton.IsVisible = false;
			DeleteVenvButton.IsVisible = false;
			VenvActionGroup.IsVisible = false;
			return;
		}

		if (pendingDelete)
		{
			VenvStateValueLabel.Text = ".venv delete scheduled";
			VenvPathValueLabel.Text = ComfyInstallService.ComfyVenvPath;
			VenvDetailValueLabel.Text = "Restart the server to stop the current runtime and remove .venv before the next boot. DIRECT launch mode is selected.";
			CreateVenvButton.IsVisible = false;
			ResetVenvButton.IsVisible = false;
			DeleteVenvButton.IsVisible = false;
			VenvActionGroup.IsVisible = false;
			return;
		}

		VenvStateValueLabel.Text = hasVenvPython
			? (useVenv ? ".venv ready and selected" : ".venv ready, direct Python selected")
			: hasVenvDirectory
				? ".venv folder exists, but python.exe is missing"
				: ".venv not created";
		VenvPathValueLabel.Text = hasVenvDirectory ? ComfyInstallService.ComfyVenvPath : $"Target: {ComfyInstallService.ComfyVenvPath}";
		VenvDetailValueLabel.Text = hasVenvPython
			? "Reset recreates the environment. Delete removes it and switches launch mode to DIRECT."
			: "Create is recommended if you want Nexus to manage ComfyUI dependencies in an isolated Python environment.";

		CreateVenvButton.IsVisible = !hasVenvDirectory;
		CreateVenvButton.IsEnabled = !hasVenvDirectory;
		ResetVenvButton.IsVisible = hasVenvDirectory;
		ResetVenvButton.IsEnabled = hasVenvDirectory;
		DeleteVenvButton.IsVisible = hasVenvDirectory;
		DeleteVenvButton.IsEnabled = hasVenvDirectory;
		VenvActionGroup.IsVisible = true;
	}

	private void UpdatePipCacheCard()
	{
		string mode = PipCacheService.GetMode(_editor.Draft);
		bool usePipDefault = string.Equals(mode, PipCacheModes.PipDefault, StringComparison.Ordinal);
		bool useNexusDefault = string.Equals(mode, PipCacheModes.NexusDefault, StringComparison.Ordinal);
		bool useCustom = string.Equals(mode, PipCacheModes.Custom, StringComparison.Ordinal);
		string effectivePath = usePipDefault
			? LocalizationManager.Text("settings.pip_cache.pip_default_path")
			: PipCacheService.GetEffectiveCachePath(_editor.Draft);

		PipCachePathValueLabel.Text = effectivePath;
		PipCacheDetailValueLabel.Text = usePipDefault
			? LocalizationManager.Text("settings.pip_cache.pip_default_detail")
			: useNexusDefault
				? LocalizationManager.Text("settings.pip_cache.default_detail")
				: LocalizationManager.Text("settings.pip_cache.custom_detail");
		PipCacheUsePipDefaultButton.IsEnabled = true;
		PipCacheUseDefaultButton.IsEnabled = true;
		PipCacheChangeButton.IsEnabled = true;
		PipCacheOpenButton.IsEnabled = !usePipDefault;
		PipCacheClearButton.IsEnabled = !usePipDefault;
		ApplyRuntimeBackupOptionVisual(PipCacheUsePipDefaultButton, usePipDefault);
		ApplyRuntimeBackupOptionVisual(PipCacheUseDefaultButton, useNexusDefault);
		ApplyRuntimeBackupOptionVisual(PipCacheChangeButton, useCustom);
	}

	private void UpdateToolButtons()
	{
		var draft = _editor.Draft;
		ApplyChipState(UseSystemGitButton, draft.GitSource == DiagnosticNodeHelpers.SystemOption);
		ApplyChipState(UseBuiltInGitButton, draft.GitSource == DiagnosticNodeHelpers.BuiltInOption);
		ApplyChipState(UseCustomGitButton, draft.GitSource == DiagnosticNodeHelpers.CustomOption);
		ApplyChipState(UseSystemPythonButton, draft.PythonSource == DiagnosticNodeHelpers.SystemOption);
		ApplyChipState(UseBuiltInPythonButton, draft.PythonSource == DiagnosticNodeHelpers.BuiltInOption);
		ApplyChipState(UseCustomPythonButton, draft.PythonSource == DiagnosticNodeHelpers.CustomOption);
		GitPathValueLabel.Text = GetToolPathDisplay(draft.GitPath);
		PythonPathValueLabel.Text = GetToolPathDisplay(draft.PythonPath);
	}

	private void StartDeferredProbes()
	{
		if (!IsVisible || InputTransparent)
		{
			return;
		}

		RequestToolVersionProbe();
		RequestGpuProbe();
	}

	private void StartToolVersionProbe()
	{
		if (!IsVisible || InputTransparent)
		{
			return;
		}

		RequestToolVersionProbe();
	}

	private void RequestToolVersionProbe()
	{
		_toolVersionProbeId++;
		if (_toolVersionProbeTask is { IsCompleted: false })
		{
			return;
		}

		_toolVersionProbeTask = RunToolVersionProbeQueueAsync();
	}

	private async Task RunToolVersionProbeQueueAsync()
	{
		try
		{
			while (_completedToolVersionProbeId != _toolVersionProbeId)
			{
				int probeId = _toolVersionProbeId;
				await RefreshToolVersionsAsync(probeId, CancellationToken.None);
				_completedToolVersionProbeId = probeId;
				if (!IsVisible)
				{
					break;
				}
			}
		}
		finally
		{
			_toolVersionProbeTask = null;
			if (IsVisible && _completedToolVersionProbeId != _toolVersionProbeId)
			{
				_toolVersionProbeTask = RunToolVersionProbeQueueAsync();
			}
		}
	}

	private void RequestGpuProbe()
	{
		_gpuProbeId++;
		if (_gpuProbeTask is { IsCompleted: false })
		{
			return;
		}

		_gpuProbeTask = RunGpuProbeQueueAsync();
	}

	private async Task RunGpuProbeQueueAsync()
	{
		try
		{
			while (_completedGpuProbeId != _gpuProbeId)
			{
				int probeId = _gpuProbeId;
				await RefreshGpuOptionsAsync(probeId, CancellationToken.None);
				_completedGpuProbeId = probeId;
				if (!IsVisible)
				{
					break;
				}
			}
		}
		finally
		{
			_gpuProbeTask = null;
			if (IsVisible && _completedGpuProbeId != _gpuProbeId)
			{
				_gpuProbeTask = RunGpuProbeQueueAsync();
			}
		}
	}

	private async Task RefreshGpuOptionsAsync(int probeId, CancellationToken cancellationToken = default)
	{
		SelectedGpuDetailLabel.Text = "Detecting available GPU devices...";
		IReadOnlyList<GpuDeviceInfo> devices;
		try
		{
			devices = await Task.Run(
				async () => await GpuDiscoveryService.DiscoverAsync(cancellationToken),
				cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (probeId != _gpuProbeId || !IsVisible)
		{
			return;
		}

		_gpuDevices.Clear();
		_gpuDevices.AddRange(devices);
		RebuildGpuOptions();
		UpdateGpuSelectionVisuals();
	}

	private void RebuildGpuOptions()
	{
		_isUpdatingGpuPicker = true;
		GpuPicker.Items.Clear();
		foreach (GpuDeviceInfo device in _gpuDevices)
		{
			GpuPicker.Items.Add($"GPU {device.Id} - {device.Name}");
		}

		_isUpdatingGpuPicker = false;
	}

	private void SelectGpuDevice(string gpuId)
	{
		_editor.Draft.GpuId = gpuId;
		UpdateGpuSelectionVisuals();
		UpdateStateChrome();
	}

	private void UpdateGpuSelectionVisuals()
	{
		string selectedId = string.IsNullOrWhiteSpace(_editor.Draft.GpuId) ? "0" : _editor.Draft.GpuId;
		GpuDeviceInfo? selected = _gpuDevices.FirstOrDefault(device => device.Id == selectedId);
		SelectedGpuLabel.Text = selected == null
			? $"GPU {selectedId}"
			: $"GPU {selected.Id} - {selected.Name}";
		SelectedGpuDetailLabel.Text = selected?.MemoryTotalMb ?? "Device name will appear after detection.";

		_isUpdatingGpuPicker = true;
		int selectedIndex = selected == null ? -1 : _gpuDevices.IndexOf(selected);
		if (selectedIndex >= 0)
		{
			GpuPicker.SelectedIndex = selectedIndex;
		}
		else if (!GpuPicker.Items.Contains($"GPU {selectedId}"))
		{
			GpuPicker.Items.Add($"GPU {selectedId}");
			GpuPicker.SelectedIndex = GpuPicker.Items.Count - 1;
		}
		_isUpdatingGpuPicker = false;
	}

	private static void ApplyChipState(Button button, bool isActive)
	{
		button.BackgroundColor = isActive ? SettingsActivePillColor : NexusColors.SurfaceSubtle;
		button.TextColor = isActive ? NexusColors.TextStrong : SettingsMutedTextColor;
	}

	private static string GetInstallModeDisplay(SetupSettings settings)
		=> string.Equals(settings.InstallMode, SetupInstallModes.ExistingComfyPath, StringComparison.Ordinal)
			? "Custom / existing ComfyUI folder"
			: "Local Nexus runtime";

	private static string GetCustomNodesPath()
		=> System.IO.Path.Combine(ComfyInstallService.ComfyPath, "custom_nodes");

	private void RebuildManagedExtensionOptions()
	{
		string customNodesPath = GetCustomNodesPath();
		var previousSelection = _managedExtensionOptions
			.Where(option => option.IsSelected)
			.Select(option => option.Folder)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		_managedExtensionOptions.Clear();
		AddManagedExtensionOption("ComfyUI-Manager", "ComfyUI-Manager", customNodesPath, previousSelection);
		AddManagedExtensionOption("ComfyUI-HUD", "ComfyUI-HUD", customNodesPath, previousSelection);
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
		bool isInstalled = IsManagedExtensionInstalled(path);
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

	private static string GetEffectiveComfyPath(SetupSettings settings)
		=> string.IsNullOrWhiteSpace(settings.ComfyPath) ? ComfyInstallService.DefaultComfyPath : settings.ComfyPath;

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
				StartSettingsOperationBlockerLogoBounce();
				await SettingsOperationBlocker.FadeToAsync(1, OperationBlockerShowAnimationLength, Easing.CubicOut);
				return;
			}

			if (!SettingsOperationBlocker.IsVisible)
			{
				return;
			}

			await SettingsOperationBlocker.FadeToAsync(0, OperationBlockerHideAnimationLength, Easing.CubicIn);
			StopSettingsOperationBlockerLogoBounce();
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

	private void StartSettingsOperationBlockerLogoBounce()
	{
		this.AbortAnimation(OperationBlockerLogoBounceAnimationName);
		SettingsOperationBlockerLogo.TranslationY = 0;
		SettingsOperationBlockerLogoGroundGlow.Opacity = 0.28;
		SettingsOperationBlockerLogoGroundGlow.ScaleX = 1;
		var bounce = new Animation();
		bounce.Add(0, 0.45, new Animation(
			value => SettingsOperationBlockerLogo.TranslationY = value,
			0,
			-OperationBlockerLogoBounceHeight,
			Easing.CubicOut));
		bounce.Add(0.45, 1, new Animation(
			value => SettingsOperationBlockerLogo.TranslationY = value,
			-OperationBlockerLogoBounceHeight,
			0,
			Easing.BounceOut));
		bounce.Add(0, 0.45, new Animation(
			value => SettingsOperationBlockerLogoGroundGlow.ScaleX = value,
			1,
			0.72,
			Easing.CubicOut));
		bounce.Add(0.45, 1, new Animation(
			value => SettingsOperationBlockerLogoGroundGlow.ScaleX = value,
			0.72,
			1,
			Easing.CubicOut));
		bounce.Add(0, 0.45, new Animation(
			value => SettingsOperationBlockerLogoGroundGlow.Opacity = value,
			0.28,
			0.16,
			Easing.CubicOut));
		bounce.Add(0.45, 1, new Animation(
			value => SettingsOperationBlockerLogoGroundGlow.Opacity = value,
			0.16,
			0.28,
			Easing.CubicOut));
		bounce.Commit(
			this,
			OperationBlockerLogoBounceAnimationName,
			16,
			OperationBlockerLogoBounceLength,
			Easing.Linear,
			repeat: () => SettingsOperationBlocker.IsVisible);
	}

	private void StopSettingsOperationBlockerLogoBounce()
	{
		this.AbortAnimation(OperationBlockerLogoBounceAnimationName);
		SettingsOperationBlockerLogo.TranslationY = 0;
		SettingsOperationBlockerLogoGroundGlow.Opacity = 0.28;
		SettingsOperationBlockerLogoGroundGlow.ScaleX = 1;
	}

	private void OnSettingsOperationBlockerCancelClicked(object? sender, EventArgs e)
		=> _modelDuplicateScanCancellation?.Cancel();

	private void SetComfyActionBusy(bool isBusy)
	{
		_isComfyActionBusy = isBusy;
		bool isLocal = string.Equals(_editor.Draft.InstallMode, SetupInstallModes.LocalRuntime, StringComparison.Ordinal);
		UseLocalRuntimeButton.IsEnabled = !isBusy;
		UseCustomComfyButton.IsEnabled = !isBusy;
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
			RestartServerRequested?.Invoke(this, new SettingsRestartRequestedEventArgs(repairRuntimeBeforeBoot));
		}
	}

	private bool ShouldRepairRuntimeBeforeSettingsRestart()
	{
		if (SetupSettingsService.Instance.HasRunnableBootTasks())
		{
			return false;
		}

		if (RuntimePythonModePresenter.HasPendingVenvDelete(SetupSettingsService.Instance.Settings)
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

	private void OnUseVenvClicked(object? sender, EventArgs e)
	{
		_editor.Draft.ServerPythonMode = PythonExecutionModes.Venv;
		_editor.Draft.PendingVenvDelete = false;
		RemoveDraftBootTask(PendingBootTaskIds.VenvDelete);
		if (!File.Exists(ComfyInstallService.ComfyVenvPythonExe)
			&& !HasDraftBootTask(PendingBootTaskIds.VenvCreate)
			&& !HasDraftBootTask(PendingBootTaskIds.VenvRebuild))
		{
			AddDraftBootTask(PendingBootTaskIds.VenvCreate, PendingBootTaskOrigins.VenvModeSelection);
		}

		UpdatePythonModeButtons();
		UpdateStateChrome();
	}

	private void OnUseLocalRuntimeClicked(object? sender, EventArgs e)
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(_editor.Draft.ComfyPath))
		{
			_lastCustomComfyPath = _editor.Draft.ComfyPath;
		}

		_editor.Draft.InstallMode = SetupInstallModes.LocalRuntime;
		_editor.Draft.ComfyPath = string.Empty;
		UpdateComfyModeButtons();
		UpdateStateChrome();
	}

	private async void OnSelectComfyPathClicked(object? sender, EventArgs e)
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!TryBeginOperation("select-comfy-path"))
		{
			return;
		}

		SetComfyActionBusy(true);
		try
		{
			if (sender == UseCustomComfyButton && !string.IsNullOrWhiteSpace(_lastCustomComfyPath))
			{
				_editor.Draft.InstallMode = SetupInstallModes.ExistingComfyPath;
				_editor.Draft.ComfyPath = _lastCustomComfyPath;
				UpdateComfyModeButtons();
				UpdateStateChrome();
				return;
			}

			var result = await PlatformManager.Current.FilePicker.PickFolderAsync("Select ComfyUI Folder");
			if (!result.IsSupported || !result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
			{
				if (!string.IsNullOrWhiteSpace(result.Message))
				{
					await ShowValidationAlertAsync(result.Message);
				}

				return;
			}

			_editor.Draft.InstallMode = SetupInstallModes.ExistingComfyPath;
			_editor.Draft.ComfyPath = result.Value;
			_lastCustomComfyPath = result.Value;
			UpdateComfyModeButtons();
			UpdateStateChrome();
		}
		finally
		{
			SetComfyActionBusy(false);
			EndOperation("select-comfy-path");
		}
	}

	private async void OnOpenComfyFolderClicked(object? sender, EventArgs e)
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!TryBeginOperation("open-comfy-folder"))
		{
			return;
		}

		SetComfyActionBusy(true);
		try
		{
			string path = GetEffectiveComfyPath(_editor.Draft);
			var result = await PlatformManager.Current.Shell.OpenPathAsync(path);
			if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
			{
				await ShowValidationAlertAsync(result.Message);
			}
		}
		finally
		{
			SetComfyActionBusy(false);
			EndOperation("open-comfy-folder");
		}
	}

	private async void OnCheckComfyUpdatesClicked(object? sender, EventArgs e)
	{
		await CheckComfyUpdatesAsync();
	}

	private async Task CheckComfyUpdatesAsync()
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!TryBeginOperation("check-comfy-updates"))
		{
			return;
		}

		SetComfyActionBusy(true);
		try
		{
			_comfyUpdatesAvailable = 0;
			ComfyApplyUpdateButton.IsVisible = false;
			ComfyApplyUpdateButton.IsEnabled = false;
			ComfyUpdateValueLabel.Text = "Checking ComfyUI repository status...";
			await Task.Yield();

			string comfyPath = GetEffectiveComfyPath(_editor.Draft);
			string gitPath = ResolveToolProbePath(_editor.Draft.GitSource, _editor.Draft.GitPath, "git");
			var checkResult = await Task.Run(async () =>
			{
				if (!Directory.Exists(comfyPath))
				{
					return (Message: "ComfyUI folder does not exist. Select a valid path before checking updates.", UpdateCount: 0);
				}

				if (!Directory.Exists(System.IO.Path.Combine(comfyPath, ".git")))
				{
					return (Message: "This ComfyUI folder is not a git checkout. Update check is unavailable.", UpdateCount: 0);
				}

				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
				(int ExitCode, string Output, string Error) fetchResult;
				try
				{
					fetchResult = await ProcessRunner.RunAsync(gitPath, "fetch --quiet", comfyPath, null, timeout.Token);
				}
				catch (OperationCanceledException)
				{
					return (Message: "Update check timed out while fetching repository status.", UpdateCount: 0);
				}

				if (fetchResult.ExitCode != 0)
				{
					return (Message: $"Update check failed: {GetProcessError(fetchResult)}", UpdateCount: 0);
				}

				(int ExitCode, string Output, string Error) upstreamResult;
				try
				{
					upstreamResult = await ProcessRunner.RunAsync(gitPath, "rev-list --count HEAD..@{u}", comfyPath, null, timeout.Token);
				}
				catch (OperationCanceledException)
				{
					return (Message: "Update check timed out while reading upstream status.", UpdateCount: 0);
				}

				if (upstreamResult.ExitCode != 0)
				{
					return (Message: "No upstream tracking branch detected. Advanced tag/revision update can be configured later.", UpdateCount: 0);
				}

				string rawCount = upstreamResult.Output
					.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
					.FirstOrDefault() ?? "0";
				if (!int.TryParse(rawCount.Trim(), out int updateCount))
				{
					return (Message: "Repository checked, but the update count could not be parsed.", UpdateCount: 0);
				}

				string message = updateCount == 0
					? "ComfyUI repository is up to date."
					: $"{updateCount} upstream commit(s) available. Review the warning before updating.";
				return (Message: message, UpdateCount: updateCount);
			});

			ComfyUpdateValueLabel.Text = checkResult.Message;
			int updateCount = checkResult.UpdateCount;
			_comfyUpdatesAvailable = updateCount;
			ComfyApplyUpdateButton.IsVisible = updateCount > 0;
			ComfyApplyUpdateButton.IsEnabled = updateCount > 0;
		}
		finally
		{
			SetComfyActionBusy(false);
			EndOperation("check-comfy-updates");
		}
	}

	private async void OnApplyComfyUpdateClicked(object? sender, EventArgs e)
	{
		await ApplyComfyUpdateAsync();
	}

	private async void OnExtensionsScanClicked(object? sender, EventArgs e)
	{
		await RefreshExtensionsStatusAsync(++_extensionsProbeId, CancellationToken.None, userRequested: true);
	}

	private async void OnExtensionsSyncUpdateClicked(object? sender, EventArgs e)
	{
		await QueueManagedExtensionsAsync(reinstall: false);
	}

	private async void OnExtensionsReinstallClicked(object? sender, EventArgs e)
	{
		await QueueManagedExtensionsAsync(reinstall: true);
	}

	private async void OnExtensionsOpenFolderClicked(object? sender, EventArgs e)
	{
		if (!TryBeginOperation("open-extensions-folder"))
		{
			return;
		}

		ExtensionsOpenFolderButton.IsEnabled = false;
		try
		{
			Directory.CreateDirectory(GetCustomNodesPath());
			var result = await PlatformManager.Current.Shell.OpenPathAsync(GetCustomNodesPath());
			if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
			{
				await ShowValidationAlertAsync(result.Message);
			}
		}
		finally
		{
			ExtensionsOpenFolderButton.IsEnabled = true;
			EndOperation("open-extensions-folder");
		}
	}

	private async void OnExtensionsRestoreHudSamplesClicked(object? sender, EventArgs e)
	{
		const string operationId = "restore-hud-samples";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		SetHudSamplesBusy(true);
		try
		{
			string sourcePath = System.IO.Path.Combine(GetCustomNodesPath(), "ComfyUI-HUD", "hud_sample");
			string targetPath = System.IO.Path.Combine(ComfyInstallService.ComfyPath, "user", "default", "workflows", "hud_sample");
			if (!Directory.Exists(sourcePath) ||
				!Directory.EnumerateFiles(sourcePath, "*.json", SearchOption.AllDirectories).Any())
			{
				return;
			}

			bool hasExistingSamples = Directory.Exists(targetPath) &&
				Directory.EnumerateFiles(targetPath, "*.json", SearchOption.AllDirectories).Any();
			HudSampleRestoreMode? restoreMode = hasExistingSamples
				? await ChooseHudSampleRestoreModeAsync(sourcePath, targetPath)
				: await ConfirmHudSampleRestoreAsync(sourcePath, targetPath);
			if (restoreMode == null)
			{
				return;
			}

			SetHudSampleRestoreStatus(
				LocalizationManager.Text("views.overlays.settings_overlay_view.restoring_hud_samples"),
				SettingsInfoTextColor);
			SetupStepResult result = await ComfyInstallService.Instance.RestoreHudSamplesAsync(
				overwriteExisting: restoreMode == HudSampleRestoreMode.Replace,
				CancellationToken.None);
			SetHudSampleRestoreStatus(
				result.IsSuccess
					? LocalizationManager.Text("views.overlays.settings_overlay_view.hud_samples_restored")
					: LocalizationManager.Format(
						"views.overlays.settings_overlay_view.hud_samples_restore_failed",
						result.Message),
				result.IsSuccess ? SettingsSuccessTextColor : SettingsFailureTextColor);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS] HUD sample restore failed");
			SetHudSampleRestoreStatus(
				LocalizationManager.Format(
					"views.overlays.settings_overlay_view.hud_samples_restore_failed",
					ex.Message),
				SettingsFailureTextColor);
		}
		finally
		{
			SetHudSamplesBusy(false);
			EndOperation(operationId);
		}
	}

	private void RebuildModelLibrariesList()
	{
		ModelLibrariesList.Children.Clear();
		var draft = _editor.Draft;
		if (draft.ModelLibraryRoots.Count == 0)
		{
			ModelLibrariesList.Children.Add(CreateModelLibraryRow(
				string.Empty,
				LocalizationManager.Format("settings.model_libraries.library", 1),
				index: -1));
			return;
		}

		for (int index = 0; index < draft.ModelLibraryRoots.Count; index++)
		{
			string path = ExtraModelPathsService.NormalizeFileSystemPath(draft.ModelLibraryRoots[index]);
			ModelLibrariesList.Children.Add(CreateModelLibraryRow(
				path,
				LocalizationManager.Format("settings.model_libraries.library", index + 1),
				index));
		}
	}

	private View CreateModelLibraryRow(string path, string title, int index)
	{
		var titleLabel = new Label
		{
			Text = title,
			Style = (Style)Resources["SettingsKeyStyle"],
			VerticalOptions = LayoutOptions.Center
		};
		var pathLabel = new Label
		{
			Text = path.Length == 0
				? LocalizationManager.Text("settings.model_libraries.not_connected")
				: Directory.Exists(path)
					? path
					: LocalizationManager.Format("settings.model_libraries.unavailable", path),
			Style = (Style)Resources["SettingsValueStyle"],
			LineBreakMode = LineBreakMode.TailTruncation,
			VerticalOptions = LayoutOptions.Center
		};
		if (path.Length > 0 && !Directory.Exists(path))
		{
			pathLabel.TextColor = SettingsWarningTextColor;
		}
		var textStack = new VerticalStackLayout
		{
			Spacing = 3,
			VerticalOptions = LayoutOptions.Center,
			Children = { titleLabel, pathLabel }
		};

		var actionLayout = new HorizontalStackLayout
		{
			Spacing = 6,
			HorizontalOptions = LayoutOptions.End,
			VerticalOptions = LayoutOptions.Center
		};
		if (path.Length > 0)
		{
			actionLayout.Children.Add(CreateModelLibraryButton(
				LocalizationManager.Text("settings.model_libraries.open"),
				OnOpenModelLibraryClicked,
				new ModelLibraryActionContext(index, path),
				isEnabled: Directory.Exists(path)));
		}

		actionLayout.Children.Add(CreateModelLibraryButton(
			path.Length == 0
				? LocalizationManager.Text("settings.model_libraries.connect")
				: LocalizationManager.Text("settings.model_libraries.replace"),
			OnReplaceModelLibraryClicked,
			new ModelLibraryActionContext(index, path)));

		if (path.Length > 0)
		{
			actionLayout.Children.Add(CreateModelLibraryButton(
				LocalizationManager.Text("settings.model_libraries.remove"),
				OnRemoveModelLibraryClicked,
				new ModelLibraryActionContext(index, path)));
		}

		var rowGrid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto)
			},
			ColumnSpacing = 10
		};
		rowGrid.Children.Add(textStack);
		Grid.SetColumn(actionLayout, 1);
		rowGrid.Children.Add(actionLayout);

		return new Border
		{
			Style = (Style)Resources["SettingsInsetBoxStyle"],
			StrokeThickness = 0,
			Padding = new Thickness(12, 9),
			Content = rowGrid
		};
	}

	private Button CreateModelLibraryButton(
		string text,
		EventHandler clicked,
		ModelLibraryActionContext context,
		bool isEnabled = true)
	{
		var button = new Button
		{
			Text = text,
			Style = (Style)Resources["SettingsTextButtonStyle"],
			CommandParameter = context,
			IsEnabled = isEnabled
		};
		button.Clicked += clicked;
		return button;
	}

	private async void OnAddModelLibraryClicked(object? sender, EventArgs e)
	{
		await SelectModelLibraryAsync(_editor.Draft.ModelLibraryRoots.Count);
	}

	private async void OnSyncModelLibraryStructureClicked(object? sender, EventArgs e)
	{
		const string operationId = "sync-model-library-structure";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		await SetSettingsOperationBlockerVisibleAsync(
			true,
			LocalizationManager.Text("settings.model_libraries.syncing_title"),
			LocalizationManager.Text("settings.model_libraries.syncing_detail"));
		try
		{
			var settings = SetupSettingsService.Instance.Settings;
			string comfyPath = GetEffectiveComfyPath(settings);
			var applyResult = await Task.Run(() =>
			{
				ExtraModelPathsResult result = ExtraModelPathsService.TryApply(
					settings,
					comfyPath,
					out ExtraModelPathsTransaction? transaction);
				return (Result: result, Transaction: transaction);
			});

			if (!applyResult.Result.IsSuccess)
			{
				applyResult.Transaction?.Rollback();
				SetModelLibrariesStatus(applyResult.Result.Message, SettingsFailureTextColor);
				return;
			}

			applyResult.Transaction?.Commit();
			_modelLibrariesRestartRequired = true;
			SetModelLibrariesStatus(
				LocalizationManager.Text("settings.model_libraries.sync_complete"),
				SettingsSuccessTextColor);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MODEL PATHS] Structure synchronization failed");
			SetModelLibrariesStatus(
				LocalizationManager.Format("settings.model_libraries.sync_failed", ex.Message),
				SettingsFailureTextColor);
		}
		finally
		{
			await SetSettingsOperationBlockerVisibleAsync(false);
			EndOperation(operationId);
			UpdateStateChrome();
		}
	}

	private void SetModelLibrariesStatus(string message, Color color)
	{
		ModelLibrariesStatusLabel.Text = message;
		ModelLibrariesStatusLabel.TextColor = color;
		ModelLibrariesStatusLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
	}

	private async void OnReplaceModelLibraryClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: ModelLibraryActionContext context })
		{
			await SelectModelLibraryAsync(context.Index);
		}
	}

	private async void OnRemoveModelLibraryClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: ModelLibraryActionContext context })
		{
			return;
		}

		if (context.Index >= 0 && context.Index < _editor.Draft.ModelLibraryRoots.Count)
		{
			_editor.Draft.ModelLibraryRoots.RemoveAt(context.Index);
		}

		RebuildModelLibrariesList();
		RebuildRuntimeBackupExternalLibraries();
		UpdateStateChrome();
		await Task.CompletedTask;
	}

	private async void OnOpenModelLibraryClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: ModelLibraryActionContext context }
			|| string.IsNullOrWhiteSpace(context.Path))
		{
			return;
		}

		var result = await PlatformManager.Current.Shell.OpenPathAsync(context.Path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			await ShowValidationAlertAsync(result.Message);
		}
	}

	private async Task SelectModelLibraryAsync(int index)
	{
		var result = await PlatformManager.Current.FilePicker.PickFolderAsync(
			LocalizationManager.Text("settings.model_libraries.folder_picker_title"));
		if (!result.IsSupported || !result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
		{
			return;
		}

		string normalized = ExtraModelPathsService.NormalizeFileSystemPath(result.Value);
		string currentPath = index >= 0 && index < _editor.Draft.ModelLibraryRoots.Count
			? ExtraModelPathsService.NormalizeFileSystemPath(_editor.Draft.ModelLibraryRoots[index])
			: string.Empty;
		if (string.Equals(currentPath, normalized, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (ExtraModelPathsService.ContainsRoot(_editor.Draft, normalized))
		{
			await ShowValidationAlertAsync(LocalizationManager.Text("settings.model_libraries.duplicate"));
			return;
		}

		if (index >= 0 && index < _editor.Draft.ModelLibraryRoots.Count)
		{
			_editor.Draft.ModelLibraryRoots[index] = normalized;
		}
		else
		{
			_editor.Draft.ModelLibraryRoots.Add(normalized);
		}

		RebuildModelLibrariesList();
		RebuildRuntimeBackupExternalLibraries();
		UpdateStateChrome();
	}

	private async void OnScanModelDuplicatesClicked(object? sender, EventArgs e)
	{
		const string operationId = "scan-model-duplicates";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		int scanGeneration = ++_modelDuplicateScanGeneration;
		_modelDuplicateScanCancellation?.Cancel();
		_modelDuplicateScanCancellation?.Dispose();
		_modelDuplicateScanCancellation = new CancellationTokenSource();
		CancellationToken cancellationToken = _modelDuplicateScanCancellation.Token;
		await SetSettingsOperationBlockerVisibleAsync(
			true,
			LocalizationManager.Text("settings.model_duplicates.scanning_title"),
			LocalizationManager.Text("settings.model_duplicates.scanning_detail"),
			allowCancel: true);
		_modelDuplicateScanResult = null;
		_modelDuplicateGroupIndex = 0;
		UpdateModelDuplicateScanUi();
		UpdateStateChrome();
		try
		{
			string comfyPath = GetEffectiveComfyPath(_editor.Draft);
			var roots = _editor.Draft.ModelLibraryRoots.ToList();
			var progress = new Progress<ModelDuplicateScanProgress>(scanProgress =>
			{
				if (scanGeneration != _modelDuplicateScanGeneration || cancellationToken.IsCancellationRequested)
				{
					return;
				}

				SettingsOperationBlockerDetailLabel.Text = GetModelDuplicateScanProgressText(scanProgress);
				UpdateSettingsOperationBlockerProgress(scanProgress.Progress);
			});
			ModelDuplicateScanResult result = await Task.Run(
				() => ModelDuplicateScanService.ScanAsync(comfyPath, roots, cancellationToken, progress),
				cancellationToken);
			if (scanGeneration != _modelDuplicateScanGeneration || cancellationToken.IsCancellationRequested)
			{
				return;
			}

			_modelDuplicateScanResult = result;
			_modelDuplicateGroupIndex = 0;
			UpdateModelDuplicateScanUi();
		}
		catch (OperationCanceledException)
		{
			if (scanGeneration == _modelDuplicateScanGeneration)
			{
				ModelDuplicateStatusLabel.Text = LocalizationManager.Text("settings.model_duplicates.cancelled");
				ModelDuplicateStatusLabel.TextColor = SettingsMutedTextColor;
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MODEL DUPLICATES] Scan failed");
			if (scanGeneration == _modelDuplicateScanGeneration)
			{
				ModelDuplicateStatusLabel.Text = LocalizationManager.Format(
					"settings.model_duplicates.scan_failed",
					ex.Message);
				ModelDuplicateStatusLabel.TextColor = SettingsFailureTextColor;
			}
		}
		finally
		{
			await SetSettingsOperationBlockerVisibleAsync(false);
			if (ReferenceEquals(_modelDuplicateScanCancellation, null) == false
				&& scanGeneration == _modelDuplicateScanGeneration)
			{
				_modelDuplicateScanCancellation.Dispose();
				_modelDuplicateScanCancellation = null;
			}

			EndOperation(operationId);
			UpdateStateChrome();
		}
	}

	private static string GetModelDuplicateScanProgressText(ModelDuplicateScanProgress progress)
	{
		return progress.Stage switch
		{
			ModelDuplicateScanStage.DiscoveringFiles => LocalizationManager.Format(
				"settings.model_duplicates.progress_discovering",
				progress.ProcessedFiles,
				RuntimeBackupService.FormatBytes(progress.ProcessedBytes)),
			ModelDuplicateScanStage.PreparingHashes => LocalizationManager.Format(
				"settings.model_duplicates.progress_preparing",
				progress.TotalFiles,
				RuntimeBackupService.FormatBytes(progress.TotalBytes)),
			ModelDuplicateScanStage.HashingFiles => LocalizationManager.Format(
				"settings.model_duplicates.progress_hashing",
				progress.ProcessedFiles,
				progress.TotalFiles,
				RuntimeBackupService.FormatBytes(progress.ProcessedBytes),
				RuntimeBackupService.FormatBytes(progress.TotalBytes)),
			ModelDuplicateScanStage.WritingReport => LocalizationManager.Text("settings.model_duplicates.progress_report"),
			_ => LocalizationManager.Text("settings.model_duplicates.scanning_detail")
		};
	}

	private async void OnOpenModelDuplicateReportClicked(object? sender, EventArgs e)
	{
		if (_modelDuplicateScanResult is not { ReportPath.Length: > 0 } result || !File.Exists(result.ReportPath))
		{
			return;
		}

		var openResult = await PlatformManager.Current.Shell.OpenPathAsync(result.ReportPath);
		if (!openResult.IsSuccess && !string.IsNullOrWhiteSpace(openResult.Message))
		{
			SetModelLibrariesStatus(openResult.Message, SettingsFailureTextColor);
		}
	}

	private async void OnOpenDuplicateLocation1Clicked(object? sender, EventArgs e)
		=> await OpenCurrentDuplicatePathAsync(0);

	private async void OnOpenDuplicateLocation2Clicked(object? sender, EventArgs e)
		=> await OpenCurrentDuplicatePathAsync(1);

	private void OnNextModelDuplicateClicked(object? sender, EventArgs e)
	{
		if (_modelDuplicateScanResult is not { Groups.Count: > 0 } result)
		{
			return;
		}

		_modelDuplicateGroupIndex = (_modelDuplicateGroupIndex + 1) % result.Groups.Count;
		UpdateModelDuplicateScanUi();
	}

	private async Task OpenCurrentDuplicatePathAsync(int fileIndex)
	{
		ModelDuplicateGroup? group = GetCurrentDuplicateGroup();
		if (group is null || fileIndex < 0 || fileIndex >= group.Files.Count)
		{
			return;
		}

		ModelDuplicateFile file = group.Files[fileIndex];
		string path = System.IO.Path.GetDirectoryName(file.FullPath) ?? file.SourceRoot;
		var result = await PlatformManager.Current.Shell.OpenPathAsync(path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			SetModelLibrariesStatus(result.Message, SettingsFailureTextColor);
		}
	}

	private void UpdateModelDuplicateScanUi()
	{
		if (_modelDuplicateScanResult is null)
		{
			ModelDuplicateStatusLabel.Text = LocalizationManager.Text("settings.model_duplicates.ready");
			ModelDuplicateStatusLabel.TextColor = SettingsMutedTextColor;
			UpdateModelDuplicateScanButtons(_activeOperations.Count > 0 || _isComfyActionBusy);
			return;
		}

		if (!_modelDuplicateScanResult.HasDuplicates)
		{
			ModelDuplicateStatusLabel.Text = LocalizationManager.Format(
				"settings.model_duplicates.none_found",
				_modelDuplicateScanResult.ScannedFileCount,
				RuntimeBackupService.FormatBytes(_modelDuplicateScanResult.ScannedBytes));
			ModelDuplicateStatusLabel.TextColor = SettingsSuccessTextColor;
			UpdateModelDuplicateScanButtons(_activeOperations.Count > 0 || _isComfyActionBusy);
			return;
		}

		ModelDuplicateGroup group = GetCurrentDuplicateGroup()!;
		string foundKey = group.Files.Count > 2
			? "settings.model_duplicates.found_many"
			: "settings.model_duplicates.found";
		ModelDuplicateStatusLabel.Text = LocalizationManager.Format(
			foundKey,
			_modelDuplicateGroupIndex + 1,
			_modelDuplicateScanResult.Groups.Count,
			group.Files[0].FileName,
			RuntimeBackupService.FormatBytes(group.Length),
			group.Files.Count,
			Math.Max(0, group.Files.Count - 2));
		ModelDuplicateStatusLabel.TextColor = SettingsWarningTextColor;
		UpdateModelDuplicateScanButtons(_activeOperations.Count > 0 || _isComfyActionBusy);
	}

	private void UpdateModelDuplicateScanButtons(bool hasActiveOperations)
	{
		bool hasResult = _modelDuplicateScanResult is not null;
		bool hasGroups = _modelDuplicateScanResult is { Groups.Count: > 0 };
		ModelDuplicateGroup? group = GetCurrentDuplicateGroup();
		OpenModelDuplicateReportButton.IsEnabled = !hasActiveOperations
			&& hasResult
			&& File.Exists(_modelDuplicateScanResult!.ReportPath);
		OpenDuplicateLocation1Button.IsEnabled = !hasActiveOperations && group?.Files.Count >= 1;
		OpenDuplicateLocation2Button.IsEnabled = !hasActiveOperations && group?.Files.Count >= 2;
		NextModelDuplicateButton.IsEnabled = !hasActiveOperations && hasGroups;
	}

	private ModelDuplicateGroup? GetCurrentDuplicateGroup()
	{
		if (_modelDuplicateScanResult is not { Groups.Count: > 0 } result)
		{
			return null;
		}

		_modelDuplicateGroupIndex = Math.Clamp(_modelDuplicateGroupIndex, 0, result.Groups.Count - 1);
		return result.Groups[_modelDuplicateGroupIndex];
	}

	private const string InternalSourceKind = "internal";
	private const string ExternalSourceKind = "external";

	private sealed record ModelLibraryActionContext(int Index, string Path);

	private void SetHudSampleRestoreStatus(string message, Color color)
	{
		HudSampleRestoreStatusLabel.Text = message;
		HudSampleRestoreStatusLabel.TextColor = color;
		HudSampleRestoreStatusLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
	}

	private static async Task<HudSampleRestoreMode?> ConfirmHudSampleRestoreAsync(string sourcePath, string targetPath)
	{
		bool confirmed = await NexusDialogService.ConfirmAsync(
			LocalizationManager.Text("views.overlays.settings_overlay_view.restore_hud_samples_title"),
			LocalizationManager.Format(
				"views.overlays.settings_overlay_view.restore_hud_samples_message",
				sourcePath,
				targetPath),
			LocalizationManager.Text("views.overlays.settings_overlay_view.restore_samples"),
			LocalizationManager.Text("common.cancel"));
		return confirmed ? HudSampleRestoreMode.MissingOnly : null;
	}

	private static async Task<HudSampleRestoreMode?> ChooseHudSampleRestoreModeAsync(string sourcePath, string targetPath)
	{
		HudSampleRestoreMode? selectedMode = null;
		await NexusDialogService.ChoiceAsync(
			LocalizationManager.Text("views.overlays.settings_overlay_view.restore_hud_samples_title"),
			LocalizationManager.Format(
				"views.overlays.settings_overlay_view.restore_hud_samples_conflict_message",
				sourcePath,
				targetPath),
			[
				new NexusDialogChoice(
					LocalizationManager.Text("views.overlays.settings_overlay_view.restore_missing"),
					() =>
					{
						selectedMode = HudSampleRestoreMode.MissingOnly;
						return Task.FromResult(NexusDialogActionResult.Close);
					}),
				new NexusDialogChoice(
					LocalizationManager.Text("views.overlays.settings_overlay_view.replace_samples"),
					() =>
					{
						selectedMode = HudSampleRestoreMode.Replace;
						return Task.FromResult(NexusDialogActionResult.Close);
					},
					IsDanger: true)
			],
			LocalizationManager.Text("common.cancel"));
		return selectedMode;
	}

	private async Task RefreshExtensionsStatusAsync(
		int probeId,
		CancellationToken cancellationToken = default,
		bool userRequested = false)
	{
		if (userRequested && !TryBeginOperation("scan-extensions"))
		{
			return;
		}

		if (userRequested)
		{
			SetExtensionsBusy(
				true,
				LocalizationManager.Text("views.overlays.settings_overlay_view.scanning_extensions_title"),
				LocalizationManager.Text("views.overlays.settings_overlay_view.scanning_extensions_detail"));
		}

		try
		{
			ExtensionsStatusValueLabel.Text = LocalizationManager.Text("settings.extensions.scanning_status");
			var node = new ManagerExtensionDiagnosticNode();
			HealthState health = await node.CheckHealthAsync(cancellationToken);
			var revisions = await ScanManagedExtensionGitStatusAsync(cancellationToken);
			if (probeId != _extensionsProbeId)
			{
				return;
			}

			RebuildManagedExtensionOptions();
			foreach (ManagedExtensionOption option in _managedExtensionOptions)
			{
				if (revisions.TryGetValue(option.Folder, out string? revision))
				{
					option.Revision = revision ?? option.Revision;
				}
			}

			RebuildManagedExtensionSelectionList();
			ExtensionsStatusValueLabel.Text = health switch
			{
				HealthState.Healthy => LocalizationManager.Format("settings.extensions.scan_ready", node.EnvironmentPath),
				HealthState.NeedsRecovery => LocalizationManager.Format("settings.extensions.scan_missing", node.EnvironmentPath),
				_ => LocalizationManager.Format("settings.extensions.scan_failed_status", node.EnvironmentPath)
			};
			ExtensionsStatusValueLabel.TextColor = health switch
			{
				HealthState.Healthy => SettingsSuccessTextColor,
				HealthState.NeedsRecovery => SettingsRequiredTextColor,
				_ => SettingsFailureTextColor
			};
			ExtensionsSyncUpdateButton.IsEnabled = true;
			ExtensionsReinstallButton.IsEnabled = true;
		}
		catch (OperationCanceledException)
		{
			NexusLog.Trace("[SETTINGS:UI] Managed extension scan canceled.");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS:UI] Managed extension scan failed");
			ExtensionsStatusValueLabel.Text = LocalizationManager.Format(
				"views.overlays.settings_overlay_view.extension_scan_failed",
				ex.Message);
			ExtensionsStatusValueLabel.TextColor = SettingsFailureTextColor;
		}
		finally
		{
			if (userRequested)
			{
				SetExtensionsBusy(false);
				EndOperation("scan-extensions");
			}
		}
	}

	private static async Task<Dictionary<string, string>> ScanManagedExtensionGitStatusAsync(CancellationToken cancellationToken)
	{
		string customNodesPath = GetCustomNodesPath();
		SetupSettings settings = SetupSettingsService.Instance.Settings;
		string gitExecutable = ResolveToolProbePath(settings.GitSource, settings.GitPath, "git");
		var folders = new List<string> { "ComfyUI-Manager", "ComfyUI-HUD" };
		folders.AddRange(SetupSettingsService.Instance.Settings.EssentialNodes.Select(node => node.Folder));
		return await Task.Run(() =>
		{
			var revisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				string path = System.IO.Path.Combine(customNodesPath, folder);
				bool isInstalled = IsManagedExtensionInstalled(path);
				revisions[folder] = GetManagedExtensionScanStatus(path, isInstalled, gitExecutable);
			}

			return revisions;
		}, cancellationToken);
	}

	private static string GetManagedExtensionScanStatus(string path, bool isInstalled, string gitExecutable)
	{
		if (!isInstalled)
		{
			return "not installed";
		}

		if (!Directory.Exists(System.IO.Path.Combine(path, ".git")))
		{
			return "local package";
		}

		string status = RunGitMetadata(path, "status --porcelain=v2 --branch", gitExecutable);
		if (string.IsNullOrWhiteSpace(status))
		{
			return "git status unavailable";
		}

		string branch = ReadGitStatusValue(status, "# branch.head ");
		string revision = ReadGitStatusValue(status, "# branch.oid ");
		string upstream = ReadGitStatusValue(status, "# branch.upstream ");
		string aheadBehind = ReadGitStatusValue(status, "# branch.ab ");
		if (revision.Length > 8)
		{
			revision = revision[..8];
		}

		string identity = !string.IsNullOrWhiteSpace(branch) &&
			!string.Equals(branch, "(detached)", StringComparison.OrdinalIgnoreCase)
				? $"{branch} @ {revision}"
				: string.IsNullOrWhiteSpace(revision)
					? "git revision unknown"
					: $"rev {revision}";
		if (string.IsNullOrWhiteSpace(upstream))
		{
			return $"{identity} - upstream unknown";
		}

		int behindCount = ParseGitBehindCount(aheadBehind);
		return behindCount == 0
			? $"{identity} - up to date"
			: $"{identity} - {behindCount} update(s) available";
	}

	private static string ReadGitStatusValue(string status, string prefix)
		=> status
			.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))
			?[prefix.Length..]
			.Trim() ?? string.Empty;

	private static int ParseGitBehindCount(string aheadBehind)
	{
		foreach (string part in aheadBehind.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (part.StartsWith("-", StringComparison.Ordinal) &&
				int.TryParse(part.AsSpan(1), out int behindCount))
			{
				return behindCount;
			}
		}

		return 0;
	}

	private async Task QueueManagedExtensionsAsync(bool reinstall)
	{
		const string operationId = "repair-extensions";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		bool isBusy = false;
		try
		{
			var selectedTargets = _managedExtensionOptions
				.Where(option => option.IsSelected)
				.Select(option => option.Folder)
				.ToList();
			if (selectedTargets.Count == 0)
			{
				await ShowValidationAlertAsync(LocalizationManager.Text("settings.extensions.select_at_least_one"));
				return;
			}

			bool confirmed = await ShowConfirmationAsync(
				LocalizationManager.Text(reinstall
					? "settings.extensions.confirm_reinstall_title"
					: "settings.extensions.confirm_sync_title"),
				LocalizationManager.Format(
					reinstall
						? "settings.extensions.confirm_reinstall_message"
						: "settings.extensions.confirm_sync_message",
					string.Join($"{Environment.NewLine}- ", selectedTargets)),
				LocalizationManager.Text(reinstall
					? "settings.extensions.queue_reinstall"
					: "settings.extensions.queue_sync"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			isBusy = true;
			SetExtensionsBusy(
				true,
				LocalizationManager.Text(reinstall
					? "views.overlays.settings_overlay_view.queueing_extension_reinstall_title"
					: "views.overlays.settings_overlay_view.queueing_extension_update_title"),
				LocalizationManager.Text("views.overlays.settings_overlay_view.queueing_extensions_detail"));
			await QueueBootTaskAsync(
				PendingBootTaskIds.ExtensionRepair,
				LocalizationManager.Text(reinstall
					? "views.overlays.settings_overlay_view.queueing_extension_reinstall_title"
					: "views.overlays.settings_overlay_view.queueing_extension_update_title"),
				LocalizationManager.Text("views.overlays.settings_overlay_view.queueing_extensions_detail"),
				saveDraft: true,
				targetFolders: selectedTargets,
				action: reinstall ? PendingBootTaskActions.ExtensionReinstall : PendingBootTaskActions.ExtensionSync,
				afterRefresh: () =>
				{
					ExtensionsStatusValueLabel.Text = reinstall
						? LocalizationManager.Text("settings.extensions.reinstall_scheduled")
						: LocalizationManager.Text("settings.extensions.sync_scheduled");
					_repositoryRestartRequired = false;
				},
				showBlocker: false);
		}
		finally
		{
			if (isBusy)
			{
				SetExtensionsBusy(false);
			}
			EndOperation(operationId);
		}
	}

	private async Task ApplyComfyUpdateAsync()
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!TryBeginOperation("apply-comfy-update"))
		{
			return;
		}

		SetComfyActionBusy(true);
		bool shouldRestoreUpdateButton = false;
		try
		{
			if (_comfyUpdatesAvailable <= 0)
			{
				ComfyUpdateValueLabel.Text = "Check updates first. No pending ComfyUI update is currently selected.";
				return;
			}

			shouldRestoreUpdateButton = true;
			ComfyApplyUpdateButton.IsVisible = false;
			ComfyApplyUpdateButton.IsEnabled = false;
			bool confirmed = await ShowConfirmationAsync(
				"Update ComfyUI?",
				"This will queue git pull, requirements sync, and CUDA PyTorch repair for the next server boot.",
				"Update",
				"Cancel");
			if (!confirmed)
			{
				return;
			}

			shouldRestoreUpdateButton = false;
			bool queued = await QueueBootTaskAsync(
				PendingBootTaskIds.ComfyUpdate,
				"QUEUEING COMFYUI UPDATE",
				"Saving the selected ComfyUI path and adding update + dependency repair to the next boot checklist...",
				saveDraft: true,
				afterRefresh: () =>
				{
					_repositoryRestartRequired = false;
					_comfyUpdatesAvailable = 0;
					ComfyApplyUpdateButton.IsVisible = false;
					ComfyUpdateValueLabel.Text = "ComfyUI update scheduled. Restart the server to pull updates and repair runtime dependencies before boot.";
				});
			if (!queued)
			{
				shouldRestoreUpdateButton = _comfyUpdatesAvailable > 0;
			}
		}
		finally
		{
			if (shouldRestoreUpdateButton && _comfyUpdatesAvailable > 0)
			{
				ComfyApplyUpdateButton.IsVisible = true;
			}

			SetComfyActionBusy(false);
			EndOperation("apply-comfy-update");
		}
	}

	private void OnUseSystemGitClicked(object? sender, EventArgs e)
	{
		_editor.Draft.GitSource = DiagnosticNodeHelpers.SystemOption;
		_editor.Draft.GitPath = "git";
		UpdateToolButtons();
		StartToolVersionProbe();
		UpdateStateChrome();
	}

	private void OnUseBuiltInGitClicked(object? sender, EventArgs e)
	{
		_editor.Draft.GitSource = DiagnosticNodeHelpers.BuiltInOption;
		_editor.Draft.GitPath = System.IO.Path.Combine(ComfyInstallService.InstalledPath, "Git", "cmd", "git.exe");
		UpdateToolButtons();
		StartToolVersionProbe();
		UpdateStateChrome();
	}

	private async void OnSelectCustomGitClicked(object? sender, EventArgs e)
	{
		await SelectCustomToolAsync(isGit: true);
	}

	private void OnUseSystemPythonClicked(object? sender, EventArgs e)
	{
		_editor.Draft.PythonSource = DiagnosticNodeHelpers.SystemOption;
		_editor.Draft.PythonPath = "python";
		UpdateToolButtons();
		StartToolVersionProbe();
		UpdateStateChrome();
	}

	private void OnUseBuiltInPythonClicked(object? sender, EventArgs e)
	{
		_editor.Draft.PythonSource = DiagnosticNodeHelpers.BuiltInOption;
		_editor.Draft.PythonPath = ComfyInstallService.PythonExe;
		UpdateToolButtons();
		StartToolVersionProbe();
		UpdateStateChrome();
	}

	private async void OnSelectCustomPythonClicked(object? sender, EventArgs e)
	{
		await SelectCustomToolAsync(isGit: false);
	}

	private void OnUsePipDefaultCacheClicked(object? sender, EventArgs e)
	{
		if (!_editor.SavePipCacheSettings(PipCacheModes.PipDefault, string.Empty))
		{
			_ = ShowValidationAlertAsync(LocalizationManager.Text("settings.pip_cache.save_failed"));
			return;
		}

		Refresh(startProbes: false);
	}

	private void OnUseDefaultPipCacheClicked(object? sender, EventArgs e)
	{
		if (!_editor.SavePipCacheSettings(PipCacheModes.NexusDefault, string.Empty))
		{
			_ = ShowValidationAlertAsync(LocalizationManager.Text("settings.pip_cache.save_failed"));
			return;
		}

		Refresh(startProbes: false);
	}

	private async void OnChangePipCacheClicked(object? sender, EventArgs e)
	{
		const string operationId = "select-pip-cache";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		try
		{
			var result = await PlatformManager.Current.FilePicker.PickFolderAsync(
				LocalizationManager.Text("settings.pip_cache.select_folder"));
			if (!result.IsSupported || !result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
			{
				if (!string.IsNullOrWhiteSpace(result.Message))
				{
					await ShowValidationAlertAsync(result.Message);
				}

				return;
			}

			if (!_editor.SavePipCacheSettings(PipCacheModes.Custom, result.Value))
			{
				await ShowValidationAlertAsync(LocalizationManager.Text("settings.pip_cache.save_failed"));
				return;
			}

			Refresh(startProbes: false);
		}
		finally
		{
			EndOperation(operationId);
		}
	}

	private async void OnOpenPipCacheClicked(object? sender, EventArgs e)
	{
		const string operationId = "open-pip-cache";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		try
		{
			string path = PipCacheService.GetEffectiveCachePath(_editor.Draft);
			Directory.CreateDirectory(path);
			var result = await PlatformManager.Current.Shell.OpenPathAsync(path);
			if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
			{
				await ShowValidationAlertAsync(result.Message);
			}
		}
		finally
		{
			EndOperation(operationId);
		}
	}

	private async void OnClearPipCacheClicked(object? sender, EventArgs e)
	{
		bool confirmed = await NexusDialogService.ConfirmAsync(
			LocalizationManager.Text("settings.pip_cache.clear_title"),
			LocalizationManager.Text("settings.pip_cache.clear_message"),
			LocalizationManager.Text("settings.pip_cache.clear"),
			LocalizationManager.Text("common.cancel"),
			okIsDanger: true);
		if (!confirmed)
		{
			return;
		}

		const string operationId = "clear-pip-cache";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		try
		{
			await Task.Run(() => PipCacheService.ClearCache(_editor.Draft));
			PipCacheDetailValueLabel.Text = LocalizationManager.Text("settings.pip_cache.clear_complete");
		}
		catch (Exception ex)
		{
			await ShowValidationAlertAsync(LocalizationManager.Format("settings.pip_cache.clear_failed", ex.Message));
		}
		finally
		{
			EndOperation(operationId);
		}
	}

	private async Task SelectCustomToolAsync(bool isGit)
	{
		string operationId = isGit ? "select-custom-git" : "select-custom-python";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		try
		{
			string title = isGit ? "Select git.exe" : "Select python.exe";
			var result = await PlatformManager.Current.FilePicker.PickFileAsync(title, [".exe"]);
			if (!result.IsSupported || !result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
			{
				if (!string.IsNullOrWhiteSpace(result.Message))
				{
					await ShowValidationAlertAsync(result.Message);
				}

				return;
			}

			if (isGit)
			{
				_editor.Draft.GitSource = DiagnosticNodeHelpers.CustomOption;
				_editor.Draft.GitPath = result.Value;
			}
			else
			{
				_editor.Draft.PythonSource = DiagnosticNodeHelpers.CustomOption;
				_editor.Draft.PythonPath = result.Value;
			}

			UpdateToolButtons();
			StartToolVersionProbe();
			UpdateStateChrome();
		}
		finally
		{
			EndOperation(operationId);
		}
	}

	private void OnUseConfiguredPythonClicked(object? sender, EventArgs e)
	{
		_editor.Draft.ServerPythonMode = PythonExecutionModes.ConfiguredPython;
		RemoveDraftAutoVenvCreateTask();
		UpdatePythonModeButtons();
		UpdateStateChrome();
	}

	private async void OnCreateVenvClicked(object? sender, EventArgs e)
	{
		await RunVenvMaintenanceAsync(VenvMaintenanceAction.Create);
	}

	private async void OnResetVenvClicked(object? sender, EventArgs e)
	{
		await RunVenvMaintenanceAsync(VenvMaintenanceAction.Reset);
	}

	private async void OnDeleteVenvClicked(object? sender, EventArgs e)
	{
		await RunVenvMaintenanceAsync(VenvMaintenanceAction.Delete);
	}

	private async void OnMaintenanceClearServerLogClicked(object? sender, EventArgs e)
	{
		await RunMaintenanceOperationAsync(
			"clear-server-log",
			LocalizationManager.Text("settings.logs.clear_title"),
			LocalizationManager.Text("settings.logs.clear_message"),
			ClearServerLogAsync,
			LocalizationManager.Text("settings.logs.clear_complete"),
			reloadSettings: false);
	}

	private async void OnMaintenanceResetSettingsClicked(object? sender, EventArgs e)
	{
		await ScheduleMaintenanceBootTaskAsync(
			"reset-settings",
			PendingBootTaskIds.ResetAll,
			LocalizationManager.Text("settings.maintenance.reset_settings_title"),
			LocalizationManager.Text("settings.maintenance.reset_settings_message"),
			LocalizationManager.Text("settings.maintenance.reset_settings_scheduled"));
	}

	private async void OnMaintenanceBackupRuntimeClicked(object? sender, EventArgs e)
	{
		const string operationId = "backup-runtime-data";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		var backupTargets = GetSelectedRuntimeBackupTargets();
		if (backupTargets.Count == 0)
		{
			EndOperation(operationId);
			return;
		}

		SetMaintenanceBusy(true);
		int generation = ++_runtimeBackupAnalysisGeneration;
		RuntimeBackupActivity.IsVisible = true;
		RuntimeBackupActivity.IsRunning = true;
		RuntimeBackupAnalysisLabel.Text = LocalizationManager.Text("settings.runtime_backup.calculating");
		RuntimeBackupAnalysisLabel.TextColor = SettingsInfoTextColor;
		MaintenanceBackupProgressBar.Progress = 0;
		try
		{
			RuntimeBackupAnalysis analysis = await ComfyInstallService.Instance.AnalyzeRuntimeBackupAsync(
				backupTargets,
				CancellationToken.None);
			if (generation != _runtimeBackupAnalysisGeneration)
			{
				return;
			}

			_runtimeBackupAnalysis = analysis;
			RuntimeBackupActivity.IsRunning = false;
			RuntimeBackupActivity.IsVisible = false;
			RefreshRuntimeBackupCard();
			if (!analysis.IsSuccess)
			{
				RuntimeBackupAnalysisLabel.Text = analysis.AvailableBytes >= 0
					? LocalizationManager.Format(
						"settings.runtime_backup.insufficient_space",
						RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
						RuntimeBackupService.FormatBytes(analysis.AvailableBytes))
					: LocalizationManager.Format(
						"settings.runtime_backup.analysis_failed",
						analysis.Message);
				RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
				return;
			}

			string formatLabel = IsRuntimeBackupFormat(RuntimeBackupFormats.Zip)
				? LocalizationManager.Text("settings.runtime_backup.zip")
				: LocalizationManager.Text("settings.runtime_backup.folder");
			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.analysis_ready",
				RuntimeBackupService.FormatBytes(analysis.SourceBytes),
				analysis.FileCount,
				RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
				RuntimeBackupService.FormatBytes(analysis.AvailableBytes));
			RuntimeBackupAnalysisLabel.TextColor = SettingsSuccessTextColor;

			string selectedTargets = string.Join(", ", analysis.Targets.Select(GetRuntimeBackupLabel));
			bool confirmed = await NexusDialogService.ConfirmAsync(
				LocalizationManager.Text("settings.runtime_backup.confirm_title"),
				LocalizationManager.Format(
					"settings.runtime_backup.confirm_message",
					selectedTargets,
					formatLabel,
					RuntimeBackupService.FormatBytes(analysis.SourceBytes),
					RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
					RuntimeBackupService.FormatBytes(analysis.AvailableBytes),
					analysis.BackupRoot),
				LocalizationManager.Text("settings.runtime_backup.start"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			RuntimeBackupAnalysis finalCheck = ComfyInstallService.Instance.RefreshRuntimeBackupSpace(analysis);
			if (!finalCheck.IsSuccess)
			{
				RuntimeBackupAnalysisLabel.Text = finalCheck.AvailableBytes >= 0
					? LocalizationManager.Format(
						"settings.runtime_backup.insufficient_space",
						RuntimeBackupService.FormatBytes(finalCheck.RequiredBytes),
						RuntimeBackupService.FormatBytes(finalCheck.AvailableBytes))
					: LocalizationManager.Format("settings.runtime_backup.analysis_failed", finalCheck.Message);
				RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
				return;
			}

			BeginComfyOperationLog("RUNTIME BACKUP");
			var service = ComfyInstallService.Instance;
			Action<string>? previousLogHandler = service.OnMessage;
			Action<double, string>? previousProgressHandler = service.OnProgress;
			SetupStepResult result;
			try
			{
				service.OnMessage = AppendComfyOperationLog;
				service.OnProgress = UpdateMaintenanceBackupProgress;
				result = await service.BackupRuntimeDataAsync(
					finalCheck,
					_editor.Draft.RuntimeBackupFormat,
					CancellationToken.None);
			}
			finally
			{
				service.OnMessage = previousLogHandler;
				service.OnProgress = previousProgressHandler;
			}

			string resultMessage = result.IsSuccess
				? LocalizationManager.Format(
					"settings.runtime_backup.backup_complete",
					result.Message.StartsWith("Runtime backup completed: ", StringComparison.Ordinal)
						? result.Message["Runtime backup completed: ".Length..]
						: result.Message)
				: LocalizationManager.Format("settings.runtime_backup.operation_failed", result.Message);
			RuntimeBackupAnalysisLabel.Text = resultMessage;
			RuntimeBackupAnalysisLabel.TextColor = result.IsSuccess
				? SettingsSuccessTextColor
				: SettingsFailureTextColor;
			CompleteComfyOperationLog(result.IsSuccess, resultMessage);
			if (result.IsSuccess)
			{
				MaintenanceBackupProgressBar.Progress = 1;
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS] Runtime backup failed");
			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.operation_failed",
				ex.Message);
			RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
			CompleteComfyOperationLog(false, ex.Message);
		}
		finally
		{
			RuntimeBackupActivity.IsRunning = false;
			RuntimeBackupActivity.IsVisible = false;
			SetMaintenanceBusy(false);
			EndOperation(operationId);
		}
	}

	private async void OnMaintenanceRestoreRuntimeClicked(object? sender, EventArgs e)
	{
		const string operationId = "analyze-runtime-restore";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		string? backupPath = await PickRuntimeRestoreFolderAsync();
		if (string.IsNullOrWhiteSpace(backupPath))
		{
			EndOperation(operationId);
			return;
		}

		SetMaintenanceBusy(true);
		await SetSettingsOperationBlockerVisibleAsync(
			true,
			LocalizationManager.Text("settings.runtime_backup.restore_analyzing_title"),
			LocalizationManager.Text("settings.runtime_backup.restore_analyzing_detail"));
		try
		{
			RuntimeRestoreAnalysis analysis = await ComfyInstallService.Instance.AnalyzeRuntimeRestoreAsync(
				backupPath,
				CancellationToken.None);
			if (!analysis.IsSuccess)
			{
				RuntimeBackupAnalysisLabel.Text = analysis.AvailableBytes >= 0
					? LocalizationManager.Format(
						"settings.runtime_backup.insufficient_space",
						RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
						RuntimeBackupService.FormatBytes(analysis.AvailableBytes))
					: LocalizationManager.Format("settings.runtime_backup.analysis_failed", analysis.Message);
				RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
				return;
			}

			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.restore_analysis_ready",
				analysis.AddCount,
				analysis.ReplaceCount,
				analysis.UnchangedCount,
				analysis.RetainedCount);
			RuntimeBackupAnalysisLabel.TextColor = SettingsSuccessTextColor;
			if (analysis.AddCount + analysis.ReplaceCount == 0)
			{
				await NexusDialogService.AlertAsync(
					LocalizationManager.Text("settings.runtime_backup.restore_no_changes_title"),
					LocalizationManager.Format(
						"settings.runtime_backup.restore_no_changes_message",
						analysis.UnchangedCount,
						analysis.RetainedCount));
				return;
			}

			bool startRestore = false;
			string preview = string.Join(
				Environment.NewLine,
				analysis.Items
					.Where(item => item.Action is RuntimeRestoreAction.Add or RuntimeRestoreAction.Replace)
					.Take(6)
					.Select(item => $"- {item.RelativePath}"));
			if (analysis.AddCount + analysis.ReplaceCount > 6)
			{
				preview += $"{Environment.NewLine}- ...";
			}
			string mappings = string.Join(
				Environment.NewLine,
				analysis.Targets.Select(target =>
					$"{target}: {(analysis.BackupFormat == RuntimeBackupFormats.Zip ? $"{analysis.BackupPath}!/{target}" : System.IO.Path.Combine(analysis.BackupPath, target))} -> {System.IO.Path.Combine(analysis.ComfyPath, target)}"));

			await NexusDialogService.ChoiceAsync(
				LocalizationManager.Text("settings.runtime_backup.restore_confirm_title"),
				LocalizationManager.Format(
					"settings.runtime_backup.restore_analysis_message",
					analysis.BackupPath,
					analysis.ComfyPath,
					analysis.AddCount,
					analysis.ReplaceCount,
					analysis.UnchangedCount,
					analysis.RetainedCount,
					RuntimeBackupService.FormatBytes(analysis.CopyBytes),
					RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
					RuntimeBackupService.FormatBytes(analysis.AvailableBytes),
					preview,
					mappings),
				[
					new NexusDialogChoice(
						LocalizationManager.Text("settings.runtime_backup.open_change_list"),
						async () =>
						{
							var openResult = await PlatformManager.Current.Shell.OpenPathAsync(analysis.PreviewReportPath);
							if (!openResult.IsSuccess)
							{
								throw new InvalidOperationException(openResult.Message
									?? LocalizationManager.Text("settings.runtime_backup.open_report_failed"));
							}
							return NexusDialogActionResult.KeepOpen;
						}),
					new NexusDialogChoice(
						LocalizationManager.Text("settings.runtime_backup.start_restore"),
						() =>
						{
							startRestore = true;
							return Task.FromResult(NexusDialogActionResult.Close);
						},
						IsDanger: true)
				],
				LocalizationManager.Text("common.cancel"));
			if (!startRestore)
			{
				return;
			}

			var args = new RuntimeRestoreRequestedEventArgs(
				new RuntimeRestoreRequest(
					analysis,
					ComfyServerProcessRegistry.FindServerProcess() != null));
			if (RuntimeRestoreRequested is null)
			{
				RuntimeBackupAnalysisLabel.Text = LocalizationManager.Text("settings.runtime_backup.restore_handler_missing");
				RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
				return;
			}

			RuntimeRestoreRequested.Invoke(this, args);
			RuntimeRestoreResult result = await args.Completion.Task;
			RuntimeBackupAnalysisLabel.Text = result.IsSuccess
				? LocalizationManager.Format(
					args.Request.ServerWasRunning
						? "settings.runtime_backup.restore_merge_complete_restarting"
						: "settings.runtime_backup.restore_merge_complete",
					result.CompletedFiles)
				: LocalizationManager.Format("settings.runtime_backup.restore_merge_failed", result.Message);
			RuntimeBackupAnalysisLabel.TextColor = result.IsSuccess ? SettingsSuccessTextColor : SettingsFailureTextColor;
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS] Runtime restore analysis failed");
			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.analysis_failed",
				ex.Message);
			RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
		}
		finally
		{
			await SetSettingsOperationBlockerVisibleAsync(false);
			SetMaintenanceBusy(false);
			EndOperation(operationId);
		}
	}

	private async void OnMaintenanceDeleteBackupClicked(object? sender, EventArgs e)
	{
		string? backupPath = await PickRuntimeBackupFolderToDeleteAsync();
		if (string.IsNullOrWhiteSpace(backupPath))
		{
			SetMaintenanceStatus(LocalizationManager.Text("settings.runtime_backup.none_selected"));
			return;
		}

		string backupName = System.IO.Path.GetFileName(backupPath);
		ResetMaintenanceBackupProgress(LocalizationManager.Format(
			"settings.runtime_backup.ready_to_delete",
			backupName));
		await RunMaintenanceOperationAsync(
			"delete-runtime-backup",
			LocalizationManager.Text("settings.runtime_backup.delete_confirm_title"),
			LocalizationManager.Format("settings.runtime_backup.delete_confirm_message", backupPath),
			ct => ComfyInstallService.Instance.DeleteRuntimeBackupAsync(backupPath, ct),
			LocalizationManager.Text("settings.runtime_backup.delete_complete"),
			reloadSettings: false,
			preferSuccessMessage: true);
	}

	private async void OnMaintenanceOpenBackupFolderClicked(object? sender, EventArgs e)
	{
		try
		{
			Directory.CreateDirectory(RuntimeBackupsPath);
			var result = await PlatformManager.Current.Shell.OpenPathAsync(RuntimeBackupsPath);
			if (!result.IsSuccess)
			{
				SetMaintenanceStatus(string.IsNullOrWhiteSpace(result.Message)
					? LocalizationManager.Text("settings.runtime_backup.open_failed")
					: result.Message);
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS] Unable to open runtime backup folder");
			SetMaintenanceStatus(ex.Message);
		}
	}

	private async void OnMaintenancePurgeRuntimeClicked(object? sender, EventArgs e)
	{
		const string operationId = "purge-local-runtime";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		try
		{
			string deletePath = ComfyInstallService.InstalledPath;
			string backupPath = ComfyInstallService.RuntimeBackupsPath;
			bool confirmed = await ShowConfirmationAsync(
				LocalizationManager.Text("settings.maintenance.purge_runtime_title"),
				LocalizationManager.Format(
					"settings.maintenance.purge_runtime_message",
					deletePath,
					backupPath),
				LocalizationManager.Text("settings.maintenance.prepare_reset"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			await SetSettingsOperationBlockerVisibleAsync(
				true,
				LocalizationManager.Text("settings.maintenance.purge_runtime_queue_title"),
				LocalizationManager.Text("settings.maintenance.purge_runtime_queue_detail"));
			try
			{
				await Task.Yield();
				SetupSettingsService.Instance.ScheduleRuntimePurge();
				SetupSettingsService.Instance.Reload();
				_editor.Reload();
				Refresh(startProbes: false);
				SetMaintenanceStatus(LocalizationManager.Text("settings.maintenance.purge_runtime_scheduled"));
			}
			finally
			{
				await SetSettingsOperationBlockerVisibleAsync(false);
			}

			RuntimePurgeRequested?.Invoke(this, EventArgs.Empty);
		}
		finally
		{
			EndOperation(operationId);
		}
	}

	private async Task ScheduleMaintenanceBootTaskAsync(
		string operationId,
		string taskId,
		string title,
		string message,
		string scheduledMessage)
	{
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		try
		{
			bool confirmed = await ShowConfirmationAsync(
				title,
				message,
				LocalizationManager.Text("settings.maintenance.prepare"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			bool queued = await QueueBootTaskAsync(
				taskId,
				LocalizationManager.Text("settings.maintenance.queue_title"),
				LocalizationManager.Text("settings.maintenance.queue_detail"),
				saveDraft: false,
				afterRefresh: () => SetMaintenanceStatus(scheduledMessage));
			if (!queued)
			{
				return;
			}

			RuntimePurgeRequested?.Invoke(this, EventArgs.Empty);
		}
		finally
		{
			EndOperation(operationId);
		}
	}

	private async Task RunMaintenanceOperationAsync(
		string operationId,
		string title,
		string message,
		Func<CancellationToken, Task<SetupStepResult>> operation,
		string successMessage,
		bool reloadSettings,
		TimeSpan? timeout = default,
		bool preferSuccessMessage = false)
	{
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		SetMaintenanceBusy(true);
		try
		{
			bool confirmed = await ShowConfirmationAsync(
				title,
				message,
				LocalizationManager.Text("settings.maintenance.continue"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			SetMaintenanceStatus($"{title.TrimEnd('?')} running...");
			BeginComfyOperationLog("MAINTENANCE LOG");
			var service = ComfyInstallService.Instance;
			Action<string>? previousLogHandler = service.OnMessage;
			Action<double, string>? previousProgressHandler = service.OnProgress;
			using var timeoutSource = timeout.HasValue
				? new CancellationTokenSource(timeout.Value)
				: operationId is "backup-runtime-data" or "restore-runtime-data"
					? new CancellationTokenSource()
					: new CancellationTokenSource(TimeSpan.FromMinutes(60));
			SetupStepResult result;
			try
			{
				service.OnMessage = AppendComfyOperationLog;
				service.OnProgress = UpdateMaintenanceBackupProgress;
				result = await operation(timeoutSource.Token);
			}
			catch (OperationCanceledException)
			{
				SetMaintenanceStatus("Maintenance operation timed out.");
				CompleteComfyOperationLog(false, "Maintenance operation timed out.");
				return;
			}
			catch (Exception ex)
			{
				SetMaintenanceStatus($"Maintenance failed: {ex.Message}");
				CompleteComfyOperationLog(false, ex.Message);
				return;
			}
			finally
			{
				service.OnMessage = previousLogHandler;
				service.OnProgress = previousProgressHandler;
			}

			if (!result.IsSuccess)
			{
				SetMaintenanceStatus(result.Message);
				CompleteComfyOperationLog(false, result.Message);
				return;
			}

			if (reloadSettings)
			{
				SetupSettingsService.Instance.Reload();
				_editor.Reload();
				Refresh();
			}

			string finalMessage = preferSuccessMessage || string.IsNullOrWhiteSpace(result.Message)
				? successMessage
				: result.Message;
			SetMaintenanceStatus(finalMessage);
			if (operationId is "backup-runtime-data" or "restore-runtime-data")
			{
				UpdateMaintenanceBackupProgress(1, finalMessage);
			}

			CompleteComfyOperationLog(true, finalMessage);
			TryUpdateUi(UpdateStateChrome);
		}
		finally
		{
			SetMaintenanceBusy(false);
			EndOperation(operationId);
		}
	}

	private static Task<SetupStepResult> ClearServerLogAsync(CancellationToken cancellationToken)
	{
		return Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				string logsDirectory = ComfyInstallService.GetLocalRuntimePath("Logs");
				string latestLogPath = SessionLogPaths.GetLatestLogPath(logsDirectory, SessionLogPaths.ComfyServerLatestFileName);
				string? directory = System.IO.Path.GetDirectoryName(latestLogPath);
				if (!string.IsNullOrWhiteSpace(directory))
				{
					Directory.CreateDirectory(directory);
				}

				var targets = new List<string> { latestLogPath };
				string? activeSessionLogPath = ComfyServerProcessRegistry.FindServerProcess()?.LogPath;
				if (!string.IsNullOrWhiteSpace(activeSessionLogPath)
					&& !targets.Any(path => string.Equals(path, activeSessionLogPath, StringComparison.OrdinalIgnoreCase)))
				{
					targets.Add(activeSessionLogPath);
				}

				int clearedCount = 0;
				int missingCount = 0;
				var lockedFiles = new List<string>();
				foreach (string target in targets)
				{
					ClearLogFileResult clearResult = TryClearLogFile(target);
					switch (clearResult)
					{
						case ClearLogFileResult.Cleared:
							clearedCount++;
							break;
						case ClearLogFileResult.Missing:
							missingCount++;
							break;
						case ClearLogFileResult.Locked:
							lockedFiles.Add(target);
							break;
					}
				}

				if (lockedFiles.Count > 0)
				{
					return new SetupStepResult(
						true,
						LocalizationManager.Format("settings.logs.clear_locked", clearedCount, lockedFiles.Count),
						1);
				}

				if (clearedCount == 0 && missingCount == targets.Count)
				{
					return new SetupStepResult(true, LocalizationManager.Text("settings.logs.clear_already_empty"), 1);
				}

				return new SetupStepResult(
					true,
					LocalizationManager.Format("settings.logs.clear_complete_count", clearedCount),
					1);
			}
			catch (Exception ex)
			{
				return new SetupStepResult(false, LocalizationManager.Format("settings.logs.clear_failed", ex.Message), 0);
			}
		}, cancellationToken);
	}

	private static ClearLogFileResult TryClearLogFile(string logPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
			{
				return ClearLogFileResult.Missing;
			}

			using var stream = new FileStream(
				logPath,
				FileMode.Open,
				FileAccess.Write,
				FileShare.ReadWrite | FileShare.Delete);
			stream.SetLength(0);
			return ClearLogFileResult.Cleared;
		}
		catch (IOException)
		{
			return ClearLogFileResult.Locked;
		}
		catch (UnauthorizedAccessException)
		{
			return ClearLogFileResult.Locked;
		}
	}

	private enum ClearLogFileResult
	{
		Cleared,
		Missing,
		Locked
	}

	private async Task RunVenvMaintenanceAsync(VenvMaintenanceAction action)
	{
		if (!TryBeginOperation("venv-maintenance"))
		{
			return;
		}

		VenvActionGroup.IsVisible = false;
		try
		{
			string title = action switch
			{
				VenvMaintenanceAction.Reset => LocalizationManager.Text("settings.venv.reset_title"),
				VenvMaintenanceAction.Delete => LocalizationManager.Text("settings.venv.delete_title"),
				_ => LocalizationManager.Text("settings.venv.create_title")
			};
			bool draftUsesVenvBeforeAction = IsDraftUsingVenv();
			bool activeServerUsesVenv = ActiveServerUsesVenv();
			string message = action switch
			{
				VenvMaintenanceAction.Reset => draftUsesVenvBeforeAction || activeServerUsesVenv
					? LocalizationManager.Text("settings.venv.reset_message_active")
					: LocalizationManager.Text("settings.venv.reset_message_direct"),
				VenvMaintenanceAction.Delete => draftUsesVenvBeforeAction || activeServerUsesVenv
					? LocalizationManager.Text("settings.venv.delete_message_active")
					: LocalizationManager.Text("settings.venv.delete_message_direct"),
				_ => draftUsesVenvBeforeAction
					? LocalizationManager.Text("settings.venv.create_message_venv")
					: LocalizationManager.Text("settings.venv.create_message_direct")
			};

			if (!await ShowConfirmationAsync(
				title,
				message,
				LocalizationManager.Text("settings.maintenance.continue"),
				LocalizationManager.Text("common.cancel")))
			{
				UpdateVenvCard();
				return;
			}

			if (action == VenvMaintenanceAction.Delete && draftUsesVenvBeforeAction)
			{
				_editor.Draft.ServerPythonMode = PythonExecutionModes.ConfiguredPython;
			}

			bool queued = await QueueBootTaskAsync(
				action switch
				{
					VenvMaintenanceAction.Reset => PendingBootTaskIds.VenvRebuild,
					VenvMaintenanceAction.Delete => PendingBootTaskIds.VenvDelete,
					_ => PendingBootTaskIds.VenvCreate
				},
				action switch
				{
					VenvMaintenanceAction.Reset => LocalizationManager.Text("settings.venv.queue_rebuild_title"),
					VenvMaintenanceAction.Delete => LocalizationManager.Text("settings.venv.queue_delete_title"),
					_ => LocalizationManager.Text("settings.venv.queue_create_title")
				},
				LocalizationManager.Text("settings.venv.queue_detail"),
				saveDraft: true,
				afterRefresh: () =>
				{
					_venvRestartRequired = false;
					VenvDetailValueLabel.Text = action switch
					{
						VenvMaintenanceAction.Reset => LocalizationManager.Text("settings.venv.rebuild_scheduled"),
						VenvMaintenanceAction.Delete => LocalizationManager.Text("settings.venv.delete_scheduled"),
						_ => LocalizationManager.Text("settings.venv.create_scheduled")
					};
				});
			if (!queued)
			{
				UpdateVenvCard();
			}
		}
		finally
		{
			EndOperation("venv-maintenance");
		}
	}

	private bool IsDraftUsingVenv()
		=> string.Equals(_editor.Draft.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal);

	private bool HasDraftBootTask(string taskId)
		=> _editor.Draft.PendingBootTasks.Any(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

	private bool IsRequiredVenvCreateTask(string taskId)
		=> string.Equals(taskId, PendingBootTaskIds.VenvCreate, StringComparison.Ordinal)
			&& string.Equals(_editor.Draft.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal)
			&& !File.Exists(ComfyInstallService.ComfyVenvPythonExe);

	private void AddDraftBootTask(string taskId, string origin = "")
	{
		if (HasDraftBootTask(taskId)) return;

		if (taskId is PendingBootTaskIds.VenvCreate or PendingBootTaskIds.VenvRebuild or PendingBootTaskIds.VenvDelete)
		{
			_editor.Draft.PendingBootTasks.RemoveAll(task =>
				(task.Id is PendingBootTaskIds.VenvCreate or PendingBootTaskIds.VenvRebuild or PendingBootTaskIds.VenvDelete)
				&& !string.Equals(task.Id, taskId, StringComparison.Ordinal));
		}

		_editor.Draft.PendingBootTasks.Add(new PendingBootTask { Id = taskId, Origin = origin });
	}

	private void RemoveDraftBootTask(string taskId)
		=> _editor.Draft.PendingBootTasks.RemoveAll(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

	private void RemoveDraftAutoVenvCreateTask()
		=> _editor.Draft.PendingBootTasks.RemoveAll(task =>
			string.Equals(task.Id, PendingBootTaskIds.VenvCreate, StringComparison.Ordinal)
			&& string.Equals(task.Origin, PendingBootTaskOrigins.VenvModeSelection, StringComparison.Ordinal));

	private static bool ActiveServerUsesVenv()
		=> RuntimePythonModePresenter.ShouldDisplayVenvMode(SetupSettingsService.Instance.Settings);

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
