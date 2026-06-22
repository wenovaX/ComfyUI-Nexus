using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Localization;

namespace ComfyUI_Nexus.Setup;

internal static class SetupStepCatalog
{
	internal static IReadOnlyList<SetupStep> CreateDefaultSteps()
	{
		return [
			new SetupStep(
				SetupStepIds.Welcome,
				Text("setup.steps.welcome.title"),
				Text("setup.steps.welcome.description"),
				static (context, cancellationToken) => context.CoreLinkDetector.RunWelcomeAsync(cancellationToken)),
			new SetupStep(
				SetupStepIds.CoreLink,
				Text("setup.steps.core_link.title"),
				Text("setup.steps.core_link.description"),
				static (context, cancellationToken) => context.CoreLinkDetector.CheckAsync(cancellationToken)),
			new SetupStep(
				SetupStepIds.ComfyCore,
				Text("setup.steps.comfy_core.title"),
				Text("setup.steps.comfy_core.description"),
				static (context, cancellationToken) => context.ComfyInstallService.InstallCoreAsync(cancellationToken)),
			new SetupStep(
				SetupStepIds.BaseResources,
				Text("setup.steps.base_resources.title"),
				Text("setup.steps.base_resources.description"),
				static (context, cancellationToken) => context.ComfyInstallService.DownloadDefaultModelAsync(cancellationToken)),
			new SetupStep(
				SetupStepIds.Manager,
				Text("setup.steps.manager.title"),
				Text("setup.steps.manager.description"),
				static (context, cancellationToken) => context.ComfyInstallService.InstallManagerAsync(cancellationToken)),
			new SetupStep(
				SetupStepIds.HudBridge,
				Text("setup.steps.hud_bridge.title"),
				Text("setup.steps.hud_bridge.description"),
				static (context, cancellationToken) => context.ComfyInstallService.InstallHudBridgeAsync(cancellationToken)),
			new SetupStep(
				SetupStepIds.Dependencies,
				Text("setup.steps.dependencies.title"),
				Text("setup.steps.dependencies.description"),
				static (context, cancellationToken) => context.ComfyInstallService.InstallDependenciesAsync(cancellationToken)),
			new SetupStep(
				SetupStepIds.Server,
				Text("setup.steps.server.title"),
				Text("setup.steps.server.description"),
				static (context, cancellationToken) => context.ComfyServerProcessService.StartAndWaitAsync(cancellationToken)),
			new SetupStep(
				SetupStepIds.Launch,
				Text("setup.steps.launch.title"),
				Text("setup.steps.launch.description"),
				static (context, cancellationToken) => context.NexusAppEntryService.LaunchAsync(cancellationToken)),
		];
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);
}
