using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

namespace ComfyUI_Nexus.Setup.Runtime;

internal enum ServerLifecycleMode
{
	Startup,
	BootServerOnly,
	Restart,
	Shutdown,
	MaintenanceRecovery,
	KillServerAndExit,
	KeepServerRunningAndExit,
}

internal enum ServerLifecycleState
{
	Idle,
	Preparing,
	QuiescingServices,
	ServicesEnded,
	StoppingServer,
	VerifyingServerStopped,
	RunningMaintenance,
	BootingServer,
	ServerReady,
	ClosingApplication,
	Completed,
	Failed,
}

[Flags]
internal enum ServerLifecycleCapability
{
	None = 0,
	Refresh = 1,
	Shutdown = 2,
	Boot = 4,
}

internal sealed record ServerLifecycleRequest(
	ServerLifecycleMode Mode,
	bool RepairRuntimeBeforeBoot = false,
	bool ResumePendingServerProcess = false,
	Func<CancellationToken, Task<SetupStepResult>>? MaintenanceAsync = null,
	Func<Task>? OnServerStoppedAsync = null);

internal sealed record ServerLifecycleSnapshot(
	long Generation,
	ServerLifecycleMode Mode,
	ServerLifecycleState State,
	string Detail,
	IReadOnlyList<string> Warnings,
	bool ShellServicesRunning);

internal sealed record ServerLifecycleResult(
	bool IsSuccess,
	bool IsInProgress,
	bool RequiresSetupHandoff,
	string Message,
	ServerLifecycleSnapshot Snapshot)
{
	internal static ServerLifecycleResult InProgress(ServerLifecycleSnapshot snapshot)
		=> new(false, true, false, "Another server lifecycle operation is already running.", snapshot);
}

internal sealed record ServerLifecycleShellHooks(
	Func<string, Task> PrepareForServerInterruptionAsync,
	Func<string, Task> QuiesceShellServicesAsync,
	Func<Task> StartShellServicesAsync);

/// <summary>
/// Owns ComfyUI server transitions. UI surfaces observe snapshots and provide
/// shell hooks; they do not decide when server/process stages may advance.
/// </summary>
internal sealed class NexusServerLifecycleCoordinator
{
	private static readonly TimeSpan ServerShutdownTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan MinimumServerShutdownPollingInterval = TimeSpan.FromMilliseconds(250);

	private readonly object _gate = new();
	private readonly GpuDiscoveryService _gpuDiscovery;
	private readonly NexusServerProcessController _serverProcesses;
	private readonly SetupSequenceOrchestrator _serverBootSequence;
	private Task<ServerLifecycleResult>? _activeOperation;
	private object? _shellOwner;
	private ServerLifecycleShellHooks? _shellHooks;
	private long _generation;
	private bool _shellServicesRunning;
	private ServerLifecycleSnapshot _snapshot = new(
		0,
		ServerLifecycleMode.Startup,
		ServerLifecycleState.Idle,
		"Idle",
		Array.Empty<string>(),
		false);

	internal NexusServerLifecycleCoordinator(
		GpuDiscoveryService gpuDiscovery,
		NexusToolingEnvironment tooling,
		ComfyInstallService comfyInstall,
		NexusServerProcessController serverProcesses)
	{
		_gpuDiscovery = gpuDiscovery ?? throw new ArgumentNullException(nameof(gpuDiscovery));
		_serverProcesses = serverProcesses ?? throw new ArgumentNullException(nameof(serverProcesses));
		_serverBootSequence = new SetupSequenceOrchestrator(tooling, comfyInstall, _serverProcesses);
		_serverBootSequence.OnProgress += RelayServerBootProgress;
	}

	internal event Action<ServerLifecycleSnapshot>? StateChanged;
	internal event Action<string>? LogEmitted;

	internal ServerLifecycleSnapshot Snapshot
	{
		get
		{
			lock (_gate)
			{
				return _snapshot;
			}
		}
	}

	internal bool Allows(ServerLifecycleCapability capability)
	{
		ServerLifecycleState state = Snapshot.State;
		return capability switch
		{
			ServerLifecycleCapability.Refresh => state is ServerLifecycleState.Idle or ServerLifecycleState.Completed or ServerLifecycleState.Failed,
			ServerLifecycleCapability.Shutdown => state is ServerLifecycleState.Idle or ServerLifecycleState.Completed or ServerLifecycleState.Failed,
			ServerLifecycleCapability.Boot => state is ServerLifecycleState.Idle or ServerLifecycleState.Completed or ServerLifecycleState.Failed,
			_ => false,
		};
	}

	internal void AttachShell(object owner, ServerLifecycleShellHooks hooks)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(hooks);

		lock (_gate)
		{
			_shellOwner = owner;
			_shellHooks = hooks;
		}
	}

	internal void DetachShell(object owner)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(_shellOwner, owner))
			{
				return;
			}

			_shellOwner = null;
			_shellHooks = null;
		}
	}

	internal Task<ServerLifecycleResult> RunAsync(ServerLifecycleRequest request, CancellationToken cancellationToken = default)
	{
		var completion = new TaskCompletionSource<ServerLifecycleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_gate)
		{
			if (_activeOperation != null)
			{
				return Task.FromResult(ServerLifecycleResult.InProgress(_snapshot));
			}

			_activeOperation = completion.Task;
		}

		_ = ExecuteOperationAsync(request, completion, cancellationToken);
		return completion.Task;
	}

	internal async Task ActivateShellServicesAsync()
	{
		ServerLifecycleShellHooks? hooks = GetShellHooks();
		if (hooks == null || _shellServicesRunning)
		{
			return;
		}

		try
		{
			await hooks.StartShellServicesAsync();
			_shellServicesRunning = true;
		}
		catch (Exception ex)
		{
			PublishWarning($"Shell service start completed with an error: {ex.Message}");
		}
	}

	private async Task ExecuteOperationAsync(
		ServerLifecycleRequest request,
		TaskCompletionSource<ServerLifecycleResult> completion,
		CancellationToken cancellationToken)
	{
		try
		{
			completion.TrySetResult(await RunCoreAsync(request, cancellationToken));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			ServerLifecycleSnapshot snapshot = Publish(request.Mode, ServerLifecycleState.Failed, "Lifecycle operation canceled.");
			completion.TrySetResult(new ServerLifecycleResult(false, false, false, "Lifecycle operation canceled.", snapshot));
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[LIFECYCLE] Server lifecycle operation failed");
			ServerLifecycleSnapshot snapshot = Publish(request.Mode, ServerLifecycleState.Failed, ex.Message);
			completion.TrySetResult(new ServerLifecycleResult(false, false, false, ex.Message, snapshot));
		}
		finally
		{
			lock (_gate)
			{
				if (ReferenceEquals(_activeOperation, completion.Task))
				{
					_activeOperation = null;
				}
			}
		}
	}

	private async Task<ServerLifecycleResult> RunCoreAsync(ServerLifecycleRequest request, CancellationToken cancellationToken)
	{
		Publish(request.Mode, ServerLifecycleState.Preparing, "Preparing server lifecycle operation.");

		if (request.Mode is ServerLifecycleMode.Restart or ServerLifecycleMode.MaintenanceRecovery)
		{
			await QuiesceShellAsync(request.Mode, "Server interruption requested.", prepareForInterruption: true);
			await StopAndVerifyServerAsync(request.Mode, cancellationToken);
			await TransitionAfterServerStopAsync(request.Mode, request.OnServerStoppedAsync);
		}
		else if (request.Mode is ServerLifecycleMode.Shutdown)
		{
			await QuiesceShellAsync(request.Mode, "Server shutdown requested from Nexus Control Deck.", prepareForInterruption: true);
			await StopAndVerifyServerAsync(request.Mode, cancellationToken);
			return Complete(request.Mode, "ComfyUI server shutdown confirmed.");
		}
		else if (request.Mode is ServerLifecycleMode.KillServerAndExit)
		{
			await QuiesceShellAsync(request.Mode, "Application server shutdown requested.", prepareForInterruption: true);
			await StopAndVerifyServerAsync(request.Mode, cancellationToken);
			Publish(request.Mode, ServerLifecycleState.ClosingApplication, "Server shutdown confirmed. Closing application.");
			return Complete(request.Mode, "Server shutdown confirmed.");
		}
		else if (request.Mode is ServerLifecycleMode.KeepServerRunningAndExit)
		{
			await QuiesceShellAsync(request.Mode, "Application exit requested.", prepareForInterruption: false);
			Publish(request.Mode, ServerLifecycleState.ClosingApplication, "Shell services ended. Keeping ComfyUI server running.");
			return Complete(request.Mode, "Shell services ended. ComfyUI server remains running.");
		}

		if (request.MaintenanceAsync != null)
		{
			Publish(request.Mode, ServerLifecycleState.RunningMaintenance, "Running lifecycle maintenance.");
			SetupStepResult maintenanceResult = await request.MaintenanceAsync(cancellationToken);
			if (!maintenanceResult.IsSuccess)
			{
				return Fail(request.Mode, maintenanceResult.Message, maintenanceResult.RequiresSetupHandoff);
			}

			if (maintenanceResult.RequiresSetupHandoff)
			{
				return Complete(request.Mode, maintenanceResult.Message, requiresSetupHandoff: true);
			}
		}

		Publish(request.Mode, ServerLifecycleState.BootingServer, request.ResumePendingServerProcess
			? "Waiting for the existing ComfyUI server launch."
			: "Starting ComfyUI server.");
		SetupStepResult bootResult = await _serverBootSequence.RunServerBootAsync(
			request.RepairRuntimeBeforeBoot,
			EmitLog,
			cancellationToken);
		if (!bootResult.IsSuccess)
		{
			return Fail(request.Mode, bootResult.Message, bootResult.RequiresSetupHandoff);
		}

		if (bootResult.RequiresSetupHandoff)
		{
			return Complete(request.Mode, bootResult.Message, requiresSetupHandoff: true);
		}

		Publish(request.Mode, ServerLifecycleState.ServerReady, "ComfyUI HTTP readiness confirmed.");
		return Complete(request.Mode, "ComfyUI server is ready.");
	}

	private async Task TransitionAfterServerStopAsync(ServerLifecycleMode mode, Func<Task>? transitionAsync)
	{
		if (transitionAsync == null)
		{
			return;
		}

		try
		{
			EmitLog("[LIFECYCLE] Server shutdown is verified. Preparing the next surface.");
			await transitionAsync();
		}
		catch (Exception ex)
		{
			PublishWarning($"Server-stop UI transition completed with an error: {ex.Message}");
		}
	}

	private void RelayServerBootProgress(double _, string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}

		EmitLog(message);
	}

	private async Task QuiesceShellAsync(ServerLifecycleMode mode, string reason, bool prepareForInterruption)
	{
		Publish(mode, ServerLifecycleState.QuiescingServices, reason);
		_gpuDiscovery.BeginQuiesce();
		ServerLifecycleShellHooks? hooks = GetShellHooks();

		try
		{
			if (hooks != null)
			{
				if (prepareForInterruption)
				{
					await hooks.PrepareForServerInterruptionAsync(reason);
				}
				else
				{
					await hooks.QuiesceShellServicesAsync(reason);
				}
			}
			else
			{
				GpuDiscoveryService.StopResult gpuStop = await _gpuDiscovery.StopAsync();
				if (!gpuStop.IsSuccess)
				{
					PublishWarning($"GPU discovery service stop completed with an error: {gpuStop.FailureMessage}");
				}
			}
		}
		catch (Exception ex)
		{
			PublishWarning($"Shell service stop completed with an error: {ex.Message}");
		}

		_shellServicesRunning = false;
		Publish(mode, ServerLifecycleState.ServicesEnded, "Shell services ended.");
	}

	private async Task StopAndVerifyServerAsync(ServerLifecycleMode mode, CancellationToken cancellationToken)
	{
		Publish(mode, ServerLifecycleState.StoppingServer, "Stopping ComfyUI server process.");
		ComfyServerProcessInfo? processInfo = _serverProcesses.FindServerProcess();
		if (processInfo != null)
		{
			EmitLog($"[LIFECYCLE] Terminating ComfyUI server: {processInfo.ProcessName} ({processInfo.ProcessId}).");
			await _serverProcesses.ShutdownAsync(processInfo, ServerShutdownTimeout);
		}
		else
		{
			EmitLog("[LIFECYCLE] No registered ComfyUI server process was found.");
		}

		Publish(mode, ServerLifecycleState.VerifyingServerStopped, "Verifying server process and listener shutdown.");
		var settings = _serverBootSequence.SettingsService.Settings;
		await _serverProcesses.EnsureStoppedAsync(
			processInfo,
			settings.ServerPort,
			ServerShutdownTimeout,
			TimeSpan.FromMilliseconds(Math.Max(
				(int)MinimumServerShutdownPollingInterval.TotalMilliseconds,
				settings.PortProbeIntervalMilliseconds)),
			cancellationToken);
	}

	private ServerLifecycleResult Complete(
		ServerLifecycleMode mode,
		string message,
		bool requiresSetupHandoff = false)
	{
		ServerLifecycleSnapshot snapshot = Publish(mode, ServerLifecycleState.Completed, message);
		return new ServerLifecycleResult(true, false, requiresSetupHandoff, message, snapshot);
	}

	private ServerLifecycleResult Fail(ServerLifecycleMode mode, string message, bool requiresSetupHandoff)
	{
		ServerLifecycleSnapshot snapshot = Publish(mode, ServerLifecycleState.Failed, message);
		return new ServerLifecycleResult(false, false, requiresSetupHandoff, message, snapshot);
	}

	private ServerLifecycleSnapshot Publish(ServerLifecycleMode mode, ServerLifecycleState state, string detail)
	{
		ServerLifecycleSnapshot snapshot;
		lock (_gate)
		{
			bool isNewOperation = state == ServerLifecycleState.Preparing;
			if (isNewOperation)
			{
				_generation++;
			}

			snapshot = new ServerLifecycleSnapshot(
				_generation,
				mode,
				state,
				detail,
				isNewOperation ? Array.Empty<string>() : _snapshot.Warnings,
				_shellServicesRunning);
			_snapshot = snapshot;
		}

		NexusLog.Info($"[LIFECYCLE #{snapshot.Generation:D2}] {snapshot.Mode} {snapshot.State} - {snapshot.Detail}");
		NexusUiActionTrace.Record("Lifecycle", $"{snapshot.Mode}.{snapshot.State}", snapshot.Detail);
		StateChanged?.Invoke(snapshot);
		return snapshot;
	}

	private void PublishWarning(string warning)
	{
		ServerLifecycleSnapshot snapshot;
		lock (_gate)
		{
			string[] warnings = [.. _snapshot.Warnings, warning];
			snapshot = _snapshot with { Warnings = warnings };
			_snapshot = snapshot;
		}

		NexusLog.Warning($"[LIFECYCLE] {warning}");
		StateChanged?.Invoke(snapshot);
	}

	private void EmitLog(string message)
	{
		NexusLog.Info(message);
		LogEmitted?.Invoke(message);
	}

	private ServerLifecycleShellHooks? GetShellHooks()
	{
		lock (_gate)
		{
			return _shellHooks;
		}
	}
}
