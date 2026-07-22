namespace ComfyUI_Nexus.Setup.Services;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Settings;
using ComfyUI_Nexus.Ui;

internal sealed class ComfyServerProcessService
{
	private const string ServerTag = "[Server]";
	private const string Nodes20EnabledSettingId = "Comfy.VueNodes.Enabled";
	private SetupSettings Settings => _settingsService.Settings;
	private int ConfiguredPort => Settings.ServerPort;
	private string ConfiguredListenAddress => Settings.ListenAddress;
	private string ConfiguredProbeAddress => GetProbeAddress(ConfiguredListenAddress);
	private string ConfiguredGpuId => Settings.GpuId;
	private TimeSpan StartupIdleTimeout => TimeSpan.FromSeconds(Math.Max(300, Settings.ServerStartupTimeoutSeconds));
	private TimeSpan ServerReadinessInterval => TimeSpan.FromMilliseconds(Math.Max(250, Settings.PortProbeIntervalMilliseconds));
	private TimeSpan ServerLogTailInterval => TimeSpan.FromMilliseconds(Math.Max(50, Settings.ServerLogTailIntervalMilliseconds));
	private static readonly TimeSpan ReadinessPendingThreshold = TimeSpan.FromSeconds(2);
	private static readonly JsonSerializerOptions WriteIndentedJsonOptions = new()
	{
		WriteIndented = true,
	};

	private const string StartupArgsFormat = "-u \"{0}\" --listen {1} --port {2} --cuda-device {3} --preview-method none";

	private Process? _serverProcess;
	private readonly NexusServerProcessController _serverProcesses;
	private readonly SetupSettingsService _settingsService;
	private readonly NexusComfyRuntimePaths _paths;
	private IDisposable? _serverLogTail;
	private string? _currentServerLogPath;
	private DateTimeOffset _lastServerStartupActivityAt;
	private long _readinessGeneration;

	internal Action<string>? OnMessage { get; set; }

	internal ComfyServerProcessService(
		NexusServerProcessController serverProcesses,
		SetupSettingsService settingsService,
		NexusComfyRuntimePaths paths)
	{
		_serverProcesses = serverProcesses ?? throw new ArgumentNullException(nameof(serverProcesses));
		_settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
	}

	private void Log(string message) => OnMessage?.Invoke(message);

	internal async Task<SetupStepResult> StartAndWaitAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		long readinessGeneration = Interlocked.Increment(ref _readinessGeneration);

		string serverPython = ResolveServerPythonExecutable();
		string comfyPath = ResolvePhysicalPath(_paths.ActiveComfyPath);
		string mainPy = Path.Combine(comfyPath, "main.py");
		string logsDirectory = ComfyInstallService.GetLocalRuntimePath("Logs");
		string latestServerLogPath = SessionLogPaths.GetLatestLogPath(logsDirectory, SessionLogPaths.ComfyServerLatestFileName);
		Settings.ServerLogFile = $"Logs/{SessionLogPaths.ComfyServerLatestFileName}";
		_currentServerLogPath = null;

		Log($"{ServerTag} Initializing server launch sequence...");
		EnsureConfiguredGpuSelection();
		EnsureNodes20Enabled(comfyPath);
		Log($"{ServerTag} PythonMode: {Settings.ServerPythonMode}");
		Log($"{ServerTag} Python: {serverPython}");
		Log($"{ServerTag} WorkDir: {comfyPath}");
		Log($"{ServerTag} Entry: {mainPy}");
		Log($"{ServerTag} Port: {ConfiguredPort}");
		Log($"{ServerTag} ProbeHost: {ConfiguredProbeAddress}");
		Log($"{ServerTag} CUDA Device: {ConfiguredGpuId}");
		Log($"{ServerTag} StartupIdleTimeout: {StartupIdleTimeout.TotalSeconds:0}s");
		Log($"{ServerTag} LatestLog: {latestServerLogPath}");

		if (await IsComfyHttpReadyAsync(cancellationToken))
		{
			Log($"{ServerTag} Server HTTP endpoint is already ready on {ComfyApiOptions.GetLocalBaseUrl(Settings)}. Skipping launch.");
			MarkLaunchSuccessful();
			return new SetupStepResult(true, "Server already running.", 1);
		}

		if (TryAttachPendingServerProcess(latestServerLogPath) is { } pendingProcess)
		{
			try
			{
				return await WaitForServerPortAsync(pendingProcess, readinessGeneration, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				MarkLaunchFailed();
				return new SetupStepResult(false, WithServerLogHint("Server startup timed out or was canceled."), 0);
			}
		}

		if (!CanResolveExecutable(serverPython))
		{
			Log($"{ServerTag} Failed: Python executable was not found.");
			return new SetupStepResult(false, WithServerLogHint($"Python executable not found: {serverPython}"), 0);
		}

		if (!File.Exists(mainPy))
		{
			Log($"{ServerTag} Failed: ComfyUI main.py was not found.");
			return new SetupStepResult(false, WithServerLogHint($"ComfyUI main.py not found at: {mainPy}"), 0);
		}

		try
		{
			Log($"{ServerTag} Starting ComfyUI backend process on CUDA device {ConfiguredGpuId}...");

			string serverLogPath = SessionLogPaths.CreateSessionLogPath(logsDirectory, SessionLogPaths.ComfyServerPrefix);
			_currentServerLogPath = serverLogPath;
			Log($"{ServerTag} LogFile: {serverLogPath}");

			string args = string.Format(StartupArgsFormat, mainPy, ConfiguredListenAddress, ConfiguredPort, ConfiguredGpuId);
			await ProcessRunner.WaitForServerLogAppendAccessAsync(
				serverLogPath,
				TimeSpan.FromSeconds(5),
				TimeSpan.FromMilliseconds(100),
				cancellationToken);

			_serverProcess = ProcessRunner.StartWithFileLog(
				serverPython,
				args,
				serverLogPath,
				LogServerOutput,
				ServerLogTailInterval,
				out var logTail,
				comfyPath,
				new Dictionary<string, string>
				{
					["PYTHONUNBUFFERED"] = "1",
					["PYTHONIOENCODING"] = "utf-8",
					["PYTHONUTF8"] = "1"
				},
				latestServerLogPath,
				_serverProcesses.IsShuttingDown);
			ReplaceServerLogTail(logTail);
			_serverProcesses.Register(_serverProcess, serverLogPath);
			SessionLogPaths.PruneOldSessionLogs(logsDirectory, SessionLogPaths.ComfyServerPrefix);

			return await WaitForServerPortAsync(_serverProcess, readinessGeneration, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			MarkLaunchFailed();
			return new SetupStepResult(false, WithServerLogHint("Server startup timed out or was canceled."), 0);
		}
		catch (Exception ex)
		{
			MarkLaunchFailed();
			return new SetupStepResult(false, WithServerLogHint($"Failed to start server: {ex.Message}"), 0);
		}
	}

	private void EnsureConfiguredGpuSelection()
	{
		string originalGpuId = Settings.GpuId;
		string configuredGpuId = string.IsNullOrWhiteSpace(Settings.GpuId)
			? "0"
			: Settings.GpuId.Trim();

		Settings.GpuId = configuredGpuId;
		if (!string.Equals(originalGpuId, configuredGpuId, StringComparison.Ordinal))
		{
			_settingsService.Save();
		}

		if (configuredGpuId != "0")
		{
			Log($"{ServerTag} CUDA device {configuredGpuId} selected. ComfyUI will validate it during startup.");
		}
	}

	private void MarkLaunchSuccessful()
	{
		Settings.LastLaunchSuccessful = true;
		Settings.LastActivePort = ConfiguredPort;
		Settings.ActiveServerLaunchSettings = ServerLaunchSettingsSnapshot.FromSettings(Settings, _paths.ActiveComfyPath);
		_settingsService.Save();
	}

	private void MarkLaunchFailed()
	{
		Settings.LastLaunchSuccessful = false;
		_settingsService.Save();
	}

	private static string GetProbeAddress(string listenAddress)
	{
		if (string.IsNullOrWhiteSpace(listenAddress)) return "127.0.0.1";

		string normalized = listenAddress.Trim();
		return normalized is "0.0.0.0" or "::" or "*" ? "127.0.0.1" : normalized;
	}

	private string ResolveServerPythonExecutable()
	{
		string pythonExecutable;
		if (string.Equals(Settings.ServerPythonMode, PythonExecutionModes.ConfiguredPython, StringComparison.Ordinal))
		{
			pythonExecutable = string.IsNullOrWhiteSpace(Settings.PythonPath) ? "python" : Settings.PythonPath;
		}
		else
		{
			pythonExecutable = _paths.ActiveVenvPythonExe;
		}

		return ResolvePhysicalPath(pythonExecutable);
	}

	private static string ResolvePhysicalPath(string path)
		=> Path.IsPathRooted(path)
			? NexusToolingPathLeaseController.ResolvePhysicalPath(path)
			: path;

	private void EnsureNodes20Enabled(string comfyPath)
	{
		try
		{
			string settingsPath = Path.Combine(comfyPath, "user", "default", "comfy.settings.json");
			string? settingsDirectory = Path.GetDirectoryName(settingsPath);
			if (!string.IsNullOrWhiteSpace(settingsDirectory))
			{
				Directory.CreateDirectory(settingsDirectory);
			}

			JsonObject settings = ReadComfySettings(settingsPath);
			if (IsJsonBooleanTrue(settings[Nodes20EnabledSettingId]))
			{
				Log($"{ServerTag} Nodes 2.0 already enabled.");
				return;
			}

			settings[Nodes20EnabledSettingId] = true;
			File.WriteAllText(
				settingsPath,
				settings.ToJsonString(WriteIndentedJsonOptions));
			Log($"{ServerTag} Nodes 2.0 enabled in ComfyUI settings.");
		}
		catch (Exception ex)
		{
			Log($"{ServerTag} Warning: failed to enforce Nodes 2.0 setting: {ex.Message}");
		}
	}

	private static JsonObject ReadComfySettings(string settingsPath)
	{
		if (!File.Exists(settingsPath))
		{
			return new JsonObject();
		}

		try
		{
			return JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject();
		}
		catch (JsonException)
		{
			return new JsonObject();
		}
	}

	private static bool IsJsonBooleanTrue(JsonNode? node)
	{
		try
		{
			return node?.GetValue<bool>() == true;
		}
		catch
		{
			return false;
		}
	}

	private static bool CanResolveExecutable(string executable)
	{
		if (string.IsNullOrWhiteSpace(executable)) return false;
		if (File.Exists(executable)) return true;

		bool hasDirectory = executable.Contains(Path.DirectorySeparatorChar)
			|| executable.Contains(Path.AltDirectorySeparatorChar);
		return !hasDirectory;
	}

	private Process? TryAttachPendingServerProcess(string latestServerLogPath)
	{
		ComfyServerProcessInfo? processInfo = _serverProcesses.FindServerProcess();
		if (processInfo == null) return null;

		Process? process = _serverProcesses.TryGetProcess(processInfo);
		if (process == null) return null;

		_serverProcess = process;
		Log($"{ServerTag} Existing server launch process detected: {processInfo.ProcessName} ({processInfo.ProcessId}) via {processInfo.Source}.");
		Log($"{ServerTag} Attaching live log tail and waiting for startup completion.");
		string serverLogPath = !string.IsNullOrWhiteSpace(processInfo.LogPath)
			? processInfo.LogPath
			: latestServerLogPath;
		_currentServerLogPath = serverLogPath;
		Log($"{ServerTag} AttachedLogFile: {serverLogPath}");
		ReplaceServerLogTail(ProcessRunner.AttachLogTail(
			serverLogPath,
			process,
			LogServerOutput,
			ServerLogTailInterval,
			latestServerLogPath,
			_serverProcesses.IsShuttingDown));
		return process;
	}

	private async Task<SetupStepResult> WaitForServerPortAsync(
		Process process,
		long readinessGeneration,
		CancellationToken cancellationToken)
	{
		try
		{
			_lastServerStartupActivityAt = DateTimeOffset.UtcNow;
		Log($"{ServerTag} Waiting for server HTTP readiness ({ComfyApiOptions.GetLocalBaseUrl(Settings)}); idle timeout {StartupIdleTimeout.TotalSeconds:0}s...");
			NexusLog.Trace($"{ServerTag} Readiness loop started. generation={readinessGeneration}, pid={process.Id}");
			using var readinessTimer = new PeriodicTimer(ServerReadinessInterval);

			while (DateTimeOffset.UtcNow - _lastServerStartupActivityAt <= StartupIdleTimeout)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!IsCurrentReadiness(process, readinessGeneration))
				{
					NexusLog.Trace($"{ServerTag} Readiness result dropped for stale generation={readinessGeneration}.");
					return new SetupStepResult(false, "Server startup was superseded by a newer lifecycle operation.", 0);
				}

				RefreshServerLogActivityFromFile();

				if (!IsProcessRunning(process))
				{
					int exitCode = TryGetExitCode(process);
					Log($"{ServerTag} Error: Server process terminated unexpectedly (Exit Code: {exitCode}).");
					return new SetupStepResult(false, WithServerLogHint($"Server crashed during startup with exit code {exitCode}. Check logs for details."), 0);
				}

				if (await IsComfyHttpReadyAsync(cancellationToken))
				{
					if (!IsCurrentReadiness(process, readinessGeneration))
					{
						NexusLog.Trace($"{ServerTag} Ready response dropped for stale generation={readinessGeneration}.");
						return new SetupStepResult(false, "Server startup was superseded by a newer lifecycle operation.", 0);
					}

					Log($"{ServerTag} Server HTTP readiness confirmed at {ComfyApiOptions.GetLocalBaseUrl(Settings)}.");
					MarkLaunchSuccessful();
					return new SetupStepResult(true, $"{ServerTag} Server started successfully.", 1);
				}

				if (!await readinessTimer.WaitForNextTickAsync(cancellationToken))
				{
					break;
				}
			}

			if (IsProcessRunning(process))
			{
				Log($"{ServerTag} Timeout: process is still running, but no startup log activity was detected for {StartupIdleTimeout.TotalSeconds:0}s and HTTP readiness was not confirmed.");
			}

			MarkLaunchFailed();
			return new SetupStepResult(false, WithServerLogHint($"Server HTTP readiness failed after startup became idle: {ComfyApiOptions.GetLocalBaseUrl(Settings)}"), 0);
		}
		finally
		{
			DisposeServerLogTail();
		}
	}

	private bool IsCurrentReadiness(Process process, long readinessGeneration)
		=> readinessGeneration == Volatile.Read(ref _readinessGeneration)
			&& ReferenceEquals(_serverProcess, process)
			&& IsProcessRunning(process);

	private async Task<bool> IsComfyHttpReadyAsync(CancellationToken cancellationToken)
	{
		string baseUrl = ComfyApiOptions.GetLocalBaseUrl(Settings).TrimEnd('/');
		Uri[] endpoints =
		[
			new Uri($"{baseUrl}/system_stats"),
			new Uri($"{baseUrl}/"),
		];

		foreach (Uri endpoint in endpoints)
		{
			Task<LocalHttpProbeResult> probeTask = LocalServerProbe.TryGetAsync(endpoint, cancellationToken);
			LocalHttpProbeResult result = await NexusSoftTimeout.AwaitAsync(
				probeTask,
				ReadinessPendingThreshold,
				() => Log($"{ServerTag} API response pending at {endpoint}; continuing to wait for this request."));
			if (result.StatusCode is { } statusCode && (int)statusCode is >= 200 and < 500)
			{
				return true;
			}
		}

		return false;
	}

	private void LogServerOutput(string message)
	{
		_lastServerStartupActivityAt = DateTimeOffset.UtcNow;
		Log(message);
	}

	private void RefreshServerLogActivityFromFile()
	{
		if (string.IsNullOrWhiteSpace(_currentServerLogPath) || !File.Exists(_currentServerLogPath))
		{
			return;
		}

		try
		{
			DateTimeOffset lastWrite = File.GetLastWriteTimeUtc(_currentServerLogPath);
			if (lastWrite > _lastServerStartupActivityAt)
			{
				_lastServerStartupActivityAt = lastWrite;
			}
		}
		catch
		{
		}
	}

	private void ReplaceServerLogTail(IDisposable logTail)
	{
		DisposeServerLogTail();
		_serverLogTail = logTail;
	}

	private void DisposeServerLogTail()
	{
		_serverLogTail?.Dispose();
		_serverLogTail = null;
	}

	private static bool IsProcessRunning(Process process)
	{
		try
		{
			return !process.HasExited;
		}
		catch
		{
			return false;
		}
	}

	private static int TryGetExitCode(Process process)
	{
		try
		{
			return process.ExitCode;
		}
		catch
		{
			return -1;
		}
	}

	private string WithServerLogHint(string message)
	{
		if (string.IsNullOrWhiteSpace(_currentServerLogPath))
		{
			return message;
		}

		return $"{message} Log: {_currentServerLogPath}";
	}
}
