namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Diagnostics;
using ComfyUI_Nexus.Setup.Diagnostics.Nodes;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

public partial class SettingsOverlayView
{
	private void UpdateComfyModeButtons()
	{
		bool isLocal = string.Equals(_editor.Draft.InstallMode, SetupInstallModes.LocalRuntime, StringComparison.Ordinal);
		bool useRemoteCore = string.Equals(_editor.Draft.ComfyCoreSource, ComfyCoreSources.RemoteLatest, StringComparison.Ordinal);
		bool useBuiltInCore = string.Equals(_editor.Draft.ComfyCoreSource, ComfyCoreSources.BuiltIn, StringComparison.Ordinal);
		ApplyChipState(UseLocalRuntimeButton, isLocal);
		ApplyChipState(UseCustomComfyButton, !isLocal);
		ApplyChipState(UseRemoteComfyCoreButton, useRemoteCore);
		ApplyChipState(UseBuiltInComfyCoreButton, useBuiltInCore);
		InstallModeValueLabel.Text = GetInstallModeDisplay(_editor.Draft);
		ComfyCoreSourceValueLabel.Text = isLocal
			? useBuiltInCore
				? LocalizationManager.Text("views.overlays.settings_overlay_view.comfy_core_source_builtin_detail")
				: LocalizationManager.Text("views.overlays.settings_overlay_view.comfy_core_source_remote_detail")
			: LocalizationManager.Text("views.overlays.settings_overlay_view.comfy_core_source_external_detail");
		ComfyPathValueLabel.Text = GetEffectiveComfyPath(_editor.Draft);
		UseRemoteComfyCoreButton.IsEnabled = isLocal && !_isComfyActionBusy;
		UseBuiltInComfyCoreButton.IsEnabled = isLocal && !_isComfyActionBusy;
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
		string venvPath = _appManager.Paths.ActiveVenvPath;
		string venvPythonExe = _appManager.Paths.ActiveVenvPythonExe;
		bool hasVenvDirectory = Directory.Exists(venvPath);
		bool hasVenvPython = File.Exists(venvPythonExe);
		bool useVenv = string.Equals(_editor.Draft.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal);
		bool pendingCreate = HasDraftBootTask(PendingBootTaskIds.VenvCreate);
		bool pendingRebuild = HasDraftBootTask(PendingBootTaskIds.VenvRebuild);
		bool pendingDelete = !useVenv
			&& (RuntimePythonModePresenter.HasPendingVenvDelete(_editor.Draft)
				|| HasDraftBootTask(PendingBootTaskIds.VenvDelete));

		if (pendingCreate || pendingRebuild)
		{
			VenvStateValueLabel.Text = pendingCreate ? ".venv create scheduled" : ".venv rebuild scheduled";
			VenvPathValueLabel.Text = venvPath;
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
			VenvPathValueLabel.Text = venvPath;
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
		VenvPathValueLabel.Text = hasVenvDirectory ? venvPath : $"Target: {venvPath}";
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

	private void RefreshGpuOptionsFromKnownDevices()
	{
		_gpuDevices.Clear();
		_gpuDevices.AddRange(_appManager.GpuDiscovery.GetCachedDevicesOrFallback());
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

	private string GetCustomNodesPath()
	{
		string comfyPath = _appManager.Paths.ConfiguredComfyPath;
		return string.IsNullOrWhiteSpace(comfyPath)
			? string.Empty
			: System.IO.Path.Combine(comfyPath, "custom_nodes");
	}

}
