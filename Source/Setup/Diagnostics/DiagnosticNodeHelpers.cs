namespace ComfyUI_Nexus.Setup.Diagnostics;

using ComfyUI_Nexus.Setup.Runtime;

internal static class DiagnosticNodeHelpers
{
	internal const string SystemOption = "system";
	internal const string BuiltInOption = "builtin";
	internal const string CustomOption = "custom";
	internal const string KeepOption = "keep";

	internal static DiagnosticOption CreateOption(string id, string displayName, string description = "", bool isRecommended = false)
		=> new()
		{
			Id = id,
			DisplayName = displayName,
			Description = description,
			IsRecommended = isRecommended
		};

	internal static string ParsePackageVersion(string packageName)
	{
		var match = System.Text.RegularExpressions.Regex.Match(packageName, @"(\d+\.\d+[\.\d]*)");
		return match.Success ? match.Groups[1].Value : "unknown";
	}

	internal static async Task<string?> TryGetCommandVersionAsync(
		string executable,
		string arguments,
		string versionPrefix,
		CancellationToken cancellationToken)
	{
		try
		{
			var (code, stdout, stderr) = await ProcessRunner.RunAsync(executable, arguments, null, null, cancellationToken);
			string output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
			if (code != 0 || string.IsNullOrWhiteSpace(output)) return null;

			return output.Trim().Replace(versionPrefix, "", StringComparison.OrdinalIgnoreCase);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return null;
		}
	}

	internal static async Task<string> TryGetGitRevisionAsync(string path, string gitExecutable, CancellationToken cancellationToken)
	{
		if (!Directory.Exists(Path.Combine(path, ".git"))) return "unknown";

		try
		{
			var (code, stdout, _) = await ProcessRunner.RunAsync(gitExecutable, "rev-parse --short HEAD", path, null, cancellationToken);
			return code == 0 && !string.IsNullOrWhiteSpace(stdout)
				? stdout.Trim()
				: "unknown";
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return "unknown";
		}
	}
}
