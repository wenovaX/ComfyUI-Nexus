namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Diagnostics;
using ComfyUI_Nexus.Setup.Diagnostics.Nodes;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;

public partial class SettingsOverlayView
{	private void OnUseSystemGitClicked(object? sender, EventArgs e)
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

	private async void OnUseSystemPythonClicked(object? sender, EventArgs e)
	{
		const string operationId = "use-system-python";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		try
		{
			PythonRuntimeCapability capability = await PythonRuntimeCapabilityProbe.ProbeAsync("python", CancellationToken.None);
			if (!capability.IsReady)
			{
				await ShowValidationAlertAsync(GetPythonCapabilityFailureMessage(capability));
				return;
			}

			_editor.Draft.PythonSource = DiagnosticNodeHelpers.SystemOption;
			_editor.Draft.PythonPath = capability.ExecutablePath;
			UpdateToolButtons();
			StartToolVersionProbe();
			UpdateStateChrome();
		}
		finally
		{
			EndOperation(operationId);
		}
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
			var result = await NexusAppManager.Instance.Platform.FilePicker.PickFolderAsync(
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
			var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(path);
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
		bool confirmed = await _appManager.Dialogs.ConfirmAsync(
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
			var result = await NexusAppManager.Instance.Platform.FilePicker.PickFileAsync(title, [".exe"]);
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
				PythonRuntimeCapability capability = await PythonRuntimeCapabilityProbe.ProbeAsync(result.Value, CancellationToken.None);
				if (!capability.IsReady)
				{
					await ShowValidationAlertAsync(GetPythonCapabilityFailureMessage(capability));
					return;
				}

				_editor.Draft.PythonSource = DiagnosticNodeHelpers.CustomOption;
				_editor.Draft.PythonPath = capability.ExecutablePath;
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

	private static string GetPythonCapabilityFailureMessage(PythonRuntimeCapability capability)
		=> capability.Status switch
		{
			PythonRuntimeCapabilityStatus.PipUnavailable => LocalizationManager.Text("setup.python.selected_pip_unavailable"),
			PythonRuntimeCapabilityStatus.UnsupportedHostEnvironment => LocalizationManager.Text("setup.python.selected_unsupported_environment"),
			_ => LocalizationManager.Text("setup.python.selected_unavailable")
		};

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

}
