namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Declares the bounded animated WebP working sets used by the application.
/// A surface owner acquires one group before it becomes interactive and releases
/// it when that surface leaves the lifecycle.
/// </summary>
internal enum NexusAnimatedWebpCacheGroup
{
	Setup,
	Shell,
	ControlDeck,
}

internal static class NexusAnimatedWebpCacheCatalog
{
	internal static readonly NexusAnimatedWebpDefinition SetupCrossroadsAmbient = new(
		"animations/setup_crossroads_ambient.webp",
		sourceFrameStride: 3,
		decodePixelWidth: 640);
	internal static readonly NexusAnimatedWebpDefinition SetupWelcomeTitle = new(
		"animations/welcome_to_nexus_title.webp",
		sourceFrameStride: 2,
		decodePixelWidth: 750);
	internal static readonly NexusAnimatedWebpDefinition SetupVanguardIcon = new(
		"animations/setup_vanguard_bold_animated.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 280);
	internal static readonly NexusAnimatedWebpDefinition SetupArchitectIcon = new(
		"animations/setup_architect_bold_animated.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 280);
	internal static readonly NexusAnimatedWebpDefinition SetupVanguardSelectionBurst = new(
		"animations/setup_vanguard_selection_burst.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 960);
	internal static readonly NexusAnimatedWebpDefinition SetupArchitectSelectionBurst = new(
		"animations/setup_architect_selection_burst.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 960);
	internal static readonly NexusAnimatedWebpDefinition SetupConsoleReadyPulse = new(
		"animations/setup_console_ready_pulse.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 240);
	internal static readonly NexusAnimatedWebpDefinition SetupConsoleBootingPulse = new(
		"animations/setup_console_booting_pulse.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 240);
	internal static readonly NexusAnimatedWebpDefinition SetupConsoleStatusBootingPulse = new(
		"animations/setup_console_status_booting_pulse.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 120);
	internal static readonly NexusAnimatedWebpDefinition SetupDiagnosticLoadingRing = new(
		"animations/setup_diagnostic_loading_ring.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 48);
	internal static readonly NexusAnimatedWebpDefinition SetupPrimaryActionReadyPulse = new(
		"animations/setup_primary_action_ready_pulse.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 220);

	internal static readonly NexusAnimatedWebpDefinition LoadingProcess = new(
		"animations/loading_process.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 256);
	internal static readonly NexusAnimatedWebpDefinition LoadingSuccess = new(
		"animations/loading_success_gate.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 256);
	internal static readonly NexusAnimatedWebpDefinition ServerBootIdle = new(
		"animations/server_boot_idle.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 384);
	internal static readonly NexusAnimatedWebpDefinition ServerBooting = new(
		"animations/server_booting.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 384);
	internal static readonly NexusAnimatedWebpDefinition ServerBootSuccess = new(
		"animations/server_boot_success.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 384);
	internal static readonly NexusAnimatedWebpDefinition ServerBootFailed = new(
		"animations/server_boot_failed.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 384);
	internal static readonly NexusAnimatedWebpDefinition HeaderGpuIdle = new(
		"animations/header_gpu_idle_energy_core.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 116);
	internal static readonly NexusAnimatedWebpDefinition HeaderGpuRunning = new(
		"animations/header_gpu_running_energy_core.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 116);
	internal static readonly NexusAnimatedWebpDefinition HeaderGpuFrameGauge = new(
		"animations/header_gpu_frame_gauge.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 116);
	internal static readonly NexusAnimatedWebpDefinition HeaderVramFrameGauge = new(
		"animations/header_vram_frame_gauge.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 404);
	internal static readonly NexusAnimatedWebpDefinition HeaderCpuFrameGauge = new(
		"animations/header_cpu_frame_gauge.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 156);
	internal static readonly NexusAnimatedWebpDefinition HeaderMainActionStopSignal = new(
		"animations/header_main_action_stop_signal.webp",
		sourceFrameStride: 1,
		decodePixelWidth: 112);

	internal static IReadOnlyList<NexusAnimatedWebpDefinition> GetDefinitions(NexusAnimatedWebpCacheGroup group)
		=> group switch
		{
			NexusAnimatedWebpCacheGroup.Setup =>
			[
				SetupCrossroadsAmbient,
				SetupWelcomeTitle,
				SetupVanguardIcon,
				SetupArchitectIcon,
				SetupVanguardSelectionBurst,
				SetupArchitectSelectionBurst,
				SetupConsoleReadyPulse,
				SetupConsoleBootingPulse,
				SetupConsoleStatusBootingPulse,
				SetupDiagnosticLoadingRing,
				SetupPrimaryActionReadyPulse,
			],
			NexusAnimatedWebpCacheGroup.Shell =>
			[
				LoadingProcess,
				LoadingSuccess,
				HeaderGpuIdle,
				HeaderGpuRunning,
				HeaderGpuFrameGauge,
				HeaderVramFrameGauge,
				HeaderCpuFrameGauge,
				HeaderMainActionStopSignal,
			],
			NexusAnimatedWebpCacheGroup.ControlDeck =>
			[
				HeaderGpuIdle,
				HeaderGpuRunning,
			],
			_ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
		};
}
