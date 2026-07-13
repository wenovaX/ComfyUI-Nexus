param(
	[Parameter(Mandatory = $true)]
	[string]$PublishDirectory,

	[Parameter(Mandatory = $true)]
	[string]$ProjectRoot,

	[Parameter(Mandatory = $true)]
	[string]$ReleaseDirectory,

	[string]$ArchivePath,

	[ValidateSet("folder", "single")]
	[string]$PackageMode = "folder",

	[string]$CertificateName,

	[switch]$CreateArchive
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\dev-code-signing.ps1"

$forbiddenSegments = @(
	".git",
	".vs",
	"bin",
	"obj",
	"artifacts",
	"build"
)

$forbiddenPrefixes = @(
	"LocalRuntime\Installed",
	"LocalRuntime\Cache",
	"LocalRuntime\Logs",
	"LocalRuntime\State",
	"LocalRuntime\Work"
)

$forbiddenFiles = @(
	"nexus_settings.json"
)

$forbiddenExtensions = @(
	".pdb",
	".binlog",
	".tmp"
)

$supportedCultureDirectories = @(
	"en-us",
	"ko-KR",
	"zh-CN",
	"zh-TW"
)

function Get-RelativePath {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Root,

		[Parameter(Mandatory = $true)]
		[string]$Path
	)

	$rootUri = [Uri]((Resolve-Path -LiteralPath $Root).ProviderPath.TrimEnd('\') + '\')
	$pathUri = [Uri]((Resolve-Path -LiteralPath $Path).ProviderPath)
	return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Assert-ReleaseRelativePath {
	param(
		[Parameter(Mandatory = $true)]
		[string]$RelativePath
	)

	$normalized = $RelativePath.Replace('/', '\').TrimStart('\')
	$segments = $normalized -split '\\'
	foreach ($segment in $segments) {
		if ($forbiddenSegments -contains $segment.ToLowerInvariant()) {
			throw "Forbidden release path segment '$segment': $RelativePath"
		}
	}

	foreach ($prefix in $forbiddenPrefixes) {
		if ($normalized.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
			throw "Forbidden release path prefix '$prefix': $RelativePath"
		}
	}

	$fileName = [System.IO.Path]::GetFileName($normalized)
	if ($forbiddenFiles -contains $fileName.ToLowerInvariant()) {
		throw "Forbidden release file '$fileName': $RelativePath"
	}

	$extension = [System.IO.Path]::GetExtension($normalized).ToLowerInvariant()
	if ($forbiddenExtensions -contains $extension) {
		throw "Forbidden release file extension '$extension': $RelativePath"
	}
}

function Copy-ReleaseFile {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Source,

		[Parameter(Mandatory = $true)]
		[string]$RelativePath
	)

	if (!(Test-Path -LiteralPath $Source -PathType Leaf)) {
		throw "Required release source file is missing: $Source"
	}

	Assert-ReleaseRelativePath -RelativePath $RelativePath
	$destination = Join-Path $ReleaseDirectory $RelativePath
	$destinationDirectory = Split-Path -Parent $destination
	if (!(Test-Path -LiteralPath $destinationDirectory)) {
		New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
	}

	Copy-Item -LiteralPath $Source -Destination $destination -Force
}

function Copy-ReleaseTree {
	param(
		[Parameter(Mandatory = $true)]
		[string]$SourceRoot,

		[Parameter(Mandatory = $true)]
		[string]$DestinationRelativeRoot
	)

	if (!(Test-Path -LiteralPath $SourceRoot -PathType Container)) {
		throw "Required release source directory is missing: $SourceRoot"
	}

	Get-ChildItem -LiteralPath $SourceRoot -Recurse -File | Where-Object {
		$_.Name -ne ".gitkeep"
	} | ForEach-Object {
		$relative = Get-RelativePath -Root $SourceRoot -Path $_.FullName
		Copy-ReleaseFile -Source $_.FullName -RelativePath (Join-Path $DestinationRelativeRoot $relative)
	}
}

function Test-IsCultureDirectory {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Name
	)

	return $Name -match '^[a-z]{2,3}(-[A-Za-z0-9]{2,8}){1,2}$'
}

function Test-AppRelativePathAllowed {
	param(
		[Parameter(Mandatory = $true)]
		[string]$RelativePath
	)

	$topSegment = ($RelativePath.Replace('/', '\') -split '\\')[0]
	if (!(Test-IsCultureDirectory -Name $topSegment)) {
		return $true
	}

	return $supportedCultureDirectories -contains $topSegment
}

function Test-RequiredReleaseFile {
	param(
		[Parameter(Mandatory = $true)]
		[string]$RelativePath
	)

	$path = Join-Path $ReleaseDirectory $RelativePath
	if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
		throw "Required release file was not packed: $RelativePath"
	}
}

function Invoke-ReleaseSigning {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Root,

		[Parameter(Mandatory = $true)]
		[string]$Name
	)

	$codeSignableExtensions = @(
		".appx",
		".appxbundle",
		".cab",
		".cat",
		".cpl",
		".dll",
		".exe",
		".msi",
		".msix",
		".msixbundle",
		".msp",
		".ocx",
		".scr",
		".sys"
	)

	$certificate = Get-NexusCodeSigningCertificate -Name $Name
	Write-Host "[Nexus] Signing release binaries with: $($certificate.Subject) ($($certificate.Thumbprint))"
	$targets = @(Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object {
		$codeSignableExtensions -contains $_.Extension.ToLowerInvariant()
	})
	if ($targets.Count -eq 0) {
		throw "No code-signable release files were found in: $Root"
	}

	$skippedCount = 0
	$signedCount = 0
	$signedFiles = [System.Collections.Generic.List[string]]::new()
	Write-Host "[Nexus] Inspecting $($targets.Count) code-signable release file(s)..."
	foreach ($target in $targets) {
		$existingSignature = Get-AuthenticodeSignature -FilePath $target.FullName
		if ($existingSignature.SignatureType.ToString() -ne "None") {
			$skippedCount++
			continue
		}

		$result = Set-AuthenticodeSignature -FilePath $target.FullName -Certificate $certificate
		if ($result.Status -ne "Valid") {
			throw "Certificate signing failed for '$($target.FullName)': $($result.Status) $($result.StatusMessage)"
		}

		$signedCount++
		$signedFiles.Add((Get-RelativePath -Root $Root -Path $target.FullName))
	}

	if ($signedFiles.Count -gt 0) {
		Write-Host "[Nexus] Newly signed release files:"
		foreach ($signedFile in $signedFiles) {
			Write-Host "[Nexus]   $signedFile"
		}
	}

	Write-Host "[Nexus] Signing complete. signed=$signedCount, existingSignature=$skippedCount"
}

function Assert-ReleaseApplicationSignature {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Root,

		[Parameter(Mandatory = $true)]
		[string]$CertificateThumbprint
	)

	$applicationPaths = @(
		"ComfyUI-Nexus.exe",
		"App\ComfyUI-Nexus.exe"
	)

	foreach ($relativePath in $applicationPaths) {
		$applicationPath = Join-Path $Root $relativePath
		if (!(Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
			continue
		}

		$signature = Get-AuthenticodeSignature -FilePath $applicationPath
		if ($signature.Status -ne "Valid" -or
			$null -eq $signature.SignerCertificate -or
			$signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
			throw "Release application signature verification failed for '$relativePath': $($signature.Status)"
		}

		Write-Host "[Nexus] Verified application signature: $relativePath"
	}
}

function Assert-ReleaseTreeClean {
	Get-ChildItem -LiteralPath $ReleaseDirectory -Recurse -Force | ForEach-Object {
		$relative = Get-RelativePath -Root $ReleaseDirectory -Path $_.FullName
		Assert-ReleaseRelativePath -RelativePath $relative
	}
}

function Assert-ZipClean {
	param(
		[Parameter(Mandatory = $true)]
		[string]$ZipPath
	)

	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
	try {
		foreach ($entry in $archive.Entries) {
			if ([string]::IsNullOrWhiteSpace($entry.FullName)) {
				continue
			}

			Assert-ReleaseRelativePath -RelativePath $entry.FullName
		}
	}
	finally {
		$archive.Dispose()
	}
}

function Get-LauncherCompiler {
	$candidates = @(
		(Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
		(Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
	)

	foreach ($candidate in $candidates) {
		if (Test-Path -LiteralPath $candidate -PathType Leaf) {
			return $candidate
		}
	}

	throw "C# launcher compiler was not found."
}

function New-PortableLauncher {
	$launcherPath = Join-Path $ReleaseDirectory "ComfyUI-Nexus.exe"
	$tempDirectory = Join-Path (Split-Path -Parent $ReleaseDirectory) ".launcher"
	if (Test-Path -LiteralPath $tempDirectory) {
		Remove-Item -LiteralPath $tempDirectory -Recurse -Force
	}

	New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
	$sourcePath = Join-Path $tempDirectory "ComfyUINexusLauncher.cs"
	$source = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(
			Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar);
		string appDirectory = Path.Combine(root, "App");
		string appPath = Path.Combine(appDirectory, "ComfyUI-Nexus.exe");

		if (!File.Exists(appPath))
		{
			MessageBox.Show(
				"ComfyUI Nexus application files were not found. Reinstall from the release ZIP and keep the App folder beside ComfyUI-Nexus.exe.",
				"ComfyUI Nexus",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);
			return 1;
		}

		try
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = appPath,
				WorkingDirectory = appDirectory,
				UseShellExecute = false,
				Arguments = BuildArguments(args)
			};
			startInfo.EnvironmentVariables["COMFYUI_NEXUS_PORTABLE_ROOT"] = root;
			Process.Start(startInfo);
			return 0;
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				"ComfyUI Nexus could not be started.\n\n" + ex.Message,
				"ComfyUI Nexus",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);
			return 1;
		}
	}

	private static string BuildArguments(string[] args)
	{
		if (args == null || args.Length == 0)
		{
			return string.Empty;
		}

		var builder = new StringBuilder();
		foreach (string arg in args)
		{
			if (builder.Length > 0)
			{
				builder.Append(' ');
			}

			builder.Append('"');
			builder.Append((arg ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""));
			builder.Append('"');
		}

		return builder.ToString();
	}
}
'@

	Set-Content -LiteralPath $sourcePath -Value $source -Encoding UTF8
	$compiler = Get-LauncherCompiler
	$iconPath = Join-Path $PublishDirectory "appicon.ico"
	$iconArgument = if (Test-Path -LiteralPath $iconPath -PathType Leaf) { "/win32icon:$iconPath" } else { $null }
	$compilerArguments = @(
		"/nologo",
		"/target:winexe",
		"/reference:System.Windows.Forms.dll",
		"/out:$launcherPath"
	)
	if ($iconArgument) {
		$compilerArguments += $iconArgument
	}
	$compilerArguments += $sourcePath
	& $compiler @compilerArguments
	if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
		throw "Portable launcher build failed."
	}

	Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}

function Assert-PackageRelativePath {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Path,

		[Parameter(Mandatory = $true)]
		[string]$Label
	)

	if ([System.IO.Path]::IsPathRooted($Path) -or $Path.Contains("..")) {
		throw "Unsafe package spec path for ${Label}: $Path"
	}
}

function Get-PackageSpec {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Root
	)

	$specPath = Join-Path $Root "LocalRuntime\Packages\runtime-package-spec.json"
	if (!(Test-Path -LiteralPath $specPath -PathType Leaf)) {
		throw "Package spec is missing: $specPath"
	}

	return Get-Content -LiteralPath $specPath -Raw -Encoding UTF8 | ConvertFrom-Json
}

$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).ProviderPath
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).ProviderPath
$packageSpec = Get-PackageSpec -Root $ProjectRoot

Assert-PackageRelativePath -Path $packageSpec.git.folder -Label "git.folder"
Assert-PackageRelativePath -Path $packageSpec.git.file -Label "git.file"
Assert-PackageRelativePath -Path $packageSpec.python.folder -Label "python.folder"
Assert-PackageRelativePath -Path $packageSpec.python.file -Label "python.file"
Assert-PackageRelativePath -Path $packageSpec.python.manifest -Label "python.manifest"
if ($null -ne $packageSpec.comfy) {
	Assert-PackageRelativePath -Path $packageSpec.comfy.folder -Label "comfy.folder"
	Assert-PackageRelativePath -Path $packageSpec.comfy.file -Label "comfy.file"
}
Assert-PackageRelativePath -Path $packageSpec.bridge.folder -Label "bridge.folder"
Assert-PackageRelativePath -Path $packageSpec.bridge.required_file -Label "bridge.required_file"

$requiredPackageFiles = @(
	@{
		Source = Join-Path $ProjectRoot "LocalRuntime\Packages\runtime-package-spec.json"
		RelativePath = "LocalRuntime\Packages\runtime-package-spec.json"
	},
	@{
		Source = Join-Path $ProjectRoot (Join-Path "LocalRuntime\Packages" (Join-Path $packageSpec.git.folder $packageSpec.git.file))
		RelativePath = Join-Path "LocalRuntime\Packages" (Join-Path $packageSpec.git.folder $packageSpec.git.file)
	},
	@{
		Source = Join-Path $ProjectRoot (Join-Path "LocalRuntime\Packages" (Join-Path $packageSpec.python.folder $packageSpec.python.file))
		RelativePath = Join-Path "LocalRuntime\Packages" (Join-Path $packageSpec.python.folder $packageSpec.python.file)
	},
	@{
		Source = Join-Path $ProjectRoot (Join-Path "LocalRuntime\Packages" (Join-Path $packageSpec.python.folder $packageSpec.python.manifest))
		RelativePath = Join-Path "LocalRuntime\Packages" (Join-Path $packageSpec.python.folder $packageSpec.python.manifest)
	}
)

if ($null -ne $packageSpec.comfy) {
	$requiredPackageFiles += @{
		Source = Join-Path $ProjectRoot (Join-Path "LocalRuntime\Packages" (Join-Path $packageSpec.comfy.folder $packageSpec.comfy.file))
		RelativePath = Join-Path "LocalRuntime\Packages" (Join-Path $packageSpec.comfy.folder $packageSpec.comfy.file)
	}
}

$bridgeSourceRoot = Join-Path $ProjectRoot (Join-Path "LocalRuntime\Packages" $packageSpec.bridge.folder)
$bridgeDestinationRelativeRoot = Join-Path "LocalRuntime\Packages" $packageSpec.bridge.folder
$bridgeRequiredRelativePath = Join-Path $bridgeDestinationRelativeRoot ($packageSpec.bridge.required_file.Replace('/', '\'))

if (Test-Path -LiteralPath $ReleaseDirectory) {
	Remove-Item -LiteralPath $ReleaseDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $ReleaseDirectory -Force | Out-Null

Write-Host "[Nexus] Copying published application files..."
Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | ForEach-Object {
	$relative = Get-RelativePath -Root $PublishDirectory -Path $_.FullName
	if ($forbiddenFiles -contains ([System.IO.Path]::GetFileName($relative).ToLowerInvariant())) {
		return
	}

	if ($PackageMode -eq "folder" -and !(Test-AppRelativePathAllowed -RelativePath $relative)) {
		return
	}

	$releaseRelativePath = if ($PackageMode -eq "folder") {
		Join-Path "App" $relative
	}
	else {
		$relative
	}
	Copy-ReleaseFile -Source $_.FullName -RelativePath $releaseRelativePath
}

if ($PackageMode -eq "folder") {
	Write-Host "[Nexus] Creating portable root launcher..."
	New-PortableLauncher
}

Write-Host "[Nexus] Copying portable runtime packages..."
foreach ($file in $requiredPackageFiles) {
	Copy-ReleaseFile -Source $file.Source -RelativePath $file.RelativePath
}

Copy-ReleaseTree `
	-SourceRoot $bridgeSourceRoot `
	-DestinationRelativeRoot $bridgeDestinationRelativeRoot

Test-RequiredReleaseFile -RelativePath "ComfyUI-Nexus.exe"
if ($PackageMode -eq "folder") {
	Test-RequiredReleaseFile -RelativePath "App\ComfyUI-Nexus.exe"
}
foreach ($file in $requiredPackageFiles) {
	Test-RequiredReleaseFile -RelativePath $file.RelativePath
}
Test-RequiredReleaseFile -RelativePath $bridgeRequiredRelativePath

Assert-ReleaseTreeClean

if (![string]::IsNullOrWhiteSpace($CertificateName)) {
	Invoke-ReleaseSigning -Root $ReleaseDirectory -Name $CertificateName
	$certificate = Get-NexusCodeSigningCertificate -Name $CertificateName
	Assert-ReleaseApplicationSignature -Root $ReleaseDirectory -CertificateThumbprint $certificate.Thumbprint
}

$fileCount = (Get-ChildItem -LiteralPath $ReleaseDirectory -Recurse -File | Measure-Object).Count
Write-Host "[Nexus] Packed $fileCount file(s)."

if ($CreateArchive) {
	if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
		throw "ArchivePath is required when CreateArchive is specified."
	}

	Write-Host "[Nexus] Creating portable release archive..."
	if (Test-Path -LiteralPath $ArchivePath) {
		Remove-Item -LiteralPath $ArchivePath -Force
	}

	$archiveDirectory = Split-Path -Parent $ArchivePath
	if (!(Test-Path -LiteralPath $archiveDirectory)) {
		New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null
	}

	$temporaryArchive = Join-Path (Split-Path -Parent $ReleaseDirectory) ([System.IO.Path]::GetFileNameWithoutExtension($ArchivePath) + ".tmp.zip")
	if (Test-Path -LiteralPath $temporaryArchive) {
		Remove-Item -LiteralPath $temporaryArchive -Force
	}

	Compress-Archive -Path (Join-Path $ReleaseDirectory "*") -DestinationPath $temporaryArchive -CompressionLevel Optimal
	Move-Item -LiteralPath $temporaryArchive -Destination $ArchivePath -Force
	Assert-ZipClean -ZipPath $ArchivePath

	$archiveSize = (Get-Item -LiteralPath $ArchivePath).Length
	Write-Host "[Nexus] Archive size: $([Math]::Round($archiveSize / 1MB, 2)) MiB"
}
else {
	Write-Host "[Nexus] Archive skipped. Use archive or zip to create the portable ZIP."
}
