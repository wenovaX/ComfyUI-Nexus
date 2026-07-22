namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

internal sealed class ComfyCoreDiagnosticNode : IConfigurableDiagnosticNode
{
	private readonly ComfyInstallService _comfyInstall;

	internal ComfyCoreDiagnosticNode(ComfyInstallService comfyInstall)
	{
		_comfyInstall = comfyInstall ?? throw new ArgumentNullException(nameof(comfyInstall));
	}

	public string NodeId => "comfy-core";
	public string DisplayName => Text("setup.comfy_core.title");
	public string Description => Text("setup.comfy_core.description");
	public string EnvironmentDetails { get; private set; } = Text("setup.common.pending_detail");
	public string EnvironmentPath { get; private set; } = string.Empty;
	public bool IsCritical => true;

	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = Array.Empty<DiagnosticOption>();
	public string SelectedOptionId { get; private set; } = string.Empty;

	public Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		string mainPy = Path.Combine(_comfyInstall.Paths.ActiveComfyPath, "main.py");
		string venvPython = _comfyInstall.Paths.ActiveVenvPythonExe;

		if (File.Exists(mainPy) && File.Exists(venvPython))
		{
			return Task.FromResult(HealthState.Healthy);
		}

		return Task.FromResult(HealthState.NeedsRecovery);
	}

	public async Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		var health = await CheckHealthAsync(cancellationToken);
		if (health == HealthState.Healthy)
		{
			var (ver, rev) = await GetComfyVersionInfoAsync();
			EnvironmentPath = _comfyInstall.Paths.ActiveComfyPath;
			EnvironmentDetails = $"ComfyUI {ver} ({rev})";
			AvailableOptions = new[]
			{
				DiagnosticNodeHelpers.CreateOption(
					DiagnosticNodeHelpers.KeepOption,
					Text("setup.common.option_keep_next"),
					isRecommended: true,
					requiresRecovery: false)
			};
		}
		else
		{
			EnvironmentDetails = Text("setup.comfy_core.ready_to_install");
			AvailableOptions = new[]
			{
				DiagnosticNodeHelpers.CreateOption(
					ComfyCoreSources.RemoteLatest,
					Text("setup.comfy_core.option_remote_latest"),
					Text("setup.comfy_core.option_remote_latest_description"),
					isRecommended: true,
					workingHint: Text("setup.comfy_core.work_hint_remote_latest"),
					requiresToolingLease: true),
				DiagnosticNodeHelpers.CreateOption(
					ComfyCoreSources.BuiltIn,
					Text("setup.comfy_core.option_builtin_source"),
					Text("setup.comfy_core.option_builtin_source_description"),
					workingHint: Text("setup.comfy_core.work_hint_builtin_source"),
					requiresToolingLease: true)
			};
		}
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
		var settings = _comfyInstall.SettingsService.Settings;

		if (optionId == ComfyCoreSources.RemoteLatest)
		{
			settings.ComfyPath = string.Empty; // Will fallback to DefaultComfyPath
			settings.ComfyCoreSource = ComfyCoreSources.RemoteLatest;
			EnvironmentDetails = Text("setup.comfy_core.using_remote_latest");
		}
		else if (optionId == ComfyCoreSources.BuiltIn)
		{
			settings.ComfyPath = string.Empty;
			settings.ComfyCoreSource = ComfyCoreSources.BuiltIn;
			EnvironmentDetails = Text("setup.comfy_core.using_builtin_source");
		}
	}

	private async Task<(string Version, string Revision)> GetComfyVersionInfoAsync()
	{
		string comfyPath = _comfyInstall.Paths.ActiveComfyPath;
		string versionFile = Path.Combine(comfyPath, "comfyui_version.py");
		string version = "unknown";
		string rev = "unknown";

		if (File.Exists(versionFile))
		{
			string content = await File.ReadAllTextAsync(versionFile);
			var versionMatch = System.Text.RegularExpressions.Regex.Match(content, "__version__\\s*=\\s*\"([^\"]+)\"");
			var revMatch = System.Text.RegularExpressions.Regex.Match(content, "__git_commit__\\s*=\\s*\"([^\"]+)\"");
			if (versionMatch.Success) version = versionMatch.Groups[1].Value;
			if (revMatch.Success && revMatch.Groups[1].Value != "unknown")
				rev = revMatch.Groups[1].Value.Substring(0, Math.Min(7, revMatch.Groups[1].Value.Length));
		}

		if (rev == "unknown" && Directory.Exists(Path.Combine(comfyPath, ".git")))
		{
			string gitExe = _comfyInstall.SettingsService.Settings.GitPath;
			if (string.IsNullOrWhiteSpace(gitExe)) gitExe = "git";

			rev = await DiagnosticNodeHelpers.TryGetGitRevisionAsync(comfyPath, gitExe, CancellationToken.None);
		}

		return (version, rev);
	}

	public async Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		try
		{
			progress?.Report(0.1);
			var result = await _comfyInstall.InstallCoreAsync(cancellationToken);
			progress?.Report(0.6);
			// After install, read version info
			var versionInfo = await GetComfyVersionInfoAsync();
			EnvironmentPath = _comfyInstall.Paths.ActiveComfyPath;
			EnvironmentDetails = $"ComfyUI {versionInfo.Version} ({versionInfo.Revision})";
			progress?.Report(1.0);
			return new RecoveryResult(result.IsSuccess, result.Message);
		}
		catch (Exception ex)
		{
			return new RecoveryResult(false, LocalizationManager.Format("setup.comfy_core.recover_failed", ex.Message));
		}
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);
}
