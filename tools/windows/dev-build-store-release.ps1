[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[string]$ProjectRoot,

	[Parameter(Mandatory = $true)]
	[string]$Configuration,

	[Parameter(Mandatory = $true)]
	[string]$Framework,

	[Parameter(Mandatory = $true)]
	[string]$Runtime,

	[switch]$CleanBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ProjectVersion {
	param([Parameter(Mandatory = $true)][string]$Path)

	[xml]$props = Get-Content -LiteralPath $Path -Raw -Encoding utf8
	$version = $props.Project.PropertyGroup.NexusVersion | Select-Object -First 1
	if ([string]::IsNullOrWhiteSpace($version)) {
		throw '[Nexus] NexusVersion was not found in Directory.Build.props.'
	}

	return $version
}

function Assert-StoreIdentity {
	param([Parameter(Mandatory = $true)][string]$Path)

	if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
		throw '[Nexus] Store.Build.props was not found. Copy Store.Build.props.example and fill in the Partner Center identity locally.'
	}

	[xml]$props = Get-Content -LiteralPath $Path -Raw -Encoding utf8
	$values = @{}
	foreach ($propertyGroup in @($props.Project.PropertyGroup)) {
		foreach ($name in 'NexusStoreIdentityName', 'NexusStorePublisher', 'NexusStorePublisherDisplayName') {
			if (-not [string]::IsNullOrWhiteSpace($propertyGroup.$name)) {
				$values[$name] = [string]$propertyGroup.$name
			}
		}
	}

	$missing = @(@('NexusStoreIdentityName', 'NexusStorePublisher', 'NexusStorePublisherDisplayName') |
		Where-Object { -not $values.ContainsKey($_) })
	if ($missing.Count -gt 0) {
		throw "[Nexus] Store.Build.props is missing: $($missing -join ', ')."
	}
}

function Assert-SymbolTool {
	$vsWherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
	if (-not (Test-Path -LiteralPath $vsWherePath -PathType Leaf)) {
		throw '[Nexus] vswhere.exe was not found. Install Visual Studio Build Tools with the MSVC x64/x86 build tools component.'
	}

	$symbolTool = @(& $vsWherePath -latest -products * -find 'VC\Tools\MSVC\**\bin\Hostx64\x64\mspdbcmf.exe') |
		Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
		Select-Object -First 1
	if ([string]::IsNullOrWhiteSpace($symbolTool)) {
		throw '[Nexus] mspdbcmf.exe was not found. Install the MSVC x64/x86 build tools component.'
	}
}

function Get-PackageManifestIdentity {
	param([Parameter(Mandatory = $true)][string]$MsixPath)

	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$archive = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
	try {
		$entry = $archive.GetEntry('AppxManifest.xml')
		if ($null -eq $entry) {
			throw '[Nexus] AppxManifest.xml was not found in the generated MSIX.'
		}

		$reader = [System.IO.StreamReader]::new($entry.Open())
		try {
			[xml]$manifest = $reader.ReadToEnd()
		}
		finally {
			$reader.Dispose()
		}

		return $manifest.Package.Identity
	}
	finally {
		$archive.Dispose()
	}
}

$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$projectPath = Join-Path $projectRootPath 'ComfyUI-Nexus.csproj'
$directoryPropsPath = Join-Path $projectRootPath 'Directory.Build.props'
$storePropsPath = Join-Path $projectRootPath 'Store.Build.props'

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
	throw "[Nexus] Project file was not found: $projectPath"
}

Assert-StoreIdentity -Path $storePropsPath
Assert-SymbolTool

$version = Get-ProjectVersion -Path $directoryPropsPath
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$releaseRoot = Join-Path $projectRootPath "build\Store_${Configuration}_$timestamp"
$packageDirectory = Join-Path $releaseRoot 'packages'
$uploadDirectory = Join-Path $releaseRoot 'upload'
$uploadPath = Join-Path $releaseRoot "ComfyUI-Nexus-v$version-$Runtime-store.msixupload"
$temporaryZipPath = "$uploadPath.tmp.zip"

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

Write-Host '[Nexus] Building Microsoft Store upload package'
Write-Host "[Nexus] Configuration : $Configuration"
Write-Host "[Nexus] Runtime       : $Runtime"
Write-Host "[Nexus] Version       : $version"
Write-Host "[Nexus] Output        : $releaseRoot"

if ($CleanBuild) {
	Write-Host '[Nexus] Cleaning previous Store build outputs...'
	& dotnet clean $projectPath -f $Framework -c $Configuration -r $Runtime -p:NexusDistributionProfile=Store -v quiet
	if ($LASTEXITCODE -ne 0) {
		throw '[Nexus] Store clean failed.'
	}
}

Write-Host '[Nexus] Restoring Store package dependencies...'
& dotnet restore $projectPath -p:TargetFramework=$Framework -p:RuntimeIdentifier=$Runtime -p:NexusDistributionProfile=Store
if ($LASTEXITCODE -ne 0) {
	throw '[Nexus] Store restore failed.'
}

Write-Host '[Nexus] Publishing MSIX and symbol package...'
& dotnet publish $projectPath `
	-f $Framework `
	-c $Configuration `
	-r $Runtime `
	-p:NexusDistributionProfile=Store `
	-p:AppxPackageSigningEnabled=false `
	-p:GenerateAppxPackageOnBuild=true `
	-p:AppxBundle=Never `
	-p:AppxPackageDir="$packageDirectory\" `
	--no-restore
if ($LASTEXITCODE -ne 0) {
	throw '[Nexus] Store publish failed.'
}

$msix = Get-ChildItem -LiteralPath $packageDirectory -Recurse -File -Filter '*.msix' | Select-Object -First 1
$symbols = Get-ChildItem -LiteralPath $packageDirectory -Recurse -File -Filter '*.appxsym' | Select-Object -First 1
if ($null -eq $msix -or $null -eq $symbols) {
	throw '[Nexus] Store publish did not produce both an MSIX package and an .appxsym symbols package.'
}

$identity = Get-PackageManifestIdentity -MsixPath $msix.FullName
if ([string]::IsNullOrWhiteSpace($identity.Name) -or [string]::IsNullOrWhiteSpace($identity.Publisher)) {
	throw '[Nexus] Generated MSIX is missing its Store package identity.'
}
if ($identity.Version -notmatch '^\d+\.\d+\.\d+\.0$') {
	throw "[Nexus] Store MSIX version must reserve its fourth part as 0. Generated version: $($identity.Version)."
}

New-Item -ItemType Directory -Path $uploadDirectory -Force | Out-Null
Copy-Item -LiteralPath $msix.FullName -Destination (Join-Path $uploadDirectory $msix.Name)
Copy-Item -LiteralPath $symbols.FullName -Destination (Join-Path $uploadDirectory $symbols.Name)
[System.IO.Compression.ZipFile]::CreateFromDirectory($uploadDirectory, $temporaryZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Move-Item -LiteralPath $temporaryZipPath -Destination $uploadPath

Write-Host ''
Write-Host '[Nexus] Microsoft Store upload package complete.'
Write-Host '[Nexus] Upload this file to Partner Center:'
Write-Host "[Nexus]   $uploadPath"
