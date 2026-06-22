namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Services;

internal sealed class BaseResourceDiagnosticNode : IConfigurableDiagnosticNode
{
	public string NodeId => "base-resources";
	public string DisplayName => Text("setup.base_model.title");
	public string Description => Text("setup.base_model.description");
	public string EnvironmentDetails { get; private set; } = Text("setup.common.pending_detail");
	public string EnvironmentPath { get; private set; } = string.Empty;
	public bool IsCritical => false;

	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = Array.Empty<DiagnosticOption>();
	public string SelectedOptionId { get; private set; } = string.Empty;

	public async Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		string fileName = SetupSettingsService.Instance.Settings.DefaultModelFileName;
		string targetPath = Path.Combine(ComfyInstallService.ComfyPath, "models", "checkpoints", fileName);

		if (File.Exists(targetPath))
		{
			long localSize = new FileInfo(targetPath).Length;
			if (localSize > 1024 * 1024 * 100) // At least 100MB sanity check
			{
				try
				{
					using var httpClient = new System.Net.Http.HttpClient();
					string url = SetupSettingsService.Instance.Settings.DefaultModelUrl;
					long? remoteSize = await ComfyUI_Nexus.Setup.Runtime.DownloadService.TryGetRemoteLengthAsync(httpClient, url, cancellationToken);

					if (!remoteSize.HasValue || localSize == remoteSize.Value)
					{
						return HealthState.Healthy;
					}
				}
				catch { }
			}
		}

		if (SelectedOptionId == "later")
		{
			return HealthState.OptionalMissing;
		}

		return HealthState.NeedsRecovery;
	}

	public async Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		var health = await CheckHealthAsync(cancellationToken);
		if (health == HealthState.Healthy)
		{
			string fileName = SetupSettingsService.Instance.Settings.DefaultModelFileName;
			string targetPath = Path.Combine(ComfyInstallService.ComfyPath, "models", "checkpoints", fileName);
			EnvironmentPath = Path.GetDirectoryName(targetPath) ?? string.Empty;
			EnvironmentDetails = Text("setup.base_model.present");
			AvailableOptions = new[]
			{
				DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.KeepOption, Text("setup.common.option_keep_next"), isRecommended: true)
			};
		}
		else
		{
			EnvironmentDetails = Text("setup.base_model.missing");
			AvailableOptions = new[]
			{
				DiagnosticNodeHelpers.CreateOption("download", Text("setup.base_model.option_download"), isRecommended: true),
				DiagnosticNodeHelpers.CreateOption("later", Text("setup.base_model.option_skip"))
			};
		}
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
	}

	public async Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		if (SelectedOptionId == "later")
		{
			EnvironmentDetails = Text("setup.base_model.skipped");
			return new RecoveryResult(true, Text("setup.base_model.skipped"));
		}

		try
		{
			var result = await ComfyInstallService.Instance.DownloadDefaultModelAsync(cancellationToken);

			if (result.IsSuccess)
			{
				string fileName = SetupSettingsService.Instance.Settings.DefaultModelFileName;
				string targetPath = Path.Combine(ComfyInstallService.ComfyPath, "models", "checkpoints", fileName);
				EnvironmentPath = Path.GetDirectoryName(targetPath) ?? string.Empty;
				EnvironmentDetails = Text("setup.base_model.download_success");
			}
			return new RecoveryResult(result.IsSuccess, result.Message);
		}
		catch (Exception ex)
		{
			return new RecoveryResult(false, LocalizationManager.Format("setup.base_model.download_failed", ex.Message));
		}
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);
}
