namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Services;

internal sealed class GitDiagnosticNode : IExecutableSelectionDiagnosticNode
{
	private readonly ComfyInstallService _comfyInstall;

	internal GitDiagnosticNode(ComfyInstallService comfyInstall)
	{
		_comfyInstall = comfyInstall ?? throw new ArgumentNullException(nameof(comfyInstall));
	}

	public string NodeId => "git-core";
	public string DisplayName => Text("setup.git.title");
	public string Description => Text("setup.git.description");
	public bool IsCritical => true;

	public string EnvironmentDetails { get; private set; } = Text("setup.common.probing");
	public string EnvironmentPath { get; private set; } = string.Empty;
	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = Array.Empty<DiagnosticOption>();
	public string SelectedOptionId { get; private set; } = string.Empty;
	private string? _detectedVersion;

	public async Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		string? systemGitVer = await DiagnosticNodeHelpers.TryGetCommandVersionAsync("git", "--version", "git version ", cancellationToken);
		_detectedVersion = systemGitVer;

		var options = new List<DiagnosticOption>();

		if (systemGitVer != null)
		{
			EnvironmentDetails = LocalizationManager.Format("setup.git.system_detected", systemGitVer);
			options.Add(DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.SystemOption, Text("setup.git.option_system"), LocalizationManager.Format("setup.git.option_system_description", systemGitVer), isRecommended: true, requiresRecovery: false));
			options.Add(DiagnosticNodeHelpers.CreateOption(
				DiagnosticNodeHelpers.BuiltInOption,
				Text("setup.common.option_install_builtin"),
				Text("setup.git.option_builtin_description"),
				workingHint: Text("setup.git.work_hint_builtin"),
				requiresToolingLease: true));
		}
		else
		{
			EnvironmentDetails = Text("setup.git.system_missing");
			options.Add(DiagnosticNodeHelpers.CreateOption(
				DiagnosticNodeHelpers.BuiltInOption,
				Text("setup.common.option_install_builtin"),
				Text("setup.git.option_builtin_description"),
				isRecommended: true,
				workingHint: Text("setup.git.work_hint_builtin"),
				requiresToolingLease: true));
		}

		options.Add(DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.CustomOption, Text("setup.common.option_manual_selection"), Text("setup.git.option_manual_description"), requiresRecovery: false));

		AvailableOptions = options;
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
		var settings = _comfyInstall.SettingsService.Settings;
		settings.GitSource = optionId;

		if (optionId == DiagnosticNodeHelpers.SystemOption)
		{
			settings.GitPath = "git";
			EnvironmentDetails = LocalizationManager.Format("setup.git.using_system", _detectedVersion ?? "unknown");
		}
		else if (optionId == DiagnosticNodeHelpers.BuiltInOption)
		{
			settings.GitPath = Path.Combine(ComfyInstallService.InstalledPath, "Git", "cmd", "git.exe");
			EnvironmentDetails = LocalizationManager.Format("setup.git.using_builtin", DiagnosticNodeHelpers.ParsePackageVersion(RuntimePackageSpecService.Load().Git.File));
		}
		else if (optionId == DiagnosticNodeHelpers.CustomOption)
		{
			EnvironmentDetails = Text("setup.git.custom_selected");
		}
	}

	public bool RequiresExecutableSelection(string optionId)
		=> optionId == DiagnosticNodeHelpers.CustomOption;

	public Task<RecoveryResult> ApplySelectedExecutableAsync(string optionId, string executablePath, CancellationToken cancellationToken)
	{
		if (!RequiresExecutableSelection(optionId))
		{
			return Task.FromResult(new RecoveryResult(false, "The selected option does not accept an executable path."));
		}

		var settings = _comfyInstall.SettingsService.Settings;
		settings.GitPath = executablePath;
		_comfyInstall.SettingsService.Save();
		EnvironmentPath = executablePath;
		EnvironmentDetails = Text("setup.git.custom_selected");
		return Task.FromResult(new RecoveryResult(true, EnvironmentDetails));
	}

	public async Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(SelectedOptionId)) return HealthState.NeedsRecovery;

		var settings = _comfyInstall.SettingsService.Settings;
		string pathToCheck = SelectedOptionId == DiagnosticNodeHelpers.SystemOption ? "git" : settings.GitPath;

		if (string.IsNullOrEmpty(pathToCheck)) return HealthState.NeedsRecovery;

		string? version = await DiagnosticNodeHelpers.TryGetCommandVersionAsync(pathToCheck, "--version", "git version ", cancellationToken);
		if (version != null)
		{
			if (SelectedOptionId == DiagnosticNodeHelpers.CustomOption) EnvironmentDetails = LocalizationManager.Format("setup.git.using_custom", pathToCheck);
			return HealthState.Healthy;
		}

		return HealthState.NeedsRecovery;
	}

	public async Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		if (SelectedOptionId == DiagnosticNodeHelpers.SystemOption)
		{
			return new RecoveryResult(false, Text("setup.git.system_unavailable"));
		}

		try
		{
			progress?.Report(0.1);
			await _comfyInstall.ExtractGitPackageAsync(cancellationToken);
			progress?.Report(1.0);
			return new RecoveryResult(true, Text("setup.git.extract_success"));
		}
		catch (Exception ex)
		{
			return new RecoveryResult(false, LocalizationManager.Format("setup.git.extract_failed", ex.Message));
		}
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);
}
