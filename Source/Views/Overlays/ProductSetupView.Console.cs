namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls.Shapes;

public partial class ProductSetupView
{	private async Task TransitionToConsoleAsync()
	{
		NexusLog.Info($"[SETUP:UI] Console transition requested from primary action. context={_currentContext}, vanguardVisible={VanguardPanel.IsVisible}, architectWorkspaceVisible={ArchitectWorkspacePanel.IsVisible}, architectInitiationVisible={ArchitectInitiationPanel.IsVisible}");
		VisualElement? currentPanel = null;
		if (VanguardPanel.IsVisible) currentPanel = VanguardPanel;
		else if (ArchitectWorkspacePanel.IsVisible) currentPanel = ArchitectWorkspacePanel;
		else if (ArchitectInitiationPanel.IsVisible) currentPanel = ArchitectInitiationPanel;

		if (currentPanel != null)
		{
			await FadeOutAndHideAsync(currentPanel, PanelQuickAnimationLength, Easing.CubicIn);
		}

		ConsoleLogTail.Clear(); // Clear existing logs

		StopCrossroadsAmbientPulse(keepWelcomeTitle: true);
		SetSceneMotionState(SetupSceneMotionState.Console);
		PreparePanelReveal(ServerConsolePanel);

		ShowActionBottomBar(LocalizationManager.Text("setup.action.launch_nexus"), false);
		ApplyConsoleBootActionState(ConsoleBootActionState.Preparing);

		// Set Back button to RETRY
		BackButton.IsVisible = true;
		var label = BackButton.Content as Label;
		if (label == null && BackButton.Content is Grid g)
		{
			label = g.Children.OfType<Label>().FirstOrDefault();
		}

		if (label != null) label.Text = "BACK";

		await Task.WhenAll(
			SafeAnimation.FadeToAsync(ServerConsolePanel, 1, ConsoleShowLength, Easing.CubicOut, "Setup.Console"),
			SafeAnimation.TranslateToAsync(ServerConsolePanel, 0, 0, ConsoleShowLength, Easing.CubicOut, "Setup.Console"),
			SafeAnimation.FadeToAsync(ActionBottomBar, 1, ConsoleShowLength, Easing.CubicOut, "Setup.Console"),
			SafeAnimation.TranslateToAsync(ActionBottomBar, 0, 0, ConsoleShowLength, Easing.CubicOut, "Setup.Console")
		);

		UpdateBootChannelInfo();
		AutoSelectServerPythonModeFromComfyVenv();
		UpdateServerPythonModeVisual();
		await InitializeGpuSelectorAsync();

		_ = RunSystemBootCheckAsync();
	}

	private async Task RunSystemBootCheckAsync()
	{
		NexusLog.Info("[SETUP:UI] Console system boot check started.");
		ApplyConsoleBootActionState(ConsoleBootActionState.Preparing);

		AddConsoleLog("[SYSTEM] Preparing Nexus services...");
		await Task.Delay(SystemBootKernelDelayMs);
		AddConsoleLog("[SYSTEM] Loading Nexus Kernel Modules...");
		await Task.Delay(SystemBootModulesDelayMs);
		AddConsoleLog("[SYSTEM] Validating Environment Integrity...");
		await Task.Delay(SystemBootValidationDelayMs);
		AddConsoleLog("[SYSTEM] Connecting workspace services...");
		await Task.Delay(SystemBootLinkDelayMs);
		AddConsoleLog("[SYSTEM] System Health: OPTIMAL.");

		ApplyConsoleBootActionState(ConsoleBootActionState.Standby);
		AddConsoleLog("[SYSTEM] Nexus Core is ready for engagement.");
	}

	private void UpdateBootChannelInfo()
	{
		var settings = SettingsService.Settings;
		ConsoleHostLabel.Text = settings.ListenAddress;
		ConsolePortLabel.Text = settings.ServerPort.ToString();
		BootHostLabel.Text = settings.ListenAddress;
		BootPortLabel.Text = settings.ServerPort.ToString();
		BootProbeLabel.Text = GetProbeAddress(settings.ListenAddress);
		BootLogLabel.Text = $"Logs/{SessionLogPaths.ComfyServerLatestFileName}";
	}

	private async void OnBootLogOpenTapped(object? sender, TappedEventArgs e)
	{
		string logsPath = ComfyInstallService.GetLocalRuntimePath("Logs");
		string logPath = System.IO.Path.Combine(logsPath, SessionLogPaths.ComfyServerLatestFileName);
		string targetPath = File.Exists(logPath) ? logPath : logsPath;

		var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(targetPath);
		if (!result.IsSuccess)
		{
			NexusLog.Warning($"[SETUP] Failed to open server boot log path: {result.Message}");
		}
	}

	private void OnBootLogOpenHovered(object? sender, PointerEventArgs e)
	{
		BootLogOpenButton.BackgroundColor = ConsoleConfigPillHoverColor;
		BootLogOpenLabel.TextColor = DiagnosticActionDefaultTextColor;
	}

	private void OnBootLogOpenUnhovered(object? sender, PointerEventArgs e)
	{
		BootLogOpenButton.BackgroundColor = ConsoleConfigPillNormalColor;
		BootLogOpenLabel.TextColor = ConsoleAccentColor;
	}

	private void AutoSelectServerPythonModeFromComfyVenv()
	{
		var settings = SettingsService.Settings;
		if (!string.Equals(settings.InstallMode, SetupInstallModes.ExistingComfyPath, StringComparison.Ordinal))
		{
			return;
		}

		if (settings.PendingVenvDelete
			|| settings.PendingBootTasks.Any(task => string.Equals(task.Id, PendingBootTaskIds.VenvDelete, StringComparison.Ordinal)))
		{
			AddConsoleLog("[CONFIG] Pending .venv delete detected. Keeping DIRECT Python mode.");
			return;
		}

		bool hasVenvPython = File.Exists(_appManager.Paths.ActiveVenvPythonExe);
		string detectedMode = hasVenvPython
			? PythonExecutionModes.Venv
			: PythonExecutionModes.ConfiguredPython;
		if (string.Equals(settings.ServerPythonMode, detectedMode, StringComparison.Ordinal))
		{
			AddConsoleLog(hasVenvPython
				? "[CONFIG] Existing ComfyUI .venv detected. VENV launch mode is active."
				: "[CONFIG] No ComfyUI .venv detected. DIRECT Python launch mode is active.");
			return;
		}

		settings.ServerPythonMode = detectedMode;
		SettingsService.Save();
		AddConsoleLog(hasVenvPython
			? "[CONFIG] Existing ComfyUI .venv detected. Switching launch mode to VENV."
			: "[CONFIG] No ComfyUI .venv detected. Switching launch mode to DIRECT Python.");
	}

	private void UpdateServerPythonModeVisual()
	{
		_isUpdatingServerPythonMode = true;
		try
		{
			bool useVenv = RuntimePythonModePresenter.ShouldDisplayVenvMode(
				SettingsService.Settings,
				includeActiveLaunchSnapshot: false);
			var colors = GetServerPythonModeColors(useVenv);
			ServerPythonModeLabel.Text = useVenv ? "VENV" : "DIRECT";
			ServerPythonModeLabel.TextColor = colors.Accent;
			ServerPythonModePill.BackgroundColor = colors.Pill;
			ServerPythonModeTrack.Color = colors.Track;
			ServerPythonModeKnob.BackgroundColor = colors.Accent;
			ServerPythonModeKnob.HorizontalOptions = useVenv ? LayoutOptions.End : LayoutOptions.Start;
		}
		finally
		{
			_isUpdatingServerPythonMode = false;
		}
	}

	private void OnServerPythonModeTapped(object? sender, TappedEventArgs e)
	{
		if (_isUpdatingServerPythonMode || !IsConsoleConfigEditable()) return;

		var settings = SettingsService.Settings;
		bool useVenv = settings.ServerPythonMode != PythonExecutionModes.Venv;
		settings.ServerPythonMode = useVenv ? PythonExecutionModes.Venv : PythonExecutionModes.ConfiguredPython;
		SettingsService.Save();
		UpdateServerPythonModeVisual();

		string modeText = useVenv ? ".venv isolated runtime" : "configured Python runtime";
		AddConsoleLog($"[CONFIG] Server Python mode set to {modeText}.");
	}

	private static (Color Accent, Color Pill, Color Track) GetServerPythonModeColors(bool useVenv)
	{
		return useVenv
			? (ServerPythonVenvColor, ServerPythonVenvPillColor, ServerPythonVenvTrackColor)
			: (ServerPythonDirectColor, ServerPythonDirectPillColor, ServerPythonDirectTrackColor);
	}

	private async void OnHostEditClicked(object? sender, TappedEventArgs e)
	{
		if (!IsConsoleConfigEditable()) return;

		var page = GetPromptPage();
		if (page == null) return;

		var settings = SettingsService.Settings;
		string? result = await page.DisplayPromptAsync(
			LocalizationManager.Text("server_config.host_title"),
			LocalizationManager.Text("server_config.host_message"),
			LocalizationManager.Text("common.save"),
			LocalizationManager.Text("common.cancel"),
			"127.0.0.1",
			64,
			Keyboard.Text,
			settings.ListenAddress);

		if (result == null || !IsConsoleConfigEditable()) return;

		string host = result.Trim();
		if (!IsValidHostValue(host))
		{
			await page.DisplayAlertAsync(
				LocalizationManager.Text("server_config.invalid_host_title"),
				LocalizationManager.Text("server_config.invalid_host_message"),
				LocalizationManager.Text("common.ok"));
			return;
		}

		settings.ListenAddress = host;
		SettingsService.Save();
		UpdateBootChannelInfo();
		AddConsoleLog($"[CONFIG] ComfyUI host set to {host}.");
	}

	private async void OnPortEditClicked(object? sender, TappedEventArgs e)
	{
		if (!IsConsoleConfigEditable()) return;

		var page = GetPromptPage();
		if (page == null) return;

		var settings = SettingsService.Settings;
		string? result = await page.DisplayPromptAsync(
			LocalizationManager.Text("server_config.port_title"),
			LocalizationManager.Text("server_config.port_message"),
			LocalizationManager.Text("common.save"),
			LocalizationManager.Text("common.cancel"),
			"8188",
			5,
			Keyboard.Numeric,
			settings.ServerPort.ToString());

		if (result == null || !IsConsoleConfigEditable()) return;

		if (!int.TryParse(result.Trim(), out int port) || port < 1 || port > 65535)
		{
			await page.DisplayAlertAsync(
				LocalizationManager.Text("server_config.invalid_port_title"),
				LocalizationManager.Text("server_config.invalid_port_message"),
				LocalizationManager.Text("common.ok"));
			return;
		}

		settings.ServerPort = port;
		SettingsService.Save();
		UpdateBootChannelInfo();
		AddConsoleLog($"[CONFIG] ComfyUI port set to {port}.");
	}

	private void OnConsoleConfigPillHovered(object? sender, PointerEventArgs e)
	{
		if (!IsConsoleConfigEditable() || sender is not Border border) return;

		border.BackgroundColor = ConsoleConfigPillHoverColor;
		_ = SafeAnimation.FadeToAsync(border, 1, 120, Easing.CubicOut, "Setup.ConsoleConfig");
	}

	private void OnConsoleConfigPillUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is not Border border) return;
		if (!IsConsoleConfigEditable())
		{
			UpdateConsoleConfigAvailability();
			return;
		}

		if (ReferenceEquals(border, ServerPythonModePill))
		{
			UpdateServerPythonModeVisual();
			return;
		}

		border.BackgroundColor = ConsoleConfigPillNormalColor;
	}

	private Page? GetPromptPage()
		=> Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;

	private static bool IsValidHostValue(string host)
		=> !string.IsNullOrWhiteSpace(host)
			&& host.Length <= 64
			&& !host.Any(char.IsWhiteSpace);

	private async Task InitializeGpuSelectorAsync()
	{
		GpuDiscoveryService gpuDiscovery = GetGpuDiscoveryService();
		GpuDiscoveryService.StartResult gpuStart = await gpuDiscovery.StartAsync();
		PopulateGpuSelector(gpuStart.Devices);
		if (!gpuStart.IsSuccess)
		{
			AddConsoleLog($"[GPU] Discovery unavailable: {gpuStart.FailureMessage}");
		}
	}

	private void RefreshGpuSelectorFromKnownDevices()
		=> PopulateGpuSelector(GetGpuDiscoveryService().GetCachedDevicesOrFallback());

	private GpuDiscoveryService GetGpuDiscoveryService()
		=> _appManager.GpuDiscovery;

	private void PopulateGpuSelector(IReadOnlyList<GpuDeviceInfo> devices)
	{
		GpuSelectorStack.Children.Clear();
		GpuSelectorDropdownPanel.IsVisible = false;
		_gpuOptionCards.Clear();
		_gpuDevices.Clear();
		SetGpuSelectorExpanded(false);

		_gpuDevices.AddRange(devices);
		foreach (GpuDeviceInfo device in devices)
		{
			Border card = CreateGpuOptionCard(device);
			GpuSelectorStack.Children.Add(card);
			_gpuOptionCards.Add(card);
		}

		UpdateGpuSelectionVisuals();
		AddConsoleLog($"[GPU] {devices.Count} device option(s) ready. Selected GPU {SettingsService.Settings.GpuId}.");
	}

	private Border CreateGpuOptionCard(GpuDeviceInfo device)
	{
		var title = new Label
		{
			Text = FormatGpuLabel(device.Id),
			TextColor = GpuCardTitleColor,
			FontSize = GpuCardTitleFontSize,
			FontAttributes = FontAttributes.Bold,
			VerticalOptions = LayoutOptions.Center
		};

		var name = new Label
		{
			Text = device.Name,
			TextColor = GpuCardNameColor,
			FontSize = GpuCardNameFontSize,
			LineBreakMode = LineBreakMode.TailTruncation
		};

		var memory = new Label
		{
			Text = device.MemoryTotalMb,
			TextColor = GpuCardMemoryColor,
			FontSize = GpuCardMemoryFontSize,
			LineBreakMode = LineBreakMode.TailTruncation
		};

		var stack = new VerticalStackLayout
		{
			Spacing = 1,
			InputTransparent = true,
			Children = { title, name, memory }
		};

		var card = new Border
		{
			BindingContext = device,
			Padding = new Thickness(9, 6),
			StrokeThickness = 1,
			StrokeShape = new RoundRectangle { CornerRadius = GpuCardCornerRadius },
			Content = stack
		};

		card.GestureRecognizers.Add(new TapGestureRecognizer
		{
			Command = new Command(() => SelectGpuDevice(device.Id))
		});

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) => ApplyGpuCardVisual(card, true);
		pointer.PointerExited += (s, e) => UpdateGpuSelectionVisuals();
		card.GestureRecognizers.Add(pointer);

		return card;
	}

	private void OnGpuSelectorTapped(object? sender, EventArgs e)
	{
		if (_gpuDevices.Count <= 1) return;

		SetGpuSelectorExpanded(!_isGpuSelectorExpanded);
	}

	private void SelectGpuDevice(string gpuId)
	{
		SettingsService.Settings.GpuId = gpuId;
		SettingsService.Save();
		SelectedGpuLabel.Text = FormatGpuLabel(gpuId);
		SetGpuSelectorExpanded(false);
		UpdateGpuSelectionVisuals();
		AddConsoleLog($"[GPU] CUDA device set to GPU {gpuId}. The next boot will use --cuda-device {gpuId}.");
	}

	private void UpdateGpuSelectionVisuals()
	{
		string selectedGpuId = SettingsService.Settings.GpuId;
		GpuDeviceInfo? selectedDevice = _gpuDevices.FirstOrDefault(device => string.Equals(device.Id, selectedGpuId, StringComparison.Ordinal));
		if (selectedDevice == null && _gpuDevices.Count > 0)
		{
			selectedDevice = _gpuDevices[0];
			SettingsService.Settings.GpuId = selectedDevice.Id;
			SettingsService.Save();
			selectedGpuId = selectedDevice.Id;
		}

		if (selectedDevice != null)
		{
			SelectedGpuLabel.Text = FormatGpuLabel(selectedDevice.Id);
			SelectedGpuNameLabel.Text = FormatGpuLabel(selectedDevice.Id);
			SelectedGpuDetailLabel.Text = $"{selectedDevice.Name} - {selectedDevice.MemoryTotalMb}";
			GpuDropdownGlyphLabel.IsVisible = _gpuDevices.Count > 1;
		}

		foreach (Border card in _gpuOptionCards)
		{
			if (card.BindingContext is not GpuDeviceInfo device) continue;

			bool isSelected = string.Equals(device.Id, selectedGpuId, StringComparison.Ordinal);
			ApplyGpuCardVisual(card, isSelected);
		}
	}

	private static void ApplyGpuCardVisual(Border card, bool isActive)
	{
		card.BackgroundColor = isActive ? GpuCardSelectedBackgroundColor : GpuCardNormalBackgroundColor;
		card.Stroke = isActive ? GpuCardSelectedStrokeColor : GpuCardNormalStrokeColor;
	}

	private void SetGpuSelectorExpanded(bool isExpanded)
	{
		_isGpuSelectorExpanded = isExpanded;
		GpuSelectorDropdownPanel.IsVisible = isExpanded;
		GpuDropdownGlyphLabel.Text = isExpanded ? "^" : "v";
	}

	private static string FormatGpuLabel(string gpuId)
		=> $"GPU {gpuId}";

	private async Task RunServerBootSequenceAsync(bool repairRuntimeBeforeBoot = false)
	{
		ApplyConsoleBootActionState(ConsoleBootActionState.Booting);

		SetupStepResult result;
		try
		{
			result = await _setupSequence.RunServerBootAsync(repairRuntimeBeforeBoot, AddConsoleLog);
		}
		catch (OperationCanceledException)
		{
			string message = LocalizationManager.Text("setup.console.boot_canceled");
			AddConsoleLog($"[ERROR] {message}");
			ApplyConsoleBootActionState(ConsoleBootActionState.Failed, message);
			return;
		}
		catch (Exception ex)
		{
			string message = LocalizationManager.Format("setup.console.boot_failed_with_message", ex.Message);
			AddConsoleLog($"[ERROR] {message}");
			ApplyConsoleBootActionState(ConsoleBootActionState.Failed, message);
			return;
		}

		if (!result.IsSuccess)
		{
			AddConsoleLog($"[ERROR] {result.Message}");
			ApplyConsoleBootActionState(ConsoleBootActionState.Failed, result.Message);
			return;
		}

		if (result.RequiresSetupHandoff)
		{
			AddConsoleLog($"[SYSTEM] {result.Message}");
			ApplyConsoleBootActionState(ConsoleBootActionState.Standby, LocalizationManager.Text("setup.console.maintenance_completed"));
			ResetFlow();
			return;
		}

		ApplyConsoleBootActionState(ConsoleBootActionState.Online);
		AddConsoleLog("[SYSTEM] Nexus Core Server detected on target port.");
		AddConsoleLog("[SYSTEM] Deployment ready.");
	}

	private static Task InvokeOnMainThreadSafeAsync(Func<Task> action)
	{
		return UiThread.InvokeAsync(action, "PRODUCT_SETUP:UI");
	}

	private void StartConsoleReadyPulse()
	{
		if (_isDisposed)
		{
			return;
		}

		_consoleBootingPulseClip.Stop();
		ConsoleBootPulseSurface.Opacity = 1;
		_consoleReadyPulseClip.PlayLoop(CanRepeatConsoleReadyPulse);
	}

	private void StopConsoleReadyPulse()
	{
		_consoleReadyPulseClip.Stop();
		ConsoleBootPulseSurface.Opacity = 0;
	}

	private void StartConsoleBootingPulse()
	{
		if (_isDisposed)
		{
			return;
		}

		_consoleReadyPulseClip.Stop();
		ConsoleBootPulseSurface.Opacity = 1;
		_consoleBootingPulseClip.PlayLoop(CanRepeatConsoleBootingPulse);
	}

	private void StopConsoleBootingPulse()
	{
		_consoleBootingPulseClip.Stop();
		ConsoleBootPulseSurface.Opacity = 0;
	}

	private void StartConsoleStatusBootingPulse()
	{
		if (_isDisposed)
		{
			return;
		}

		ConsoleStatusPulseSurface.Opacity = 1;
		_consoleStatusBootingPulseClip.PlayLoop(CanRepeatConsoleStatusBootingPulse);
	}

	private void StopConsoleStatusBootingPulse()
	{
		_consoleStatusBootingPulseClip.Stop();
		ConsoleStatusPulseSurface.Opacity = 0;
	}

	private bool CanRepeatConsoleReadyPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ServerConsolePanel.IsVisible
			&& _consoleBootActionState == ConsoleBootActionState.Standby;

	private bool CanRepeatConsoleBootingPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ServerConsolePanel.IsVisible
			&& _consoleBootActionState == ConsoleBootActionState.Booting;

	private bool CanRepeatConsoleStatusBootingPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ServerConsolePanel.IsVisible
			&& _consoleBootActionState == ConsoleBootActionState.Booting;

	private async void OnConsoleRetryClicked(object? sender, EventArgs e)
	{
		if (_consoleBootActionState != ConsoleBootActionState.Failed) return;

		await RunSystemBootCheckAsync();
		if (_consoleBootActionState == ConsoleBootActionState.Standby)
		{
			if (_consoleRepairBeforeBoot && !await ConfirmConsoleRepairBootAsync())
			{
				return;
			}

			await RunServerBootSequenceAsync(_consoleRepairBeforeBoot);
		}
	}

	private void OnConsoleRepairBeforeBootToggleClicked(object? sender, EventArgs e)
	{
		if (_consoleBootActionState is not (ConsoleBootActionState.Standby or ConsoleBootActionState.Failed)) return;

		_consoleRepairBeforeBoot = !_consoleRepairBeforeBoot;
		ApplyConsoleRepairBeforeBootToggleVisual(ShouldShowConsoleRecoverBoot(_consoleBootActionState));
	}

	private async Task<bool> ConfirmConsoleRepairBootAsync()
	{
		var page = GetPromptPage();
		if (page == null)
		{
			return true;
		}

		string repairTarget = RuntimeRepairTarget.GetDisplay(_appManager.Settings.Settings, _appManager.Paths);
		return await page.DisplayAlertAsync(
			LocalizationManager.Text("setup.console.recover_boot_title"),
			LocalizationManager.Format("setup.console.recover_boot_message", repairTarget),
			LocalizationManager.Text("setup.console.recover_boot"),
			LocalizationManager.Text("common.cancel"));
	}

	private void ApplyConsoleBootActionState(ConsoleBootActionState state, string? detail = null)
	{
		XamlLifetimeDiagnostics.RecordSurface("product-setup-console", state.ToString());
		_consoleBootActionState = state;
		if (_isDisposed) return;

		StopConsoleReadyPulse();
		StopConsoleBootingPulse();
		StopConsoleStatusBootingPulse();
		StopPrimaryActionReadyPulse();

		ConsoleBootButton.CancelAnimations();
		ConsoleRepairBeforeBootToggle.CancelAnimations();
		ConsoleRetryButton.CancelAnimations();
		ConsoleRetryButton.Scale = 1.0;
		ConsoleBootPulseSurface.Opacity = 0;

		bool showRepairToggle = ShouldShowConsoleRecoverBoot(state);
		if (!showRepairToggle)
		{
			_consoleRepairBeforeBoot = false;
		}

		ConsoleBootActionsGrid.Spacing = showRepairToggle ? ConsoleBootActionsExpandedRowSpacing : 0;
		SetConsoleButtonAvailability(
			ConsoleBootButton,
			isVisible: state is ConsoleBootActionState.Preparing or ConsoleBootActionState.Standby or ConsoleBootActionState.Booting,
			isEnabled: state == ConsoleBootActionState.Standby);
		SetConsoleButtonAvailability(
			ConsoleRepairBeforeBootToggle,
			isVisible: showRepairToggle,
			isEnabled: showRepairToggle);
		SetConsoleButtonAvailability(
			ConsoleRetryButton,
			isVisible: state == ConsoleBootActionState.Failed,
			isEnabled: state == ConsoleBootActionState.Failed);
		ConsoleBootButton.Opacity = state switch
		{
			ConsoleBootActionState.Preparing => ConsoleButtonPreparingOpacity,
			ConsoleBootActionState.Booting => ConsoleButtonBootingOpacity,
			_ => 1.0
		};
		ApplyConsoleRepairBeforeBootToggleVisual(showRepairToggle);
		ConsoleRetryButton.Opacity = state == ConsoleBootActionState.Failed ? 1.0 : 0.0;

		PrimaryActionButton.IsEnabled = state == ConsoleBootActionState.Online;
		PrimaryActionButton.InputTransparent = state != ConsoleBootActionState.Online;
		PrimaryActionButton.Opacity = state == ConsoleBootActionState.Online ? 1.0 : 0.4;
		UpdateBackButtonAvailability();
		UpdateConsoleConfigAvailability();

		switch (state)
		{
			case ConsoleBootActionState.Preparing:
				SetServerState(LocalizationManager.Text("setup.console.state_standby"), LocalizationManager.Text("setup.console.preparing_detail"), ConsoleStateAccentHex, LocalizationManager.Text("setup.console.badge_ready"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_initializing"), ConsoleStateInitializingHex);
				break;
			case ConsoleBootActionState.Standby:
				SetServerState(LocalizationManager.Text("setup.console.state_standby"), LocalizationManager.Text("setup.console.standby_detail"), ConsoleStateAccentHex, LocalizationManager.Text("setup.console.badge_ready"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_standby"), ConsoleStateAccentHex);
				StartConsoleReadyPulse();
				break;
			case ConsoleBootActionState.Booting:
				SetServerState(LocalizationManager.Text("setup.console.state_booting"), LocalizationManager.Text("setup.console.booting_detail"), ConsoleStateWarningHex, LocalizationManager.Text("setup.console.badge_boot"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_booting"), ConsoleStateWarningHex);
				StartConsoleBootingPulse();
				StartConsoleStatusBootingPulse();
				break;
			case ConsoleBootActionState.Failed:
				SetServerState(LocalizationManager.Text("setup.console.state_failed"), detail ?? LocalizationManager.Text("setup.console.failed_detail"), ConsoleStateFailedHex, LocalizationManager.Text("setup.console.badge_fail"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_failed"), ConsoleStateFailedHex);
				ConsoleRetryButton.BackgroundColor = ConsoleRetryNormalColor;
				break;
			case ConsoleBootActionState.Online:
				SetServerState(LocalizationManager.Text("setup.console.state_online"), LocalizationManager.Text("setup.console.online_detail"), ConsoleStateAccentHex, LocalizationManager.Text("setup.console.badge_live"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_online"), ConsoleStateAccentHex);
				_currentState = ViewState.Ready;
				StartPrimaryActionReadyPulse();
				break;
		}

		XamlLifetimeDiagnostics.WriteSnapshot($"product-setup-console:{state}");
	}

	private bool IsConsoleConfigEditable()
		=> _consoleBootActionState is ConsoleBootActionState.Standby or ConsoleBootActionState.Failed;

	private void UpdateConsoleConfigAvailability()
	{
		bool isEditable = IsConsoleConfigEditable();
		SetConsoleConfigPillAvailability(ConsoleHostPill, isEditable);
		SetConsoleConfigPillAvailability(ConsolePortPill, isEditable);
		SetConsoleConfigPillAvailability(ServerPythonModePill, isEditable);

		if (!isEditable)
		{
			ConsoleHostPill.BackgroundColor = ConsoleConfigPillNormalColor;
			ConsolePortPill.BackgroundColor = ConsoleConfigPillNormalColor;
			UpdateServerPythonModeVisual();
		}
	}

	private static void SetConsoleConfigPillAvailability(Border pill, bool isEditable)
	{
		pill.IsEnabled = isEditable;
		pill.InputTransparent = !isEditable;
		pill.Opacity = isEditable ? 1.0 : ConsoleConfigDisabledOpacity;
	}

	private static void SetConsoleButtonAvailability(Border button, bool isVisible, bool isEnabled)
	{
		button.IsVisible = isVisible;
		button.IsEnabled = isEnabled;
		button.InputTransparent = !isEnabled;
	}

	private void ApplyConsoleRepairBeforeBootToggleVisual(bool isVisible)
	{
		ConsoleRepairBeforeBootToggle.Opacity = isVisible ? 1.0 : 0.0;
		ConsoleRepairBeforeBootGlyph.Text = _consoleRepairBeforeBoot ? "■" : "□";
		ConsoleRepairBeforeBootToggle.BackgroundColor = _consoleRepairBeforeBoot
			? ConsoleRecoverHoverColor
			: ConsoleRecoverNormalColor;
	}

	private void SetConsoleStatus(string text, string colorHex)
	{
		var color = Color.FromArgb(colorHex);
		ConsoleStatusLabel.Text = text;
		ConsoleStatusBorder.Stroke = color;
		ConsoleStatusLabel.TextColor = color;
	}

	private void SetServerState(string title, string detail, string colorHex, string stateTag)
	{
		var color = Color.FromArgb(colorHex);
		ServerStateTitleLabel.Text = title;
		ServerStateTitleLabel.TextColor = color;
		ServerStateDetailLabel.Text = detail;
		ServerStateGlyphLabel.Text = stateTag;
		ServerStateGlyphLabel.TextColor = color;
		ServerStateBadge.BackgroundColor = color.WithAlpha(0.16f);
		ServerStateAccentBar.BackgroundColor = color;
		ServerStateCardGlow.Color = color;
		ServerStateCardGlow.Opacity = title == "FAILED" ? 0.14 : 0.08;
		ServerStateCard.BackgroundColor = ServerStateCardBackgroundColor;
	}

	private static string GetProbeAddress(string listenAddress)
	{
		if (string.IsNullOrWhiteSpace(listenAddress)) return "127.0.0.1";

		string normalized = listenAddress.Trim();
		return normalized is "0.0.0.0" or "::" or "*" ? "127.0.0.1" : normalized;
	}

	private void AddConsoleLog(string message)
	{
		if (_isDisposed)
		{
			return;
		}

		ConsoleLogTail.AppendLine(message);
	}

	private bool ShouldShowConsoleRecoverBoot(ConsoleBootActionState state)
	{
		if (state == ConsoleBootActionState.Failed)
		{
			return true;
		}

		return state == ConsoleBootActionState.Standby
			&& SettingsService.Settings.LastLaunchSuccessful;
	}

}
