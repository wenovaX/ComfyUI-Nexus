namespace ComfyUI_Nexus;

using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Controls.Buttons;

/// <summary>
/// Owns app-lifetime runtime components. Views consume explicit component contracts
/// and never construct or retain replacement app-runtime instances.
/// </summary>
internal sealed class NexusAppManager : IDisposable
{
	private static readonly object InstanceGate = new();
	private static NexusAppManager? _instance;
	private bool _disposed;

	/// <summary>
	/// Gets the fully initialized app-runtime owner.
	/// </summary>
	internal static NexusAppManager Instance
		=> Volatile.Read(ref _instance)
			?? throw new InvalidOperationException("Nexus app runtime manager is unavailable.");

	internal static NexusAppManager CreateForApplication()
	{
		lock (InstanceGate)
		{
			if (_instance is not null)
			{
				throw new InvalidOperationException("Nexus app runtime manager has already been created.");
			}

			var manager = new NexusAppManager();
			Volatile.Write(ref _instance, manager);
			return manager;
		}
	}

	internal NexusToolingEnvironment Tooling { get; }
	internal SetupSettingsService Settings { get; } = new();
	internal NexusPreferenceStore Preferences { get; } = new();
	internal PlatformManager Platform { get; } = PlatformManager.CreateForAppRuntime();
	internal NexusComfyRuntimePaths Paths { get; }
	internal NexusServerProcessController ServerProcesses { get; }
	internal ComfyInstallService ComfyInstall { get; }
	internal GpuDiscoveryService GpuDiscovery { get; } = new();
	internal NexusServerLifecycleCoordinator ServerLifecycle { get; }
	internal NexusControlDeckWindowService ControlDeckWindow { get; } = new();
	internal NexusBackgroundWorkerPool BackgroundWorkers { get; } = new();
	internal NexusAnimatedWebpFrameCache AnimatedWebpFrames { get; } = new();
	internal NexusUiPostCoordinator UiPosts { get; } = new();
	internal NexusShellLayoutScaleService ShellLayoutScale { get; } = new();
	internal NexusSessionDiagnosticsService SessionDiagnostics { get; } = new();
	internal NexusDialogService Dialogs { get; } = new();
	internal NexusRailHoverRegistry RailHoverRegistry { get; } = new();
	internal NexusExceptionDiagnosticsService ExceptionDiagnostics { get; } = new();
	internal NexusBindingDiagnosticsService BindingDiagnostics { get; } = new();

	private NexusAppManager()
	{
		NexusStorageProvisioner.EnsureCreated();
		Paths = new NexusComfyRuntimePaths(Settings, Preferences);
		Tooling = new NexusToolingEnvironment(Settings, Paths, Preferences);
		_ = Tooling.StartStartupCleanupAsync(BackgroundWorkers);
		ServerProcesses = new NexusServerProcessController(Settings);
		ComfyInstall = new ComfyInstallService(Tooling, ServerProcesses, Settings, Paths);
		ServerLifecycle = new NexusServerLifecycleCoordinator(GpuDiscovery, Tooling, ComfyInstall, ServerProcesses);
		NexusConcurrencyDiagnostics.ConfigureRuntimeSnapshots(
			BackgroundWorkers.GetSnapshot,
			UiPosts.GetSnapshot);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		ControlDeckWindow.Close();
		Dialogs.Dispose();
		SessionDiagnostics.Dispose();
		BindingDiagnostics.Dispose();
		ExceptionDiagnostics.Dispose();
		Tooling.Dispose();
		ShellLayoutScale.Dispose();
		BackgroundWorkers.Dispose();
		AnimatedWebpFrames.Dispose();
		UiPosts.Dispose();
		NexusConcurrencyDiagnostics.ClearRuntimeSnapshots();
		lock (InstanceGate)
		{
			if (ReferenceEquals(_instance, this))
			{
				Volatile.Write(ref _instance, null);
			}
		}
	}
}
