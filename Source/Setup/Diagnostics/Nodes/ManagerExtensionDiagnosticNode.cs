namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

internal sealed class ManagerExtensionDiagnosticNode : IConfigurableDiagnosticNode
{
	public string NodeId => "manager-extension";
	public string DisplayName => Text("setup.extensions.title");
	public string Description => Text("setup.extensions.description");
	public bool IsCritical => true;

	public string EnvironmentDetails { get; private set; } = Text("setup.common.pending_detail");
	public string EnvironmentPath { get; private set; } = string.Empty;
	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = Array.Empty<DiagnosticOption>();
	public string SelectedOptionId { get; private set; } = string.Empty;

	public Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		try
		{
			string customNodesPath = ComfyPathResolver.ResolveActiveCustomNodesPath();
			string managerPath = Path.Combine(customNodesPath, HudBridgeInstaller.ManagerExtensionFolderName);
			string hudPath = Path.Combine(customNodesPath, HudBridgeInstaller.HudExtensionFolderName);

			var essentialNodes = SetupSettingsService.Instance.Settings.EssentialNodes;
			var details = new System.Text.StringBuilder();

			bool allHealthy = true;

			bool managerExists = File.Exists(Path.Combine(managerPath, "__init__.py"));
			if (managerExists)
			{
				details.AppendLine(LocalizationManager.Format("setup.extensions.item_installed", "Manager"));
			}
			else
			{
				allHealthy = false;
				details.AppendLine(LocalizationManager.Format("setup.extensions.item_pending", "Manager"));
			}

			bool hudExists = File.Exists(Path.Combine(hudPath, "__init__.py"));
			if (hudExists)
			{
				details.AppendLine(LocalizationManager.Format("setup.extensions.item_installed", "HUD"));
			}
			else
			{
				allHealthy = false;
				details.AppendLine(LocalizationManager.Format("setup.extensions.item_pending", "HUD"));
			}

			bool bridgeExists = HudBridgeInstaller.IsNexusBridgeExtensionHealthy(customNodesPath);
			if (bridgeExists)
			{
				details.AppendLine(LocalizationManager.Format("setup.extensions.item_installed", "Nexus Bridge"));
			}
			else
			{
				allHealthy = false;
				details.AppendLine(LocalizationManager.Format("setup.extensions.item_pending", "Nexus Bridge"));
			}

			foreach (var node in essentialNodes)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (string.Equals(node.Folder, HudBridgeInstaller.ManagerExtensionFolderName, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(node.Folder, HudBridgeInstaller.HudExtensionFolderName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string nodePath = Path.Combine(customNodesPath, node.Folder);
				bool nodeExists = Directory.Exists(nodePath);

				if (nodeExists)
				{
					details.AppendLine(LocalizationManager.Format("setup.extensions.item_installed", node.Folder));
				}
				else
				{
					allHealthy = false;
					details.AppendLine(LocalizationManager.Format("setup.extensions.item_pending", node.Folder));
				}
			}

			EnvironmentDetails = details.ToString().TrimEnd();
			EnvironmentPath = customNodesPath;

			return Task.FromResult(allHealthy ? HealthState.Healthy : HealthState.NeedsRecovery);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[INIT:MANAGER] Check failed");
			EnvironmentDetails = LocalizationManager.Format("setup.extensions.check_failed", ex.Message);
			EnvironmentPath = ComfyPathResolver.ResolveActiveCustomNodesPath();
			return Task.FromResult(HealthState.CriticalError);
		}
	}

	public async Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		var health = await CheckHealthAsync(cancellationToken);
		if (health == HealthState.Healthy)
		{
			AvailableOptions = new[]
			{
				DiagnosticNodeHelpers.CreateOption(DiagnosticNodeHelpers.KeepOption, Text("setup.common.option_keep_next"), isRecommended: true)
			};
		}
		else
		{
			AvailableOptions = new[]
			{
				DiagnosticNodeHelpers.CreateOption("install", Text("setup.extensions.option_install"), isRecommended: true)
			};
		}
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
	}

	public async Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		try
		{
			progress?.Report(0.1);
			var extensionResult = await ComfyInstallService.Instance.InstallManagedExtensionsAsync(
				targetFolders: null,
				forceSyncExisting: false,
				reinstallExisting: false,
				cancellationToken);
			if (!extensionResult.IsSuccess) return new RecoveryResult(false, extensionResult.Message);

			SetupSettingsService.Instance.EnqueueBootTask(
				PendingBootTaskIds.ExtensionRepair,
				origin: "initiation-sequence",
				action: PendingBootTaskActions.ExtensionSync);

			progress?.Report(1.0);

			// Refresh details
			await CheckHealthAsync(cancellationToken);

			return new RecoveryResult(true, Text("setup.extensions.prepare_success"));
		}
		catch (Exception ex)
		{
			return new RecoveryResult(false, LocalizationManager.Format("setup.extensions.install_failed", ex.Message));
		}
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);
}
