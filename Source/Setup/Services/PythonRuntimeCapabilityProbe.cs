namespace ComfyUI_Nexus.Setup.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using ComfyUI_Nexus.Setup.Runtime;

/// <summary>
/// Verifies that a Python executable can run and expose pip for Nexus setup work.
/// </summary>
internal static class PythonRuntimeCapabilityProbe
{
	internal static async Task<PythonRuntimeCapability> ProbeAsync(
		string executablePath,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(executablePath))
		{
			return PythonRuntimeCapability.Unavailable;
		}

		try
		{
			var (versionExitCode, versionOutput, versionError) = await ProcessRunner.RunAsync(
				executablePath,
				"--version",
				null,
				null,
				cancellationToken);
			string version = SelectOutput(versionOutput, versionError);
			if (versionExitCode != 0 || string.IsNullOrWhiteSpace(version))
			{
				return PythonRuntimeCapability.Unavailable;
			}

			var (environmentExitCode, environmentOutput, _) = await ProcessRunner.RunAsync(
				executablePath,
				"-c \"import sys,sysconfig; print(sys.executable); print(sys.prefix); print(sysconfig.get_platform())\"",
				null,
				null,
				cancellationToken);
			if (environmentExitCode != 0 || !TryReadEnvironment(environmentOutput, out string resolvedExecutablePath, out string environmentDetails))
			{
				return PythonRuntimeCapability.Unavailable;
			}

			if (IsUnsupportedHostEnvironment(environmentDetails))
			{
				return new PythonRuntimeCapability(
					PythonRuntimeCapabilityStatus.UnsupportedHostEnvironment,
					NormalizeVersion(version),
					string.Empty,
					resolvedExecutablePath);
			}

			var (pipExitCode, pipOutput, pipError) = await ProcessRunner.RunAsync(
				executablePath,
				"-m pip --version",
				null,
				null,
				cancellationToken);
			string pipVersion = SelectOutput(pipOutput, pipError);
			if (pipExitCode != 0 || string.IsNullOrWhiteSpace(pipVersion))
			{
				return new PythonRuntimeCapability(
					PythonRuntimeCapabilityStatus.PipUnavailable,
					NormalizeVersion(version),
					string.Empty,
					resolvedExecutablePath);
			}

			return new PythonRuntimeCapability(
				PythonRuntimeCapabilityStatus.Ready,
				NormalizeVersion(version),
				pipVersion,
				resolvedExecutablePath);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return PythonRuntimeCapability.Unavailable;
		}
	}

	private static string SelectOutput(string stdout, string stderr)
		=> (string.IsNullOrWhiteSpace(stdout) ? stderr : stdout).Trim();

	private static string NormalizeVersion(string output)
		=> output.Replace("Python ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

	private static bool IsUnsupportedHostEnvironment(string output)
	{
		string normalized = output.Replace('\\', '/').Trim();
		return normalized.Contains("/msys", StringComparison.OrdinalIgnoreCase)
			|| normalized.Contains("/mingw", StringComparison.OrdinalIgnoreCase)
			|| normalized.Contains("mingw_", StringComparison.OrdinalIgnoreCase)
			|| normalized.Contains("ucrt", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryReadEnvironment(string output, out string executablePath, out string environmentDetails)
	{
		string[] lines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (lines.Length < 3 || string.IsNullOrWhiteSpace(lines[0]))
		{
			executablePath = string.Empty;
			environmentDetails = string.Empty;
			return false;
		}

		executablePath = lines[0];
		environmentDetails = string.Join(Environment.NewLine, lines.Skip(1));
		return true;
	}
}

internal enum PythonRuntimeCapabilityStatus
{
	Ready,
	PipUnavailable,
	UnsupportedHostEnvironment,
	Unavailable
}

internal sealed record PythonRuntimeCapability(
	PythonRuntimeCapabilityStatus Status,
	string Version,
	string PipVersion,
	string ExecutablePath)
{
	internal static PythonRuntimeCapability Unavailable { get; } = new(PythonRuntimeCapabilityStatus.Unavailable, string.Empty, string.Empty, string.Empty);

	internal bool IsReady => Status == PythonRuntimeCapabilityStatus.Ready;
}
