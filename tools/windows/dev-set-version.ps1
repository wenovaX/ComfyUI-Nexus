[CmdletBinding()]
param(
	[switch]$NoPause,

	[string]$ProjectRoot = (Join-Path $PSScriptRoot '..\..')
)

$ErrorActionPreference = 'Stop'

function Pause-NexusVersionTool {
	param([bool]$Enabled)

	if (-not $Enabled) {
		return
	}

	Write-Host ''
	Write-Host 'Press any key to exit...' -ForegroundColor DarkGray
	try {
		[Console]::ReadKey($true) | Out-Null
	}
	catch {
		Read-Host 'Press Enter to exit' | Out-Null
	}
}

function ConvertTo-NexusVersion {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Value
	)

	$trimmed = $Value.Trim()
	if ($trimmed.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
		$trimmed = $trimmed.Substring(1)
	}

	if ($trimmed -notmatch '^\d+(?:\.\d+){2,3}$') {
		throw 'Enter a version with three or four numeric parts, for example 1.0.0.2.'
	}

	$parts = $trimmed.Split('.')
	$normalizedParts = [System.Collections.Generic.List[string]]::new()
	foreach ($part in $parts) {
		$number = 0
		if (-not [int]::TryParse($part, [ref]$number) -or $number -lt 0 -or $number -gt 65535) {
			throw "Version part '$part' must be a number from 0 to 65535."
		}

		$normalizedParts.Add($number.ToString([System.Globalization.CultureInfo]::InvariantCulture))
	}

	while ($normalizedParts.Count -lt 4) {
		$normalizedParts.Add('0')
	}

	return $normalizedParts -join '.'
}

function Get-NexusMauiVersionMapping {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Version
	)

	$canonicalVersion = ConvertTo-NexusVersion $Version
	$parsedVersion = [System.Version]::Parse($canonicalVersion)
	return [pscustomobject]@{
		DisplayVersion = $parsedVersion.ToString(3)
		PortablePackageVersion = $canonicalVersion
		StorePackageVersion = "$($parsedVersion.ToString(3)).0"
	}
}

function Get-NexusVersionFromProps {
	param([Parameter(Mandatory = $true)][string]$Path)

	$content = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false))
	$match = [regex]::Match($content, '<NexusVersion>\s*(?<version>[^<]+?)\s*</NexusVersion>')
	if (-not $match.Success) {
		throw "NexusVersion was not found in $Path."
	}

	return ConvertTo-NexusVersion $match.Groups['version'].Value
}

function Set-NexusVersionFileContent {
	param(
		[Parameter(Mandatory = $true)][string]$Path,
		[Parameter(Mandatory = $true)][string]$Pattern,
		[Parameter(Mandatory = $true)][string]$Replacement
	)

	$utf8 = [System.Text.UTF8Encoding]::new($false)
	$content = [System.IO.File]::ReadAllText($Path, $utf8)
	$updated = [regex]::new($Pattern).Replace($content, $Replacement, 1)
	if ($updated -eq $content) {
		throw "Expected version entry was not found in $Path."
	}

	[System.IO.File]::WriteAllText($Path, $updated, $utf8)
}

$projectRoot = (Resolve-Path -LiteralPath $ProjectRoot).ProviderPath
$propsPath = Join-Path $projectRoot 'Directory.Build.props'
$manifestPath = Join-Path $projectRoot 'Platforms\Windows\app.manifest'
$shouldPause = -not $NoPause
$exitCode = 0

try {
	if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
		throw "Directory.Build.props was not found: $propsPath"
	}

	if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
		throw "Windows app manifest was not found: $manifestPath"
	}

	$currentVersion = Get-NexusVersionFromProps $propsPath
	$currentMauiVersion = Get-NexusMauiVersionMapping $currentVersion
	Write-Host ''
	Write-Host "[Nexus] Current source version : $currentVersion" -ForegroundColor Cyan
	Write-Host "[Nexus] Display version        : $($currentMauiVersion.DisplayVersion)" -ForegroundColor DarkGray
	Write-Host "[Nexus] Portable package       : $($currentMauiVersion.PortablePackageVersion)" -ForegroundColor DarkGray
	Write-Host "[Nexus] Store package          : $($currentMauiVersion.StorePackageVersion) (fourth part reserved as 0)" -ForegroundColor DarkGray
	$requestedVersion = Read-Host 'Enter the new version (for example 1.0.0.3, blank to cancel)'
	if ([string]::IsNullOrWhiteSpace($requestedVersion)) {
		Write-Host '[Nexus] Version update cancelled.' -ForegroundColor Yellow
		return
	}

	$newVersion = ConvertTo-NexusVersion $requestedVersion
	if ($newVersion -eq $currentVersion) {
		Write-Host "[Nexus] Version is already $currentVersion. No files were changed." -ForegroundColor Yellow
		return
	}

	$newMauiVersion = Get-NexusMauiVersionMapping $newVersion
	Write-Host ''
	Write-Host "[Nexus] Version change         : $currentVersion -> $newVersion" -ForegroundColor Cyan
	Write-Host "[Nexus] Display version        : $($newMauiVersion.DisplayVersion)" -ForegroundColor DarkGray
	Write-Host "[Nexus] Portable package       : $($newMauiVersion.PortablePackageVersion)" -ForegroundColor DarkGray
	Write-Host "[Nexus] Store package          : $($newMauiVersion.StorePackageVersion) (fourth part reserved as 0)" -ForegroundColor DarkGray
	$confirmation = (Read-Host 'Apply this version change? [Y/N]').Trim()
	if ($confirmation -notmatch '^(y|yes)$') {
		Write-Host '[Nexus] Version update cancelled.' -ForegroundColor Yellow
		return
	}

	Set-NexusVersionFileContent `
		-Path $propsPath `
		-Pattern '<NexusVersion>\s*[^<]+?\s*</NexusVersion>' `
		-Replacement "<NexusVersion>$newVersion</NexusVersion>"
	Set-NexusVersionFileContent `
		-Path $manifestPath `
		-Pattern '(<assemblyIdentity\s+version=")[^"]+("\s+name="ComfyUI-Nexus\.WinUI\.app"\s*/>)' `
		-Replacement "`${1}$newVersion`${2}"

	Write-Host ''
	Write-Host "[Nexus] Version updated successfully: $currentVersion -> $newVersion" -ForegroundColor Green
	Write-Host "[Nexus] Display version        : $($newMauiVersion.DisplayVersion)" -ForegroundColor DarkGray
	Write-Host "[Nexus] Portable package       : $($newMauiVersion.PortablePackageVersion)" -ForegroundColor DarkGray
	Write-Host "[Nexus] Store package          : $($newMauiVersion.StorePackageVersion) (fourth part reserved as 0)" -ForegroundColor DarkGray
	Write-Host "[Nexus] Updated: $propsPath" -ForegroundColor DarkGray
	Write-Host "[Nexus] Updated: $manifestPath" -ForegroundColor DarkGray
}
catch {
	Write-Host ''
	Write-Host "[Nexus] ERROR: $($_.Exception.Message)" -ForegroundColor Red
	$exitCode = 1
}
finally {
	Pause-NexusVersionTool $shouldPause
}

exit $exitCode
