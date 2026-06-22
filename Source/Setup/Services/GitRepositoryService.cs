namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed class GitRepositoryService
{
	private readonly Action<string> _log;

	internal GitRepositoryService(Action<string> log)
	{
		_log = log;
	}

	internal async Task<SetupStepResult> SyncComfyRepoAsync(string gitExe, string tag, CancellationToken cancellationToken)
		=> await SyncGenericRepoAsync(gitExe, SetupSettingsService.Instance.Settings.ComfyRepoUrl, ComfyInstallService.ComfyPath, tag, cancellationToken);

	internal async Task<SetupStepResult> PullFastForwardAsync(string gitExe, string path, string tag, CancellationToken cancellationToken)
	{
		_log($"{tag} Checking repository update...");
		var result = await RunGitAsync(gitExe, "pull --ff-only", path, cancellationToken);
		if (result.ExitCode != 0)
		{
			string detail = GetGitErrorDetail(result.Output, result.Error);
			return new SetupStepResult(false, $"{tag} Update failed: {detail}", 0);
		}

		return new SetupStepResult(true, $"{tag} Repository updated.", 1);
	}

	internal async Task<SetupStepResult> EnsureRepoPresentAsync(string gitExe, string url, string path, string tag, CancellationToken cancellationToken)
	{
		if (Directory.Exists(path))
		{
			_log($"{tag} Existing custom node detected. Skipping update: {path}");
			return new SetupStepResult(true, $"{tag} Existing custom node detected.", 1);
		}

		_log($"{tag} Missing custom node. Cloning from {url}");
		var (code, _, error) = await RunGitAsync(gitExe, $"clone {url} \"{path}\"", null, cancellationToken);
		return new SetupStepResult(code == 0, code == 0 ? $"{tag} Clone successful." : $"Clone failed: {error}", code == 0 ? 1 : 0);
	}

	internal async Task<SetupStepResult> EnsureRepoSyncedAsync(
		string gitExe,
		string url,
		string path,
		string tag,
		bool forceSyncExisting,
		CancellationToken cancellationToken)
	{
		if (!forceSyncExisting)
		{
			return await EnsureRepoPresentAsync(gitExe, url, path, tag, cancellationToken);
		}

		return await SyncGenericRepoAsync(gitExe, url, path, tag, cancellationToken);
	}

	internal async Task<GitRepositoryInspection> InspectRepositoryAsync(
		string gitExe,
		string expectedUrl,
		string path,
		CancellationToken cancellationToken)
	{
		if (!Directory.Exists(path))
		{
			return GitRepositoryInspection.Missing(path);
		}

		bool hasGitMarker = Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));
		if (!hasGitMarker)
		{
			return GitRepositoryInspection.Invalid(path, "The folder is not a git repository.");
		}

		var (topCode, topOutput, topError) = await RunGitAsync(gitExe, "rev-parse --show-toplevel", path, cancellationToken);
		if (topCode != 0)
		{
			return GitRepositoryInspection.Invalid(path, $"Unable to resolve repository root. {GetGitErrorDetail(topOutput, topError)}");
		}

		string expectedRoot = NormalizePath(path);
		string actualRoot = NormalizePath(topOutput.Trim());
		if (!string.Equals(expectedRoot, actualRoot, StringComparison.OrdinalIgnoreCase))
		{
			return GitRepositoryInspection.Invalid(
				path,
				$"Git resolved a different repository root. expected='{expectedRoot}', actual='{actualRoot}'");
		}

		var (urlCode, urlOutput, urlError) = await RunGitAsync(gitExe, "config --get remote.origin.url", path, cancellationToken);
		if (urlCode != 0)
		{
			return GitRepositoryInspection.Invalid(path, $"Unable to resolve origin URL. {GetGitErrorDetail(urlOutput, urlError)}");
		}

		string actualUrl = urlOutput.Trim();
		if (!RepositoryUrlsMatch(expectedUrl, actualUrl))
		{
			return GitRepositoryInspection.Invalid(
				path,
				$"Repository origin mismatch. expected='{expectedUrl}', actual='{actualUrl}'");
		}

		return GitRepositoryInspection.Valid(path, actualRoot, actualUrl);
	}

	internal async Task<(string? Exe, string Version)> ResolvePortableGitAsync(CancellationToken cancellationToken)
	{
		string portable = Path.Combine(ComfyInstallService.InstalledPath, "Git", "cmd", "git.exe");
		if (File.Exists(portable))
		{
			var (hasGit, version) = await CheckGitAsync(portable, cancellationToken);
			if (hasGit) return (portable, version);
		}

		return (null, string.Empty);
	}

	internal async Task<(string? Exe, string Version)> ResolveConfiguredGitAsync(CancellationToken cancellationToken)
	{
		string configured = SetupSettingsService.Instance.Settings.GitPath;
		if (!string.IsNullOrWhiteSpace(configured))
		{
			var (hasConfiguredGit, configuredVersion) = await CheckGitAsync(configured, cancellationToken);
			if (hasConfiguredGit) return (configured, configuredVersion);
		}

		return await ResolvePortableGitAsync(cancellationToken);
	}

	private async Task<SetupStepResult> SyncGenericRepoAsync(string gitExe, string url, string path, string tag, CancellationToken cancellationToken)
	{
		var inspection = await InspectRepositoryAsync(gitExe, url, path, cancellationToken);
		if (inspection.IsValid)
		{
			_log($"{tag} Updating existing repository...");
			var fetchResult = await RunGitAsync(gitExe, "fetch origin", path, cancellationToken);
			if (fetchResult.ExitCode != 0)
			{
				string detail = GetGitErrorDetail(fetchResult.Output, fetchResult.Error);
				_log($"{tag} Fetch failed: {detail}");
				return new SetupStepResult(false, $"Sync failed: {detail}", 0);
			}

			string? upstreamRef = await ResolvePreferredUpstreamRefAsync(gitExe, path, cancellationToken);
			if (string.IsNullOrWhiteSpace(upstreamRef))
			{
				return new SetupStepResult(false, "Sync failed: unable to resolve repository upstream branch.", 0);
			}

			var (code, output, error) = await RunGitAsync(gitExe, $"reset --hard {upstreamRef}", path, cancellationToken);
			if (code != 0)
			{
				_log($"{tag} Reset failed: {GetGitErrorDetail(output, error)}");
			}

			return new SetupStepResult(
				code == 0,
				code == 0 ? $"{tag} Sync successful." : $"Sync failed: {GetGitErrorDetail(output, error)}",
				code == 0 ? 1 : 0);
		}

		if (inspection.Exists)
		{
			_log($"{tag} Existing folder is not the expected repository: {inspection.Reason}");
			return new SetupStepResult(false, $"Sync failed: existing folder is not the expected repository. {inspection.Reason}", 0);
		}

		_log($"{tag} Cloning from {url}");
		var (cloneCode, _, cloneError) = await RunGitAsync(gitExe, $"clone {url} \"{path}\"", null, cancellationToken);
		return new SetupStepResult(cloneCode == 0, cloneCode == 0 ? $"{tag} Clone successful." : $"Clone failed: {cloneError}", cloneCode == 0 ? 1 : 0);
	}

	private static async Task<(int ExitCode, string Output, string Error)> RunGitAsync(
		string gitExe,
		string arguments,
		string? workingDirectory,
		CancellationToken cancellationToken)
		=> await ProcessRunner.RunAsync(gitExe, arguments, workingDirectory, null, cancellationToken);

	private static string GetGitErrorDetail(string output, string error)
	{
		string detail = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
		return string.IsNullOrWhiteSpace(detail) ? "Git returned a non-zero exit code." : detail;
	}

	private static async Task<string?> ResolvePreferredUpstreamRefAsync(
		string gitExe,
		string path,
		CancellationToken cancellationToken)
	{
		string? defaultBranch = await ResolveOriginHeadRefAsync(gitExe, path, cancellationToken);
		if (!string.IsNullOrWhiteSpace(defaultBranch)) return defaultBranch;

		string? currentUpstream = await ResolveCurrentUpstreamRefAsync(gitExe, path, cancellationToken);
		if (!string.IsNullOrWhiteSpace(currentUpstream)) return currentUpstream;

		foreach (string fallback in new[] { "origin/main", "origin/master" })
		{
			if (await RemoteRefExistsAsync(gitExe, path, fallback, cancellationToken))
			{
				return fallback;
			}
		}

		return null;
	}

	private static async Task<string?> ResolveOriginHeadRefAsync(
		string gitExe,
		string path,
		CancellationToken cancellationToken)
	{
		var (code, output, _) = await RunGitAsync(gitExe, "symbolic-ref --quiet --short refs/remotes/origin/HEAD", path, cancellationToken);
		if (code == 0)
		{
			string refName = output.Trim();
			if (!string.IsNullOrWhiteSpace(refName)) return refName;
		}

		await RunGitAsync(gitExe, "remote set-head origin --auto", path, cancellationToken);
		(code, output, _) = await RunGitAsync(gitExe, "symbolic-ref --quiet --short refs/remotes/origin/HEAD", path, cancellationToken);
		if (code != 0) return null;

		string resolvedRef = output.Trim();
		return string.IsNullOrWhiteSpace(resolvedRef) ? null : resolvedRef;
	}

	private static async Task<string?> ResolveCurrentUpstreamRefAsync(
		string gitExe,
		string path,
		CancellationToken cancellationToken)
	{
		var (code, output, _) = await RunGitAsync(gitExe, "rev-parse --abbrev-ref --symbolic-full-name @{u}", path, cancellationToken);
		if (code != 0) return null;

		string refName = output.Trim();
		return string.IsNullOrWhiteSpace(refName) ? null : refName;
	}

	private static async Task<bool> RemoteRefExistsAsync(
		string gitExe,
		string path,
		string refName,
		CancellationToken cancellationToken)
	{
		var (code, _, _) = await RunGitAsync(gitExe, $"rev-parse --verify --quiet {refName}", path, cancellationToken);
		return code == 0;
	}

	private static async Task<(bool IsSuccess, string Version)> CheckGitAsync(string gitExe, CancellationToken cancellationToken)
	{
		try
		{
			var (exitCode, output, _) = await ProcessRunner.RunAsync(gitExe, "--version", null, null, cancellationToken);
			return (exitCode == 0, output.Trim());
		}
		catch
		{
			return (false, string.Empty);
		}
	}

	private static string NormalizePath(string path)
		=> Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private static bool RepositoryUrlsMatch(string expected, string actual)
		=> string.Equals(NormalizeRepositoryUrl(expected), NormalizeRepositoryUrl(actual), StringComparison.OrdinalIgnoreCase);

	private static string NormalizeRepositoryUrl(string url)
	{
		string normalized = (url ?? string.Empty).Trim().TrimEnd('/');
		return normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
			? normalized[..^4]
			: normalized;
	}
}

internal sealed record GitRepositoryInspection(
	bool Exists,
	bool IsValid,
	string Path,
	string RootPath,
	string OriginUrl,
	string Reason)
{
	internal static GitRepositoryInspection Missing(string path)
		=> new(false, false, path, string.Empty, string.Empty, "Repository folder is missing.");

	internal static GitRepositoryInspection Invalid(string path, string reason)
		=> new(true, false, path, string.Empty, string.Empty, reason);

	internal static GitRepositoryInspection Valid(string path, string rootPath, string originUrl)
		=> new(true, true, path, rootPath, originUrl, string.Empty);
}
