namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Services;

internal sealed class BaseResourceDiagnosticNode : IConfigurableDiagnosticNode
{
	private readonly ComfyInstallService _comfyInstall;

	internal BaseResourceDiagnosticNode(ComfyInstallService comfyInstall)
	{
		_comfyInstall = comfyInstall ?? throw new ArgumentNullException(nameof(comfyInstall));
	}

	private const string DownloadOption = "download";
	private const string BrowserOption = "browser";
	private const string LaterOption = "later";

	public string NodeId => "base-resources";
	public string DisplayName => Text("setup.base_model.title");
	public string Description => Text("setup.base_model.description");
	public string EnvironmentDetails { get; private set; } = Text("setup.common.pending_detail");
	public string EnvironmentPath { get; private set; } = string.Empty;
	public string SecondaryEnvironmentPath { get; private set; } = string.Empty;
	public bool IsCritical => false;

	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = Array.Empty<DiagnosticOption>();
	public string SelectedOptionId { get; private set; } = string.Empty;

	public Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		// Browser and later choices are explicit setup decisions. Do not let a stale or partial
		// local checkpoint trigger a remote-size probe before the sequence can advance.
		if (SelectedOptionId == BrowserOption)
		{
			return Task.FromResult(HealthState.Healthy);
		}

		if (SelectedOptionId == LaterOption)
		{
			return Task.FromResult(HealthState.OptionalMissing);
		}

		string fileName = _comfyInstall.SettingsService.Settings.DefaultModelFileName;
		string targetPath = Path.Combine(_comfyInstall.Paths.ActiveModelsRootPath, "checkpoints", fileName);

		// Diagnostic rendering must remain local-only. DownloadService validates and resumes
		// staged files when a managed download is explicitly requested.
		bool hasUsableLocalModel = File.Exists(targetPath)
			&& new FileInfo(targetPath).Length > 1024 * 1024 * 100;
		return Task.FromResult(hasUsableLocalModel ? HealthState.Healthy : HealthState.NeedsRecovery);
	}

	public async Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		if (SelectedOptionId is BrowserOption or LaterOption)
		{
			EnvironmentPath = _comfyInstall.SettingsService.Settings.DefaultModelUrl;
			SecondaryEnvironmentPath = GetCheckpointsDirectory();
			EnvironmentDetails = SelectedOptionId == BrowserOption
				? Text("setup.base_model.browser_download_requested")
				: Text("setup.base_model.skipped");
			AvailableOptions = new[]
			{
				DiagnosticNodeHelpers.CreateOption(
					DiagnosticNodeHelpers.KeepOption,
					Text("setup.common.option_keep_next"),
					isRecommended: true,
					requiresRecovery: false)
			};
			return;
		}

		SecondaryEnvironmentPath = string.Empty;
		var health = await CheckHealthAsync(cancellationToken);
		if (health == HealthState.Healthy)
		{
			string fileName = _comfyInstall.SettingsService.Settings.DefaultModelFileName;
			string targetPath = Path.Combine(_comfyInstall.Paths.ActiveModelsRootPath, "checkpoints", fileName);
			EnvironmentPath = Path.GetDirectoryName(targetPath) ?? string.Empty;
			EnvironmentDetails = Text("setup.base_model.present");
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
			EnvironmentPath = GetCheckpointsDirectory();
			EnvironmentDetails = Text("setup.base_model.missing");
			AvailableOptions = new[]
			{
				DiagnosticNodeHelpers.CreateOption(
					DownloadOption,
					Text("setup.base_model.option_download"),
					Text("setup.base_model.option_download_description"),
					isRecommended: true,
					workingHint: Text("setup.base_model.work_hint_download"),
					canCancel: true,
					cancellationWorkingHint: Text("setup.base_model.canceling_download"),
					cancellationResultDetails: Text("setup.base_model.download_canceled")),
				DiagnosticNodeHelpers.CreateOption(
					BrowserOption,
					Text("setup.base_model.option_browser_download"),
					Text("setup.base_model.option_browser_download_description"),
					completionPolicy: DiagnosticActionCompletionPolicy.AssumeHealthy),
				DiagnosticNodeHelpers.CreateOption(
					LaterOption,
					Text("setup.base_model.option_skip"),
					requiresRecovery: false,
					completionPolicy: DiagnosticActionCompletionPolicy.AssumeOptionalMissing)
			};
		}
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
		if (optionId is BrowserOption or LaterOption)
		{
			EnvironmentPath = _comfyInstall.SettingsService.Settings.DefaultModelUrl;
			SecondaryEnvironmentPath = GetCheckpointsDirectory();
			EnvironmentDetails = optionId == BrowserOption
				? Text("setup.base_model.browser_download_requested")
				: Text("setup.base_model.skipped");
			return;
		}

		SecondaryEnvironmentPath = string.Empty;
	}

	public async Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		if (SelectedOptionId == BrowserOption)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				string url = _comfyInstall.SettingsService.Settings.DefaultModelUrl;
				NexusLog.Info($"[SETUP:BASE_MODEL] External model download requested. url={url}");

				// Browser launches are intentionally not part of the setup completion contract.
				// The user can reopen the same URL from the card if the shell does not surface a browser.
				_ = NexusAppManager.Instance.Platform.Shell.OpenPathAsync(url);

				EnvironmentPath = url;
				SecondaryEnvironmentPath = GetCheckpointsDirectory();
				EnvironmentDetails = Text("setup.base_model.browser_download_requested");
				return new RecoveryResult(true, Text("setup.base_model.browser_download_requested"));
			}
			catch (Exception ex)
			{
				return new RecoveryResult(false, LocalizationManager.Format("setup.base_model.browser_open_failed_with_error", ex.Message));
			}
		}

		try
		{
			var result = await _comfyInstall.DownloadDefaultModelAsync(cancellationToken);

			if (result.IsSuccess)
			{
				string fileName = _comfyInstall.SettingsService.Settings.DefaultModelFileName;
				string targetPath = Path.Combine(_comfyInstall.Paths.ActiveModelsRootPath, "checkpoints", fileName);
				EnvironmentPath = Path.GetDirectoryName(targetPath) ?? string.Empty;
				SecondaryEnvironmentPath = string.Empty;
				EnvironmentDetails = Text("setup.base_model.download_success");
			}
			return new RecoveryResult(result.IsSuccess, result.Message);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			return new RecoveryResult(false, LocalizationManager.Format("setup.base_model.download_failed", ex.Message));
		}
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);

	private string GetCheckpointsDirectory()
		=> Path.Combine(_comfyInstall.Paths.ActiveModelsRootPath, "checkpoints");
}
