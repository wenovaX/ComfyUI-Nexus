namespace ComfyUI_Nexus.Setup.Services;

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComfyUI_Nexus.Setup.Runtime;
#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
#endif

internal sealed class RuntimePackageService
{
	private const string ExpectedPortableGitFile = "PortableGit-2.54.0-64-bit.7z.exe";
	private const string ExpectedPortableGitSigner = "Johannes Schindelin";
	private const string ExpectedPortableGitSha256 = "BEA006A6CC69673F27B1647E84AB3A68E912FBC175AB6320C5987E012897F311";
	private static readonly TimeSpan PortableGitExtractTimeout = TimeSpan.FromMinutes(3);

	private readonly Action<string> _log;

	internal RuntimePackageService(Action<string> log)
	{
		_log = log;
	}

	internal async Task ExtractGitPackageAsync(CancellationToken cancellationToken)
	{
		var packageSpec = RuntimePackageSpecService.Load();
		string gitPackage = ResolveGitPackagePath(packageSpec);
		_log($"[Runtime] Git package: {gitPackage}");
		if (!File.Exists(gitPackage))
		{
			throw new FileNotFoundException("Git package missing in Packages folder.");
		}

		string destinationPath = Path.Combine(ComfyInstallService.InstalledPath, "Git");
		if (IsPortableGitPackage(gitPackage))
		{
			await ExtractPortableGitPackageAsync(gitPackage, destinationPath, cancellationToken);
			return;
		}

		await ReplaceDirectoryAsync(destinationPath, cancellationToken);
		ValidateMinGitPackage(gitPackage, packageSpec.Git);
		await ExtractZipAsync(gitPackage, destinationPath, cancellationToken);
		await VerifyInstalledGitAsync(destinationPath, cancellationToken);
		SetupSettingsService.Instance.Settings.GitPath = Path.Combine(destinationPath, "cmd", "git.exe");
		SetupSettingsService.Instance.Save();
	}

	private static string ResolveGitPackagePath(RuntimePackageSpec packageSpec)
	{
		string packageRoot = Path.Combine(ComfyInstallService.LocalRuntimePath, "Packages", packageSpec.Git.Folder);
		string expectedPath = Path.Combine(packageRoot, packageSpec.Git.File);
		return expectedPath;
	}

	internal async Task ExtractPythonPackageAsync(CancellationToken cancellationToken)
	{
		var packageSpec = RuntimePackageSpecService.Load();
		string pythonPackage = packageSpec.GetPythonPackagePath(ComfyInstallService.RootPath);
		_log($"[Runtime] Python package: {pythonPackage}");
		if (!File.Exists(pythonPackage))
		{
			throw new FileNotFoundException("Python package missing in Packages folder.");
		}

		await InstallPythonRuntimeArchiveAsync(ComfyInstallService.PythonPath, pythonPackage, packageSpec.Python, cancellationToken);
	}

	internal async Task ExtractComfyZipDirectlyAsync(
		string zipPath,
		string targetPath,
		RuntimeOptionalPackageSpec packageSpec,
		CancellationToken cancellationToken)
	{
		ValidateComfyPackage(zipPath, packageSpec);

		string tempDir = Path.Combine(Path.GetTempPath(), $"Nexus-Zip-{Guid.NewGuid():N}");
		try
		{
			Directory.CreateDirectory(tempDir);
			await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir, true), cancellationToken);
			string[] subDirs = Directory.GetDirectories(tempDir);
			string sourcePath = subDirs.Length == 1 ? subDirs[0] : tempDir;
			await Task.Run(() => CopyDirectory(sourcePath, targetPath), cancellationToken);
		}
		finally
		{
			await DeleteDirectoryIfExistsAsync(tempDir);
		}
	}

	private static void ValidateComfyPackage(string packagePath, RuntimeOptionalPackageSpec packageSpec)
	{
		if (!File.Exists(packagePath))
		{
			throw new FileNotFoundException("ComfyUI source package was not found.", packagePath);
		}

		if (!string.Equals(Path.GetFileName(packagePath), packageSpec.File, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Refusing unexpected ComfyUI source package: {Path.GetFileName(packagePath)}");
		}

		if (string.IsNullOrWhiteSpace(packageSpec.Sha256))
		{
			return;
		}

		string actualHash = ComputeSha256(packagePath);
		if (!string.Equals(actualHash, NormalizeSha256(packageSpec.Sha256), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("ComfyUI source package SHA-256 verification failed.");
		}
	}

	internal void CopyDirectory(string sourceDir, string targetDir)
	{
		Directory.CreateDirectory(targetDir);
		foreach (string file in Directory.GetFiles(sourceDir))
		{
			CopyFileWithRuntimeProcessFallback(file, Path.Combine(targetDir, Path.GetFileName(file)));
		}

		foreach (string subDir in Directory.GetDirectories(sourceDir))
		{
			CopyDirectory(subDir, Path.Combine(targetDir, Path.GetFileName(subDir)));
		}
	}

	private void CopyFileWithRuntimeProcessFallback(string sourcePath, string destinationPath)
	{
		try
		{
			File.Copy(sourcePath, destinationPath, true);
		}
		catch (IOException) when (IsInstalledRuntimePath(destinationPath))
		{
			_log($"[Runtime] Locked runtime file detected. Terminating local runtime processes before retry: {Path.GetFileName(destinationPath)}");
			KillInstalledRuntimeProcesses();
			Thread.Sleep(250);
			File.Copy(sourcePath, destinationPath, true);
		}
	}

	private static bool IsInstalledRuntimePath(string path)
	{
		string fullPath = Path.GetFullPath(path);
		string installedPath = Path.GetFullPath(ComfyInstallService.InstalledPath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		return fullPath.StartsWith(
			installedPath + Path.DirectorySeparatorChar,
			StringComparison.OrdinalIgnoreCase);
	}

	internal void KillInstalledRuntimeProcesses()
	{
#if !WINDOWS
		_log("[Runtime] Process termination is only available on Windows. Retrying copy without cleanup.");
#else
		string installedPath = Path.GetFullPath(ComfyInstallService.InstalledPath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			.ToLowerInvariant();

		foreach (Process process in Process.GetProcesses())
		{
			try
			{
				string processName = process.ProcessName.ToLowerInvariant();
				if (processName is not ("python" or "pythonw" or "git" or "cmd" or "conhost")) continue;

				string? fileName = process.MainModule?.FileName;
				if (string.IsNullOrWhiteSpace(fileName)) continue;

				string processPath = Path.GetFullPath(fileName).ToLowerInvariant();
				if (!processPath.StartsWith(installedPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
				{
					continue;
				}

				_log($"[Runtime] Terminating locked runtime process: {process.ProcessName} (PID: {process.Id})");
				process.Kill(entireProcessTree: true);
				process.WaitForExit(2000);
			}
			catch
			{
			}
			finally
			{
				process.Dispose();
			}
		}
#endif
	}

	private static async Task ExtractZipAsync(string zipPath, string destinationPath, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(destinationPath);
		await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, destinationPath, true), cancellationToken);
	}

	private static bool IsPortableGitPackage(string packagePath)
		=> string.Equals(Path.GetFileName(packagePath), ExpectedPortableGitFile, StringComparison.OrdinalIgnoreCase);

	private static void ValidateMinGitPackage(string packagePath, RuntimePackageFileSpec packageSpec)
	{
		if (!File.Exists(packagePath))
		{
			throw new FileNotFoundException("MinGit package file was not found.", packagePath);
		}

		if (!string.Equals(Path.GetFileName(packagePath), packageSpec.File, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Refusing unexpected MinGit package file: {Path.GetFileName(packagePath)}");
		}

		string actualHash = ComputeSha256(packagePath);
		if (!string.Equals(actualHash, NormalizeSha256(packageSpec.Sha256), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("MinGit SHA-256 verification failed.");
		}
	}

	private async Task ExtractPortableGitPackageAsync(string packagePath, string destinationPath, CancellationToken cancellationToken)
	{
#if !WINDOWS
		await Task.CompletedTask;
		throw new PlatformNotSupportedException("PortableGit packages are only supported on Windows.");
#else
		ValidatePortableGitPackage(packagePath);
		await ReplaceDirectoryAsync(destinationPath, cancellationToken);
		Directory.CreateDirectory(destinationPath);

		_log($"[Runtime] Extracting PortableGit into: {destinationPath}");
		var extract = await ProcessRunner.RunAsync(
			packagePath,
			$"-y -o{QuoteInstallerArgument(destinationPath)}",
			Path.GetDirectoryName(packagePath),
			_log,
			cancellationToken,
			PortableGitExtractTimeout);

		if (extract.ExitCode != 0)
		{
			string detail = string.IsNullOrWhiteSpace(extract.Error) ? extract.Output.Trim() : extract.Error.Trim();
			throw new InvalidOperationException($"PortableGit extraction failed. ExitCode: {extract.ExitCode}. {detail}");
		}

		await VerifyInstalledGitAsync(destinationPath, cancellationToken);
		SetupSettingsService.Instance.Settings.GitPath = Path.Combine(destinationPath, "cmd", "git.exe");
		SetupSettingsService.Instance.Save();
#endif
	}

	private static void ValidatePortableGitPackage(string packagePath)
	{
		if (!File.Exists(packagePath))
		{
			throw new FileNotFoundException("PortableGit package file was not found.", packagePath);
		}

		if (!string.Equals(Path.GetFileName(packagePath), ExpectedPortableGitFile, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Refusing unexpected PortableGit package file: {Path.GetFileName(packagePath)}");
		}

		string actualHash = ComputeSha256(packagePath);
		if (!string.Equals(actualHash, ExpectedPortableGitSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("PortableGit SHA-256 verification failed.");
		}

		ValidateSignedExecutable(packagePath, ExpectedPortableGitSigner, "PortableGit package");
	}

	private async Task VerifyInstalledGitAsync(string destinationPath, CancellationToken cancellationToken)
	{
		string gitExe = Path.Combine(destinationPath, "cmd", "git.exe");
		if (!File.Exists(gitExe))
		{
			throw new FileNotFoundException("Installed Git executable was not found.", gitExe);
		}

		var result = await ProcessRunner.RunAsync(
			gitExe,
			"--version",
			destinationPath,
			_log,
			cancellationToken);

		if (result.ExitCode == 0) return;

		string detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();
		throw new InvalidOperationException($"Git executable verification failed. ExitCode: {result.ExitCode}. {detail}");
	}

	private async Task InstallPythonRuntimeArchiveAsync(
		string destinationPath,
		string packagePath,
		PythonRuntimePackageSpec packageSpec,
		CancellationToken cancellationToken)
	{
		var manifest = await LoadPythonRuntimeManifestAsync(packagePath, packageSpec, cancellationToken);
		ValidatePythonRuntimePackage(packagePath, manifest, packageSpec);

		string candidateRoot = Path.Combine(ComfyInstallService.InstalledPath, $"Python.candidate-{Guid.NewGuid():N}");
		string candidatePythonPath = Path.Combine(candidateRoot, manifest.LayoutRoot);
		string backupPath = Path.Combine(ComfyInstallService.InstalledPath, $"Python.backup-{Guid.NewGuid():N}");
		bool backupCreated = false;
		bool candidatePromoted = false;

		try
		{
			Directory.CreateDirectory(candidateRoot);
			_log($"[Runtime] Extracting verified Python {manifest.Version} runtime...");
			await Task.Run(() => ZipFile.ExtractToDirectory(packagePath, candidateRoot, true), cancellationToken);

			await VerifyPythonRuntimeAsync(candidatePythonPath, verifyVenvCreation: true, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			if (Directory.Exists(destinationPath))
			{
				MoveRuntimeDirectory(destinationPath, backupPath);
				backupCreated = true;
			}

			MoveRuntimeDirectory(candidatePythonPath, destinationPath);
			candidatePromoted = true;
			await VerifyPythonRuntimeAsync(destinationPath, verifyVenvCreation: false, cancellationToken);

			SetupSettingsService.Instance.Settings.PythonPath = ComfyInstallService.PythonExe;
			SetupSettingsService.Instance.Save();

			if (backupCreated)
			{
				try
				{
					await DeleteDirectoryIfExistsAsync(backupPath);
					backupCreated = false;
				}
				catch (Exception ex)
				{
					_log($"[Runtime] Python runtime installed, but the previous runtime backup could not be removed: {ex.Message}");
				}
			}
		}
		catch
		{
			if (candidatePromoted && Directory.Exists(destinationPath))
			{
				await ReplaceDirectoryAsync(destinationPath, CancellationToken.None);
			}

			if (backupCreated && Directory.Exists(backupPath) && !Directory.Exists(destinationPath))
			{
				MoveRuntimeDirectory(backupPath, destinationPath);
				backupCreated = false;
			}

			throw;
		}
		finally
		{
			try
			{
				await DeleteDirectoryIfExistsAsync(candidateRoot);
			}
			catch (Exception ex)
			{
				_log($"[Runtime] Python candidate cleanup failed: {ex.Message}");
			}
		}
	}

	private static async Task<PythonRuntimeManifest> LoadPythonRuntimeManifestAsync(
		string packagePath,
		PythonRuntimePackageSpec packageSpec,
		CancellationToken cancellationToken)
	{
		string manifestPath = Path.Combine(Path.GetDirectoryName(packagePath) ?? string.Empty, packageSpec.Manifest);
		if (!File.Exists(manifestPath))
		{
			throw new FileNotFoundException("Python runtime manifest was not found.", manifestPath);
		}

		await using var stream = File.OpenRead(manifestPath);
		var manifest = await JsonSerializer.DeserializeAsync<PythonRuntimeManifest>(stream, cancellationToken: cancellationToken);
		return manifest ?? throw new InvalidOperationException("Python runtime manifest is empty or invalid.");
	}

	private static void ValidatePythonRuntimePackage(
		string packagePath,
		PythonRuntimeManifest manifest,
		PythonRuntimePackageSpec packageSpec)
	{
		if (!string.Equals(Path.GetFileName(packagePath), packageSpec.File, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(manifest.Archive, packageSpec.File, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Unexpected Python runtime archive: {Path.GetFileName(packagePath)}");
		}

		string expectedSha256 = NormalizeSha256(packageSpec.Sha256);
		if (!string.Equals(NormalizeSha256(manifest.Sha256), expectedSha256, StringComparison.Ordinal) ||
			!string.Equals(ComputeSha256(packagePath), expectedSha256, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Python runtime archive SHA-256 verification failed.");
		}

		if (!string.Equals(manifest.Version, packageSpec.Version, StringComparison.Ordinal) ||
			!string.Equals(manifest.Platform, packageSpec.Platform, StringComparison.Ordinal) ||
			!string.Equals(manifest.LayoutRoot, packageSpec.LayoutRoot, StringComparison.Ordinal) ||
			!string.Equals(manifest.Source, packageSpec.Source, StringComparison.Ordinal) ||
			!string.Equals(manifest.SourceInstaller, packageSpec.SourceInstaller, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(NormalizeSha256(manifest.SourceInstallerSha256), NormalizeSha256(packageSpec.SourceInstallerSha256), StringComparison.Ordinal) ||
			!string.Equals(manifest.Publisher, packageSpec.Publisher, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Python runtime manifest metadata does not match the trusted package definition.");
		}
	}

	private static string QuoteInstallerArgument(string value)
		=> $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

	private async Task VerifyPythonRuntimeAsync(string pythonRoot, bool verifyVenvCreation, CancellationToken cancellationToken)
	{
		string pythonExe = Path.Combine(pythonRoot, "python.exe");
		if (!File.Exists(pythonExe))
		{
			throw new FileNotFoundException("Python runtime executable was not found.", pythonExe);
		}

		await VerifyPythonCommandAsync(pythonExe, pythonRoot, "--version", "Python executable verification failed.", cancellationToken);
		await VerifyPythonCommandAsync(
			pythonExe,
			pythonRoot,
			"-c \"import venv, ensurepip, ssl, sqlite3, ctypes; print('runtime-ready')\"",
			"Python standard runtime verification failed.",
			cancellationToken);
		await VerifyPythonCommandAsync(pythonExe, pythonRoot, "-m pip --version", "Python pip verification failed.", cancellationToken);

		if (!verifyVenvCreation)
		{
			return;
		}

		string venvPath = Path.Combine(Path.GetDirectoryName(pythonRoot) ?? pythonRoot, $"venv-probe-{Guid.NewGuid():N}");
		try
		{
			await VerifyPythonCommandAsync(
				pythonExe,
				pythonRoot,
				$"-m venv {QuoteInstallerArgument(venvPath)}",
				"Python venv creation verification failed.",
				cancellationToken);
			await VerifyPythonCommandAsync(
				Path.Combine(venvPath, "Scripts", "python.exe"),
				venvPath,
				"-m pip --version",
				"Python venv pip verification failed.",
				cancellationToken);
		}
		finally
		{
			await DeleteDirectoryIfExistsAsync(venvPath);
		}
	}

	private async Task VerifyPythonCommandAsync(
		string pythonExe,
		string workingDirectory,
		string arguments,
		string failureMessage,
		CancellationToken cancellationToken)
	{
		IReadOnlyDictionary<string, string>? environmentVariables = UsesPipCache(arguments)
			? PipCacheService.CreateEnvironment()
			: null;
		if (environmentVariables is not null)
		{
			_log($"[Runtime] pip cache: {environmentVariables[PipCacheService.EnvironmentVariableName]}");
		}
		else if (UsesPipCache(arguments))
		{
			_log("[Runtime] pip cache: pip default");
		}

		var result = await ProcessRunner.RunAsync(
			pythonExe,
			arguments,
			workingDirectory,
			_log,
			cancellationToken,
			environmentVariables: environmentVariables);

		if (result.ExitCode == 0) return;

		string detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();
		throw new InvalidOperationException($"{failureMessage} ExitCode: {result.ExitCode}. {detail}");
	}

	private static bool UsesPipCache(string arguments)
		=> arguments.Contains("-m pip", StringComparison.Ordinal)
			|| arguments.Contains("-m ensurepip", StringComparison.Ordinal)
			|| arguments.Contains("-m venv", StringComparison.Ordinal);

	private void MoveRuntimeDirectory(string sourcePath, string destinationPath)
	{
		try
		{
			Directory.Move(sourcePath, destinationPath);
		}
		catch (IOException) when (IsInstalledRuntimePath(sourcePath) || IsInstalledRuntimePath(destinationPath))
		{
			_log($"[Runtime] Locked Python runtime detected. Terminating local runtime processes before retry: {sourcePath}");
			KillInstalledRuntimeProcesses();
			Thread.Sleep(250);
			Directory.Move(sourcePath, destinationPath);
		}
	}

	private static string ComputeSha256(string path)
	{
		using var stream = File.OpenRead(path);
		byte[] hash = SHA256.HashData(stream);
		return Convert.ToHexString(hash);
	}

	private static string NormalizeSha256(string value)
		=> value.Replace(" ", string.Empty, StringComparison.Ordinal)
			.Trim()
			.ToUpper(CultureInfo.InvariantCulture);

	private static void ValidateSignedExecutable(string installerPath, string expectedSubject, string packageLabel)
	{
#if !WINDOWS
		throw new PlatformNotSupportedException("Authenticode signature verification is only supported on Windows.");
#else
		int trustResult = WinVerifyTrustFile(installerPath);
		if (trustResult != 0)
		{
			throw new InvalidOperationException($"{packageLabel} Authenticode verification failed. WinTrust result: 0x{trustResult:X8}");
		}

#pragma warning disable SYSLIB0057 // Need the embedded Authenticode signer certificate from a PE file.
		using var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
			System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(installerPath));
#pragma warning restore SYSLIB0057
		if (!certificate.Subject.Contains(expectedSubject, StringComparison.Ordinal))
		{
			throw new InvalidOperationException($"{packageLabel} signer mismatch: {certificate.Subject}");
		}
#endif
	}

#if WINDOWS
	private static int WinVerifyTrustFile(string filePath)
	{
		Guid action = WintrustActionGenericVerifyV2;
		using var fileInfo = new WinTrustFileInfo(filePath);
		using var trustData = new WinTrustData(fileInfo);
		return WinVerifyTrust(IntPtr.Zero, ref action, trustData.Handle);
	}

	private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

	[DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

	private sealed class WinTrustFileInfo : IDisposable
	{
		private readonly IntPtr _filePathPtr;
		internal IntPtr Handle { get; }

		internal WinTrustFileInfo(string filePath)
		{
			_filePathPtr = Marshal.StringToCoTaskMemUni(filePath);
			var data = new WinTrustFileInfoData
			{
				StructSize = (uint)Marshal.SizeOf<WinTrustFileInfoData>(),
				FilePath = _filePathPtr,
				FileHandle = IntPtr.Zero,
				KnownSubject = IntPtr.Zero
			};
			Handle = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfoData>());
			Marshal.StructureToPtr(data, Handle, false);
		}

		public void Dispose()
		{
			if (Handle != IntPtr.Zero)
			{
				Marshal.DestroyStructure<WinTrustFileInfoData>(Handle);
				Marshal.FreeCoTaskMem(Handle);
			}

			if (_filePathPtr != IntPtr.Zero)
			{
				Marshal.FreeCoTaskMem(_filePathPtr);
			}
		}
	}

	private sealed class WinTrustData : IDisposable
	{
		internal IntPtr Handle { get; }

		internal WinTrustData(WinTrustFileInfo fileInfo)
		{
			var data = new WinTrustDataData
			{
				StructSize = (uint)Marshal.SizeOf<WinTrustDataData>(),
				PolicyCallbackData = IntPtr.Zero,
				SipClientData = IntPtr.Zero,
				UiChoice = 2,
				RevocationChecks = 0,
				UnionChoice = 1,
				File = fileInfo.Handle,
				StateAction = 0,
				StateData = IntPtr.Zero,
				UrlReference = IntPtr.Zero,
				ProvFlags = 0x00000010,
				UiContext = 0,
				SignatureSettings = IntPtr.Zero
			};
			Handle = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustDataData>());
			Marshal.StructureToPtr(data, Handle, false);
		}

		public void Dispose()
		{
			if (Handle == IntPtr.Zero) return;

			Marshal.DestroyStructure<WinTrustDataData>(Handle);
			Marshal.FreeCoTaskMem(Handle);
		}
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WinTrustFileInfoData
	{
		public uint StructSize;
		public IntPtr FilePath;
		public IntPtr FileHandle;
		public IntPtr KnownSubject;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WinTrustDataData
	{
		public uint StructSize;
		public IntPtr PolicyCallbackData;
		public IntPtr SipClientData;
		public uint UiChoice;
		public uint RevocationChecks;
		public uint UnionChoice;
		public IntPtr File;
		public uint StateAction;
		public IntPtr StateData;
		public IntPtr UrlReference;
		public uint ProvFlags;
		public uint UiContext;
		public IntPtr SignatureSettings;
	}
#endif

	private static async Task DeleteDirectoryIfExistsAsync(string path)
	{
		await Task.Run(() =>
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		});
	}

	private async Task ReplaceDirectoryAsync(string path, CancellationToken cancellationToken)
	{
		if (!Directory.Exists(path)) return;

		await Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				ClearReadOnlyAttributes(path);
				Directory.Delete(path, recursive: true);
			}
			catch (Exception) when (IsInstalledRuntimePath(path))
			{
				_log($"[Runtime] Locked runtime directory detected. Terminating local runtime processes before retry: {path}");
				KillInstalledRuntimeProcesses();
				Thread.Sleep(250);
				ClearReadOnlyAttributes(path);
				Directory.Delete(path, recursive: true);
			}
		}, cancellationToken);
	}

	private static void ClearReadOnlyAttributes(string path)
	{
		if (!Directory.Exists(path)) return;

		foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		foreach (string directory in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(directory, FileAttributes.Normal);
		}

		File.SetAttributes(path, FileAttributes.Normal);
	}
}

internal sealed record PythonRuntimeManifest(
	[property: JsonPropertyName("archive")] string Archive,
	[property: JsonPropertyName("sha256")] string Sha256,
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("platform")] string Platform,
	[property: JsonPropertyName("layout_root")] string LayoutRoot,
	[property: JsonPropertyName("created_utc")] string CreatedUtc,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("source_installer")] string SourceInstaller,
	[property: JsonPropertyName("source_installer_sha256")] string SourceInstallerSha256,
	[property: JsonPropertyName("publisher")] string Publisher);
