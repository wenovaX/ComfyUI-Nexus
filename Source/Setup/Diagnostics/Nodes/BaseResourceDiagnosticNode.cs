namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Services;
using Microsoft.Maui.ApplicationModel;

internal sealed class BaseResourceDiagnosticNode : IConfigurableDiagnosticNode
{
	private const string DownloadOption = "download";
	private const string BrowserOption = "browser";
	private const string LaterOption = "later";

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

		if (SelectedOptionId == BrowserOption)
		{
			return HealthState.Healthy;
		}

		if (SelectedOptionId == LaterOption)
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
			EnvironmentPath = GetCheckpointsDirectory();
			EnvironmentDetails = Text("setup.base_model.missing");
			AvailableOptions = new[]
			{
				DiagnosticNodeHelpers.CreateOption(
					DownloadOption,
					Text("setup.base_model.option_download"),
					Text("setup.base_model.option_download_description"),
					isRecommended: true,
					workingHint: Text("setup.base_model.work_hint_download")),
				DiagnosticNodeHelpers.CreateOption(
					BrowserOption,
					Text("setup.base_model.option_browser_download"),
					Text("setup.base_model.option_browser_download_description")),
				DiagnosticNodeHelpers.CreateOption(LaterOption, Text("setup.base_model.option_skip"))
			};
		}
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
	}

	public async Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		if (SelectedOptionId == BrowserOption)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				string url = SetupSettingsService.Instance.Settings.DefaultModelUrl;
				bool opened = await Launcher.Default.OpenAsync(url);
				if (!opened)
				{
					return new RecoveryResult(false, Text("setup.base_model.browser_open_failed"));
				}

				Directory.CreateDirectory(GetCheckpointsDirectory());
				EnvironmentPath = GetCheckpointsDirectory();
				EnvironmentDetails = Text("setup.base_model.browser_download_started");
				return new RecoveryResult(true, Text("setup.base_model.browser_download_started"));
			}
			catch (Exception ex)
			{
				return new RecoveryResult(false, LocalizationManager.Format("setup.base_model.browser_open_failed_with_error", ex.Message));
			}
		}

		if (SelectedOptionId == LaterOption)
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

	private static string GetCheckpointsDirectory()
		=> Path.Combine(ComfyPathResolver.ResolveActiveModelsRootPath(), "checkpoints");
}
