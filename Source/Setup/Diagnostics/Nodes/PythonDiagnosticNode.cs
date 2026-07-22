namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Services;

internal sealed class PythonDiagnosticNode : IExecutableSelectionDiagnosticNode
{
	private readonly ComfyInstallService _comfyInstall;

	internal PythonDiagnosticNode(ComfyInstallService comfyInstall)
	{
		_comfyInstall = comfyInstall ?? throw new ArgumentNullException(nameof(comfyInstall));
	}

	public string NodeId => "python-engine";
	public string DisplayName => Text("setup.python.title");
	public string Description => Text("setup.python.description");
	public bool IsCritical => true;

	public string EnvironmentDetails { get; private set; } = Text("setup.common.probing");
	public string EnvironmentPath { get; private set; } = string.Empty;
	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = Array.Empty<DiagnosticOption>();
	public string SelectedOptionId { get; private set; } = string.Empty;
	private PythonRuntimeCapability _systemCapability = PythonRuntimeCapability.Unavailable;

	public async Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		_systemCapability = await PythonRuntimeCapabilityProbe.ProbeAsync("python", cancellationToken);

		var options = new List<DiagnosticOption>();

		if (_systemCapability.IsReady)
		{
			EnvironmentDetails = LocalizationManager.Format("setup.python.system_detected", _systemCapability.Version);
			options.Add(DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.SystemOption, Text("setup.python.option_system"), LocalizationManager.Format("setup.python.option_system_description", _systemCapability.Version), requiresRecovery: false));
			options.Add(DiagnosticNodeHelpers.CreateOption(
				DiagnosticNodeHelpers.BuiltInOption,
				Text("setup.common.option_install_builtin"),
				Text("setup.python.option_builtin_description"),
				isRecommended: true,
				workingHint: Text("setup.python.work_hint_builtin"),
				requiresToolingLease: true));
		}
		else
		{
			EnvironmentDetails = GetCapabilityFailureDetails(_systemCapability, systemPython: true);
			options.Add(DiagnosticNodeHelpers.CreateOption(
				DiagnosticNodeHelpers.BuiltInOption,
				Text("setup.common.option_install_builtin"),
				Text("setup.python.option_builtin_description"),
				isRecommended: true,
				workingHint: Text("setup.python.work_hint_builtin"),
				requiresToolingLease: true));
		}

		options.Add(DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.CustomOption, Text("setup.common.option_manual_selection"), Text("setup.python.option_manual_description"), requiresRecovery: false));

		AvailableOptions = options;
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
		var settings = _comfyInstall.SettingsService.Settings;
		settings.PythonSource = optionId;

		if (optionId == DiagnosticNodeHelpers.SystemOption)
		{
			settings.PythonPath = _systemCapability.ExecutablePath;
			EnvironmentPath = _systemCapability.ExecutablePath;
			EnvironmentDetails = LocalizationManager.Format("setup.python.using_system", _systemCapability.Version);
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

	public bool RequiresExecutableSelection(string optionId)
		=> optionId == DiagnosticNodeHelpers.CustomOption;

	public async Task<RecoveryResult> ApplySelectedExecutableAsync(string optionId, string executablePath, CancellationToken cancellationToken)
	{
		if (!RequiresExecutableSelection(optionId))
		{
			return new RecoveryResult(false, "The selected option does not accept an executable path.");
		}

		PythonRuntimeCapability capability = await PythonRuntimeCapabilityProbe.ProbeAsync(executablePath, cancellationToken);
		if (!capability.IsReady)
		{
			EnvironmentDetails = GetCapabilityFailureDetails(capability, systemPython: false);
			return new RecoveryResult(false, EnvironmentDetails);
		}

		var settings = _comfyInstall.SettingsService.Settings;
		settings.PythonPath = executablePath;
		_comfyInstall.SettingsService.Save();
		EnvironmentPath = executablePath;
		EnvironmentDetails = LocalizationManager.Format("setup.python.using_custom", executablePath);
		return new RecoveryResult(true, EnvironmentDetails);
	}

	public async Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(SelectedOptionId)) return HealthState.NeedsRecovery;

		var settings = _comfyInstall.SettingsService.Settings;
		string pathToCheck = settings.PythonPath;

		if (string.IsNullOrEmpty(pathToCheck)) return HealthState.NeedsRecovery;

		PythonRuntimeCapability capability = await PythonRuntimeCapabilityProbe.ProbeAsync(pathToCheck, cancellationToken);
		if (capability.IsReady)
		{
			if (SelectedOptionId == DiagnosticNodeHelpers.CustomOption) EnvironmentDetails = LocalizationManager.Format("setup.python.using_custom", pathToCheck);
			return HealthState.Healthy;
		}

		EnvironmentDetails = GetCapabilityFailureDetails(capability, systemPython: false);

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
			progress?.Report(0.1);
			await _comfyInstall.ExtractPythonPackageAsync(cancellationToken);
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

	private static string GetCapabilityFailureDetails(PythonRuntimeCapability capability, bool systemPython)
		=> capability.Status switch
		{
			PythonRuntimeCapabilityStatus.PipUnavailable => Text(systemPython
				? "setup.python.system_pip_unavailable"
				: "setup.python.selected_pip_unavailable"),
			PythonRuntimeCapabilityStatus.UnsupportedHostEnvironment => Text(systemPython
				? "setup.python.system_unsupported_environment"
				: "setup.python.selected_unsupported_environment"),
			_ => Text(systemPython
				? "setup.python.system_missing"
				: "setup.python.selected_unavailable")
		};
}
