namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

internal sealed class PythonEnvironmentDiagnosticNode : IOptionalConfigurableDiagnosticNode
{
	private const string CreateOption = "create-venv";
	private const string ResetOption = "reset-venv";
	private const string KeepOption = "keep-venv";

	public string NodeId => "python-environment";
	public string DisplayName => Text("setup.venv.title");
	public string Description => Text("setup.venv.description");
	public bool IsCritical => false;

	public string EnvironmentDetails { get; private set; } = Text("setup.venv.checking");
	public string EnvironmentPath { get; private set; } = ComfyInstallService.ComfyVenvPath;
	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = Array.Empty<DiagnosticOption>();
	public string SelectedOptionId { get; private set; } = CreateOption;

	public async Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		await CheckHealthAsync(cancellationToken);
		RefreshOptions();
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
		var settings = SetupSettingsService.Instance.Settings;

		if (optionId is CreateOption or ResetOption or KeepOption)
		{
			settings.ServerPythonMode = PythonExecutionModes.Venv;
			EnvironmentDetails = optionId == KeepOption
				? Text("setup.venv.keep_selected")
				: Text("setup.venv.mode_selected");
		}

		SetupSettingsService.Instance.Save();
	}

	public Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var settings = SetupSettingsService.Instance.Settings;
		bool useVenv = RuntimePythonModePresenter.ShouldDisplayVenvMode(settings, includeActiveLaunchSnapshot: false);
		bool hasVenvDirectory = Directory.Exists(ComfyInstallService.ComfyVenvPath);
		bool hasVenvPython = File.Exists(ComfyInstallService.ComfyVenvPythonExe);

		EnvironmentPath = hasVenvDirectory ? ComfyInstallService.ComfyVenvPath : ComfyInstallService.ComfyPath;

		if (!useVenv)
		{
			string python = string.IsNullOrWhiteSpace(settings.PythonPath) ? "python" : settings.PythonPath;
			string venvStatus = hasVenvPython
				? Text("setup.venv.direct_existing")
				: hasVenvDirectory
					? Text("setup.venv.direct_incomplete")
					: Text("setup.venv.direct_missing");
			EnvironmentDetails = $"{LocalizationManager.Format("setup.venv.direct_active", python)}{Environment.NewLine}{venvStatus}";
			RefreshOptions();
			return Task.FromResult(HealthState.Healthy);
		}

		if (hasVenvPython)
		{
			EnvironmentDetails = Text("setup.venv.ready");
			RefreshOptions();
			return Task.FromResult(HealthState.Healthy);
		}

		EnvironmentDetails = hasVenvDirectory
			? Text("setup.venv.incomplete")
			: Text("setup.venv.missing");

		RefreshOptions();
		return Task.FromResult(HealthState.OptionalMissing);
	}

	public async Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		try
		{
			progress?.Report(0.1);

			if (SelectedOptionId == ResetOption)
			{
				var result = await ComfyInstallService.Instance.RebuildComfyVenvOnlyAsync(cancellationToken);
				progress?.Report(result.IsSuccess ? 1.0 : 0.0);
				return new RecoveryResult(result.IsSuccess, result.Message);
			}

			if (SelectedOptionId == KeepOption)
			{
				SetupSettingsService.Instance.Settings.ServerPythonMode = PythonExecutionModes.Venv;
				SetupSettingsService.Instance.Save();
				progress?.Report(1.0);
				return new RecoveryResult(true, Text("setup.venv.keep_selected"));
			}

			if (SelectedOptionId == CreateOption)
			{
				var result = await ComfyInstallService.Instance.EnsureComfyVenvOnlyAsync(cancellationToken);
				progress?.Report(result.IsSuccess ? 1.0 : 0.0);
				return new RecoveryResult(result.IsSuccess, result.Message);
			}

			progress?.Report(1.0);
			return new RecoveryResult(true, Text("setup.venv.launch_mode_updated"));
		}
		catch (Exception ex)
		{
			return new RecoveryResult(false, LocalizationManager.Format("setup.venv.setup_failed", ex.Message));
		}
	}

	private void RefreshOptions()
	{
		bool hasVenvDirectory = Directory.Exists(ComfyInstallService.ComfyVenvPath);
		bool hasVenvPython = File.Exists(ComfyInstallService.ComfyVenvPythonExe);

		var options = new List<DiagnosticOption>();

		if (!hasVenvDirectory)
		{
			options.Add(DiagnosticNodeHelpers.CreateOption(CreateOption, Text("setup.venv.option_create"), Text("setup.venv.option_create_description"), isRecommended: true));
		}
		else if (hasVenvPython)
		{
			options.Add(DiagnosticNodeHelpers.CreateOption(ResetOption, Text("setup.venv.option_reset"), Text("setup.venv.option_reset_description"), isRecommended: true));
			options.Add(DiagnosticNodeHelpers.CreateOption(KeepOption, Text("setup.venv.option_keep"), Text("setup.venv.option_keep_description")));
		}
		else
		{
			options.Add(DiagnosticNodeHelpers.CreateOption(ResetOption, Text("setup.venv.option_reset"), Text("setup.venv.option_reset_incomplete_description"), isRecommended: true));
		}

		AvailableOptions = options;
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);
}
