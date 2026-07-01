namespace ComfyUI_Nexus.Setup.Services;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Settings;

internal sealed class ComfyServerProcessService
{
	private const string ServerTag = "[Server]";
	private const string Nodes20EnabledSettingId = "Comfy.VueNodes.Enabled";
	private static SetupSettings Settings => SetupSettingsService.Instance.Settings;
	private static int ConfiguredPort => Settings.ServerPort;
	private static string ConfiguredListenAddress => Settings.ListenAddress;
	private static string ConfiguredProbeAddress => GetProbeAddress(ConfiguredListenAddress);
	private static string ConfiguredGpuId => Settings.GpuId;
	private static TimeSpan StartupTimeout => TimeSpan.FromSeconds(Math.Max(180, Settings.ServerStartupTimeoutSeconds));
	private static TimeSpan PortProbeInterval => TimeSpan.FromMilliseconds(Math.Max(50, Settings.PortProbeIntervalMilliseconds));
	private static TimeSpan ServerLogTailInterval => TimeSpan.FromMilliseconds(Math.Max(50, Settings.ServerLogTailIntervalMilliseconds));

	private const string StartupArgsFormat = "-u \"{0}\" --listen {1} --port {2} --highvram --fp16-unet --fp16-text-enc --force-fp16 --use-pytorch-cross-attention --cuda-device {3} --preview-method none";

	private Process? _serverProcess;
	private IDisposable? _serverLogTail;
	private string? _currentServerLogPath;

	internal Action<string>? OnMessage { get; set; }

	private void Log(string message) => OnMessage?.Invoke(message);

	internal async Task<SetupStepResult> StartAndWaitAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		string serverPython = ResolveServerPythonExecutable();
		string comfyPath = ComfyPathResolver.ResolveActiveComfyPath();
		string mainPy = Path.Combine(comfyPath, "main.py");
		string logsDirectory = ComfyInstallService.GetLocalRuntimePath("Logs");
		string latestServerLogPath = SessionLogPaths.GetLatestLogPath(logsDirectory, SessionLogPaths.ComfyServerLatestFileName);
		Settings.ServerLogFile = $"Logs/{SessionLogPaths.ComfyServerLatestFileName}";
		_currentServerLogPath = null;

		Log($"{ServerTag} Initializing server launch sequence...");
		await EnsureValidGpuSelectionAsync(cancellationToken);
		EnsureNodes20Enabled(comfyPath);
		Log($"{ServerTag} PythonMode: {Settings.ServerPythonMode}");
		Log($"{ServerTag} Python: {serverPython}");
		Log($"{ServerTag} WorkDir: {comfyPath}");
		Log($"{ServerTag} Entry: {mainPy}");
		Log($"{ServerTag} Port: {ConfiguredPort}");
		Log($"{ServerTag} ProbeHost: {ConfiguredProbeAddress}");
		Log($"{ServerTag} CUDA Device: {ConfiguredGpuId}");
		Log($"{ServerTag} StartupTimeout: {StartupTimeout.TotalSeconds:0}s");
		Log($"{ServerTag} LatestLog: {latestServerLogPath}");

		if (await PortProbeService.WaitUntilHttpReadyAsync(ComfyApiOptions.LocalBaseUrl, PortProbeInterval, PortProbeInterval, cancellationToken))
		{
			Log($"{ServerTag} Server HTTP endpoint is already ready on {ComfyApiOptions.LocalBaseUrl}. Skipping launch.");
			MarkLaunchSuccessful();
			return new SetupStepResult(true, "Server already running.", 1);
		}

		if (TryAttachPendingServerProcess(latestServerLogPath) is { } pendingProcess)
		{
			try
			{
				return await WaitForServerPortAsync(pendingProcess, cancellationToken);
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
				msg => Log(msg),
				ServerLogTailInterval,
				out var logTail,
				comfyPath,
				new Dictionary<string, string>
				{
					["PYTHONUNBUFFERED"] = "1",
					["PYTHONIOENCODING"] = "utf-8",
					["PYTHONUTF8"] = "1"
				},
				latestServerLogPath);
			ReplaceServerLogTail(logTail);
			ComfyServerProcessRegistry.Register(_serverProcess, serverLogPath);
			SessionLogPaths.PruneOldSessionLogs(logsDirectory, SessionLogPaths.ComfyServerPrefix);

			return await WaitForServerPortAsync(_serverProcess, cancellationToken);
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

	private async Task EnsureValidGpuSelectionAsync(CancellationToken cancellationToken)
	{
		string originalGpuId = Settings.GpuId;
		string configuredGpuId = string.IsNullOrWhiteSpace(Settings.GpuId)
			? "0"
			: Settings.GpuId.Trim();

		if (configuredGpuId == "0")
		{
			Settings.GpuId = configuredGpuId;
			if (!string.Equals(originalGpuId, configuredGpuId, StringComparison.Ordinal))
			{
				SetupSettingsService.Instance.Save();
			}

			return;
		}

		Log($"{ServerTag} Probing CUDA devices before launch...");
		IReadOnlyList<GpuDeviceInfo> devices = await GpuDiscoveryService.DiscoverAsync(cancellationToken);
		if (IsConfiguredGpuAvailable(configuredGpuId, devices))
		{
			Settings.GpuId = configuredGpuId;
			if (!string.Equals(originalGpuId, configuredGpuId, StringComparison.Ordinal))
			{
				SetupSettingsService.Instance.Save();
			}

			Log($"{ServerTag} CUDA device {configuredGpuId} verified ({devices.Count} detected).");
			return;
		}

		Log($"{ServerTag} CUDA device {configuredGpuId} is unavailable ({devices.Count} detected). Falling back to CUDA device 0.");
		Settings.GpuId = "0";
		Settings.LastLaunchSuccessful = false;
		SetupSettingsService.Instance.Save();
	}

	private static bool IsConfiguredGpuAvailable(string configuredGpuId, IReadOnlyList<GpuDeviceInfo> devices)
	{
		if (devices.Any(device => string.Equals(device.Id, configuredGpuId, StringComparison.Ordinal)))
		{
			return true;
		}

		return int.TryParse(configuredGpuId, out int configuredIndex)
			&& configuredIndex >= 0
			&& configuredIndex < devices.Count;
	}

	private static void MarkLaunchSuccessful()
	{
		Settings.LastLaunchSuccessful = true;
		Settings.LastActivePort = ConfiguredPort;
		Settings.ActiveServerLaunchSettings = ServerLaunchSettingsSnapshot.FromSettings(Settings, ComfyPathResolver.ResolveActiveComfyPath());
		SetupSettingsService.Instance.Save();
	}

	private static void MarkLaunchFailed()
	{
		Settings.LastLaunchSuccessful = false;
		SetupSettingsService.Instance.Save();
	}

	private static string GetProbeAddress(string listenAddress)
	{
		if (string.IsNullOrWhiteSpace(listenAddress)) return "127.0.0.1";

		string normalized = listenAddress.Trim();
		return normalized is "0.0.0.0" or "::" or "*" ? "127.0.0.1" : normalized;
	}

	private static string ResolveServerPythonExecutable()
	{
		if (string.Equals(Settings.ServerPythonMode, PythonExecutionModes.ConfiguredPython, StringComparison.Ordinal))
		{
			return string.IsNullOrWhiteSpace(Settings.PythonPath) ? "python" : Settings.PythonPath;
		}

		return ComfyPathResolver.ResolveActiveVenvPythonExe();
	}

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
				settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
		ComfyServerProcessInfo? processInfo = ComfyServerProcessRegistry.FindServerProcess();
		if (processInfo == null) return null;

		Process? process = ComfyServerProcessRegistry.TryGetProcess(processInfo);
		if (process == null) return null;

		_serverProcess = process;
		Log($"{ServerTag} Existing server launch process detected: {processInfo.ProcessName} ({processInfo.ProcessId}) via {processInfo.Source}.");
		Log($"{ServerTag} Attaching live log tail and waiting for startup completion.");
		string serverLogPath = !string.IsNullOrWhiteSpace(processInfo.LogPath)
			? processInfo.LogPath
			: latestServerLogPath;
		_currentServerLogPath = serverLogPath;
		Log($"{ServerTag} AttachedLogFile: {serverLogPath}");
		ReplaceServerLogTail(ProcessRunner.AttachLogTail(serverLogPath, process, msg => Log(msg), ServerLogTailInterval, latestServerLogPath));
		return process;
	}

	private async Task<SetupStepResult> WaitForServerPortAsync(Process process, CancellationToken cancellationToken)
	{
		try
		{
			Log($"{ServerTag} Waiting for server HTTP readiness ({ComfyApiOptions.LocalBaseUrl})...");

			DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);

			while (DateTimeOffset.UtcNow < deadline)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (!IsProcessRunning(process))
				{
					int exitCode = TryGetExitCode(process);
					Log($"{ServerTag} Error: Server process terminated unexpectedly (Exit Code: {exitCode}).");
					return new SetupStepResult(false, WithServerLogHint($"Server crashed during startup with exit code {exitCode}. Check logs for details."), 0);
				}

				if (await PortProbeService.WaitUntilHttpReadyAsync(ComfyApiOptions.LocalBaseUrl, PortProbeInterval, PortProbeInterval, cancellationToken))
				{
					Log($"{ServerTag} Server HTTP readiness confirmed at {ComfyApiOptions.LocalBaseUrl}.");
					MarkLaunchSuccessful();
					return new SetupStepResult(true, $"{ServerTag} Server started successfully.", 1);
				}

				await Task.Delay(PortProbeInterval, cancellationToken);
			}

			if (IsProcessRunning(process))
			{
				Log($"{ServerTag} Timeout: process is still running, but HTTP readiness was not confirmed within {StartupTimeout.TotalSeconds:0}s.");
			}

			MarkLaunchFailed();
			return new SetupStepResult(false, WithServerLogHint($"Server HTTP readiness failed within timeout: {ComfyApiOptions.LocalBaseUrl}"), 0);
		}
		finally
		{
			DisposeServerLogTail();
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
