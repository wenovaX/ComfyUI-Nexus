namespace ComfyUI_Nexus.Setup.Services;

using System.IO;
using ComfyUI_Nexus.Setup.Models;

internal sealed class CoreLinkDetector
{
	internal Action<string>? OnMessage { get; set; }

	private void Log(string message) => OnMessage?.Invoke(message);

	internal Task<SetupStepResult> RunWelcomeAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Log("Welcome to Nexus Command Center.");
		return Task.FromResult(new SetupStepResult(true, "Command Center initialized.", 1));
	}

	internal async Task<SetupStepResult> CheckAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Log("[CoreCheck] Scanning for fundamental environment tools...");

		try
		{
			// 1. Resolve Git
			Log("[CoreCheck] Resolving Git...");
			string? gitExe = await ResolveGitAsync(cancellationToken);
			if (gitExe == null)
			{
				Log("[CoreCheck] Git missing. Extracting portable Git...");
				await ComfyInstallService.Instance.ExtractGitPackageAsync(cancellationToken);
				gitExe = await ResolveGitAsync(cancellationToken);
			}

			if (gitExe == null) return new SetupStepResult(false, "Failed to resolve or extract Git.", 0);
			Log($"[CoreCheck] Git ready: {gitExe}");

			// 2. Resolve Python
			Log("[CoreCheck] Resolving Python...");
			string? pythonExe = await ResolvePythonAsync(cancellationToken);
			if (pythonExe == null)
			{
				Log("[CoreCheck] Python missing. Installing local Python runtime...");
				await ComfyInstallService.Instance.ExtractPythonPackageAsync(cancellationToken);
				pythonExe = await ResolvePythonAsync(cancellationToken);
			}

			if (pythonExe == null) return new SetupStepResult(false, "Failed to resolve or install Python runtime.", 0);
			Log($"[CoreCheck] Python ready: {pythonExe}");

			return new SetupStepResult(true, "Core tools (Git & Python) are verified and ready.", 1);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Environment check failed: {ex.Message}", 0);
		}
	}

	private async Task<string?> ResolveGitAsync(CancellationToken cancellationToken)
	{
		// Try Portable First as per user intent for "Portable"
		string portable = Path.Combine(ComfyInstallService.InstalledPath, "Git", "cmd", "git.exe");
		if (File.Exists(portable)) return portable;

		if (!ComfyInstallService.PortableOnly)
		{
			try
			{
				var (code, _, _) = await Runtime.ProcessRunner.RunAsync("git", "--version", null, null, cancellationToken);
				if (code == 0) return "git";
			}
			catch { }
		}
		return null;
	}

	private async Task<string?> ResolvePythonAsync(CancellationToken cancellationToken)
	{
		string portable = ComfyInstallService.PythonExe;
		if (File.Exists(portable)) return portable;
		return null;
	}
}
