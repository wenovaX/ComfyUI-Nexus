namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Services;

internal sealed class PythonDiagnosticNode : IConfigurableDiagnosticNode
{
	public string NodeId => "python-engine";
	public string DisplayName => Text("setup.python.title");
	public string Description => Text("setup.python.description");
	public bool IsCritical => true;

	public string EnvironmentDetails { get; private set; } = Text("setup.common.probing");
	public string EnvironmentPath { get; private set; } = string.Empty;
	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = Array.Empty<DiagnosticOption>();
	public string SelectedOptionId { get; private set; } = "builtin";
	private string? _detectedVersion;

	public async Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		string? systemPythonVer = await DiagnosticNodeHelpers.TryGetCommandVersionAsync("python", "--version", "Python ", cancellationToken);
		_detectedVersion = systemPythonVer;

		var options = new List<DiagnosticOption>();

		if (systemPythonVer != null)
		{
			EnvironmentDetails = LocalizationManager.Format("setup.python.system_detected", systemPythonVer);
			options.Add(DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.SystemOption, Text("setup.python.option_system"), LocalizationManager.Format("setup.python.option_system_description", systemPythonVer)));
			options.Add(DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.BuiltInOption, Text("setup.common.option_install_builtin"), Text("setup.python.option_builtin_description"), isRecommended: true));
		}
		else
		{
			EnvironmentDetails = Text("setup.python.system_missing");
			options.Add(DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.BuiltInOption, Text("setup.common.option_install_builtin"), Text("setup.python.option_builtin_description"), isRecommended: true));
		}

		options.Add(DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.CustomOption, Text("setup.common.option_manual_selection"), Text("setup.python.option_manual_description")));

		AvailableOptions = options;
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
		var settings = SetupSettingsService.Instance.Settings;
		settings.PythonSource = optionId;

		if (optionId == DiagnosticNodeHelpers.SystemOption)
		{
			settings.PythonPath = "python";
			EnvironmentDetails = LocalizationManager.Format("setup.python.using_system", _detectedVersion ?? "unknown");
		}
		else if (optionId == DiagnosticNodeHelpers.BuiltInOption)
		{
			settings.PythonPath = ComfyInstallService.PythonExe;
			EnvironmentDetails = LocalizationManager.Format("setup.python.using_builtin", DiagnosticNodeHelpers.ParsePackageVersion(RuntimePackageSpecService.Load().Python.File));
		}
		else if (optionId == DiagnosticNodeHelpers.CustomOption)
		{
			EnvironmentDetails = Text("setup.python.custom_selected");
		}
	}

	public async Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		var settings = SetupSettingsService.Instance.Settings;
		string pathToCheck = SelectedOptionId == DiagnosticNodeHelpers.SystemOption ? "python" : settings.PythonPath;

		if (string.IsNullOrEmpty(pathToCheck)) return HealthState.NeedsRecovery;

		string? version = await DiagnosticNodeHelpers.TryGetCommandVersionAsync(pathToCheck, "--version", "Python ", cancellationToken);
		if (version != null)
		{
			if (SelectedOptionId == DiagnosticNodeHelpers.CustomOption) EnvironmentDetails = LocalizationManager.Format("setup.python.using_custom", pathToCheck);
			return HealthState.Healthy;
		}

		return HealthState.NeedsRecovery;
	}

	public async Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		if (SelectedOptionId == DiagnosticNodeHelpers.SystemOption)
		{
			return new RecoveryResult(false, Text("setup.python.system_unavailable"));
		}

		try
		{
			if (ComfyInstallService.Instance == null)
			{
				return new RecoveryResult(false, Text("setup.common.install_service_missing"));
			}
			progress?.Report(0.1);
			await ComfyInstallService.Instance.ExtractPythonPackageAsync(cancellationToken);
			progress?.Report(1.0);
			return new RecoveryResult(true, Text("setup.python.install_success"));
		}
		catch (Exception ex)
		{
			return new RecoveryResult(false, LocalizationManager.Format("setup.python.install_failed", ex.Message));
		}
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);
}
