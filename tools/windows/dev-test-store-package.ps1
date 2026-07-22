[CmdletBinding()]
param(
	[Parameter(Position = 0)]
	[ValidateSet('install', 'status', 'remove', 'help')]
	[string]$Action = 'help',

	[string]$PackagePath,

	[switch]$ReplaceExisting,

	[switch]$UpdateExisting,

	[switch]$ResetData,

	[switch]$RemoveCertificate,

	[switch]$Launch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestCertificateFriendlyName = 'Nexus Store Local Test'
$script:WindowsSdkPackageId = 'Microsoft.WindowsSDK.10.0.26100'

function Show-Help {
	Write-Host 'Nexus Store Package Local Test'
	Write-Host ''
Write-Host 'Quick start:'
	Write-Host '  .\tools\windows\dev-test-store-package.ps1 install -ReplaceExisting -ResetData -Launch'
	Write-Host '      Select a recent Store build from the numbered list.'
	Write-Host '  .\tools\windows\dev-test-store-package.ps1 install -UpdateExisting -Launch'
	Write-Host '      Update the installed Store package while retaining package data.'
	Write-Host '  .\tools\windows\dev-test-store-package.ps1 install -PackagePath "<path-to-store.msix>" -ReplaceExisting -ResetData -Launch'
	Write-Host '  .\tools\windows\dev-test-store-package.ps1 remove -ResetData -RemoveCertificate'
	Write-Host ''
	Write-Host 'Usage:'
	Write-Host '  .\tools\windows\dev-test-store-package.ps1 install [-PackagePath <msix>] [-ReplaceExisting | -UpdateExisting] [-ResetData] [-Launch]'
	Write-Host '  .\tools\windows\dev-test-store-package.ps1 status'
	Write-Host '  .\tools\windows\dev-test-store-package.ps1 remove [-ResetData] [-RemoveCertificate]'
	Write-Host '  .\tools\windows\dev-test-store-package.ps1 help'
	Write-Host ''
	Write-Host 'Commands:'
	Write-Host '  install  Signs a copy of the Store MSIX with a local test certificate and installs it.'
	Write-Host '           Without -PackagePath, select a Store_Release build by number.'
	Write-Host '           The certificate subject matches the local Partner Center Publisher value.'
	Write-Host '  status   Shows the installed local Store package, package data, and Nexus-owned temporary tooling drive mappings.'
	Write-Host '  remove   Removes the locally installed Store package. Use -ResetData to remove package data, runtime data, and Nexus-owned temporary tooling drive mappings.'
	Write-Host '  help     Shows this help text.'
	Write-Host ''
	Write-Host 'Safety:'
	Write-Host '  The original MSIX and .msixupload are never modified.'
	Write-Host '  install writes a signed copy under build\Store_*\local-test.'
	Write-Host '  An existing package is never removed unless -ReplaceExisting is supplied.'
	Write-Host '  -UpdateExisting installs a higher package version in place and retains package data.'
	Write-Host '  Package data and runtime data are retained unless -ResetData is supplied. Tooling drive mappings exist only while setup tooling is active.'
	Write-Host '  remove deletes locally generated *-local-test.msix copies under build\Store_*\local-test.'
	Write-Host '  install requests elevation once to trust its local test certificate for MSIX installation.'
	Write-Host '  remove -RemoveCertificate removes the local test certificate from current-user and local-machine stores.'
	Write-Host '  install installs Windows SDK Signing Tools through winget when signtool.exe is unavailable.'
}

function Get-StoreIdentity {
	param([Parameter(Mandatory = $true)][string]$ProjectRoot)

	$propsPath = Join-Path $ProjectRoot 'Store.Build.props'
	if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
		throw '[Nexus] Store.Build.props was not found. Store package local testing requires the local Partner Center identity.'
	}

	[xml]$props = Get-Content -LiteralPath $propsPath -Raw -Encoding utf8
	$values = @{}
	foreach ($propertyGroup in @($props.Project.PropertyGroup)) {
		foreach ($name in 'NexusStoreIdentityName', 'NexusStorePublisher') {
			if (-not [string]::IsNullOrWhiteSpace($propertyGroup.$name)) {
				$values[$name] = [string]$propertyGroup.$name
			}
		}
	}

	$missing = @(@('NexusStoreIdentityName', 'NexusStorePublisher') |
		Where-Object { -not $values.ContainsKey($_) })
	if ($missing.Count -gt 0) {
		throw "[Nexus] Store.Build.props is missing: $($missing -join ', ')."
	}

	return [pscustomobject]@{
		Name = $values.NexusStoreIdentityName
		Publisher = $values.NexusStorePublisher
	}
}

function Get-PackageManifest {
	param([Parameter(Mandatory = $true)][string]$MsixPath)

	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$archive = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
	try {
		$entry = $archive.GetEntry('AppxManifest.xml')
		if ($null -eq $entry) {
			throw '[Nexus] AppxManifest.xml was not found in the MSIX package.'
		}

		$reader = [System.IO.StreamReader]::new($entry.Open())
		try {
			[xml]$manifest = $reader.ReadToEnd()
			return $manifest.Package
		}
		finally {
			$reader.Dispose()
		}
	}
	finally {
		$archive.Dispose()
	}
}

function Get-StorePackageCandidates {
	param([Parameter(Mandatory = $true)][string]$ProjectRoot)

	$buildRoot = Join-Path $ProjectRoot 'build'
	if (-not (Test-Path -LiteralPath $buildRoot -PathType Container)) {
		return @()
	}

	$candidates = foreach ($buildDirectory in Get-ChildItem -LiteralPath $buildRoot -Directory -Filter 'Store_Release_*') {
		if ($buildDirectory.Name -notmatch '^Store_Release_(?<stamp>\d{8}_\d{6})$') {
			continue
		}

		$buildStamp = $matches.stamp

		$package = Get-ChildItem -LiteralPath (Join-Path $buildDirectory.FullName 'upload') `
			-File -Filter '*.msix' -ErrorAction SilentlyContinue |
			Sort-Object Name |
			Select-Object -First 1
		if ($null -eq $package) {
			continue
		}

		[pscustomobject]@{
			BuildName = $buildDirectory.Name
			Stamp = $buildStamp
			PackagePath = $package.FullName
		}
	}

	return @($candidates | Sort-Object Stamp -Descending)
}

function Select-StoreMsix {
	param([Parameter(Mandatory = $true)][string]$ProjectRoot)

	$candidates = @(Get-StorePackageCandidates -ProjectRoot $ProjectRoot)
	if ($candidates.Count -eq 0) {
		Write-Host '[Nexus] No Store MSIX build was found. Build a Store package first.' -ForegroundColor Yellow
		return $null
	}

	Write-Host '[Nexus] Available Store builds:'
	for ($index = 0; $index -lt $candidates.Count; $index++) {
		$candidate = $candidates[$index]
		Write-Host ('  [{0}] {1}' -f ($index + 1), $candidate.BuildName)
	}

	$selection = Read-Host "[Nexus] Select a build number (1-$($candidates.Count), Enter for latest)"
	if ([string]::IsNullOrWhiteSpace($selection)) {
		return $candidates[0].PackagePath
	}

	$selectedIndex = 0
	if (-not [int]::TryParse($selection, [ref]$selectedIndex) -or
		$selectedIndex -lt 1 -or
		$selectedIndex -gt $candidates.Count) {
		Write-Host "[Nexus] Invalid Store build selection: $selection" -ForegroundColor Yellow
		return $null
	}

	return $candidates[$selectedIndex - 1].PackagePath
}

function Find-SignTool {
	$windowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
	if (-not (Test-Path -LiteralPath $windowsKitsRoot -PathType Container)) {
		return $null
	}

	$signTools = @(Get-ChildItem -LiteralPath $windowsKitsRoot -Recurse -File -Filter 'signtool.exe' -ErrorAction SilentlyContinue)
	$signTool = @($signTools | Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
		Sort-Object FullName -Descending |
		Select-Object -First 1)
	if ($signTool.Count -gt 0) {
		return $signTool[0].FullName
	}

	$signTool = @($signTools | Sort-Object FullName -Descending | Select-Object -First 1)
	return if ($signTool.Count -gt 0) { $signTool[0].FullName } else { $null }
}

function Ensure-SignTool {
	$signToolPath = Find-SignTool
	if (-not [string]::IsNullOrWhiteSpace($signToolPath)) {
		return $signToolPath
	}

	$winget = Get-Command winget.exe -ErrorAction SilentlyContinue
	if ($null -eq $winget) {
		throw '[Nexus] signtool.exe was not found and winget is unavailable. Install the Windows SDK Signing Tools component, then run this command again.'
	}

	Write-Host '[Nexus] Windows SDK Signing Tools were not found. Installing the required Windows SDK package...'
	& $winget.Source install `
		--id $script:WindowsSdkPackageId `
		--exact `
		--source winget `
		--accept-package-agreements `
		--accept-source-agreements `
		--disable-interactivity
	if ($LASTEXITCODE -ne 0) {
		throw '[Nexus] Windows SDK Signing Tools installation failed. Complete the SDK installation, then run this command again.'
	}

	$signToolPath = Find-SignTool
	if ([string]::IsNullOrWhiteSpace($signToolPath)) {
		throw '[Nexus] Windows SDK installation completed, but signtool.exe was still not found. Restart the terminal and run this command again.'
	}

	Write-Host "[Nexus] Windows SDK Signing Tools are ready: $signToolPath"
	return $signToolPath
}

function Get-OrCreateTestCertificate {
	param([Parameter(Mandatory = $true)][string]$Publisher)

	$certificate = Get-ChildItem Cert:\CurrentUser\My |
		Where-Object {
			$_.FriendlyName -eq $script:TestCertificateFriendlyName -and
			$_.Subject -eq $Publisher -and
			$_.HasPrivateKey -and
			$_.NotAfter -gt (Get-Date)
		} |
		Select-Object -First 1
	if ($null -eq $certificate) {
		Write-Host '[Nexus] Creating a local MSIX test certificate...'
		$certificate = New-SelfSignedCertificate `
			-Subject $Publisher `
			-Type CodeSigningCert `
			-CertStoreLocation 'Cert:\CurrentUser\My' `
			-FriendlyName $script:TestCertificateFriendlyName `
			-KeyExportPolicy NonExportable
	}

	foreach ($storeName in 'TrustedPeople', 'Root') {
		$store = [System.Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'CurrentUser')
		$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
		try {
			if (-not @($store.Certificates | Where-Object { $_.Thumbprint -eq $certificate.Thumbprint })) {
				$store.Add($certificate)
			}
		}
		finally {
			$store.Close()
		}
	}

	return $certificate
}

function Test-LocalMachineCertificateTrust {
	param([Parameter(Mandatory = $true)][string]$Thumbprint)

	foreach ($storeName in 'Root', 'TrustedPeople') {
		$store = [System.Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'LocalMachine')
		$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
		try {
			if (-not @($store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint })) {
				return $false
			}
		}
		finally {
			$store.Close()
		}
	}

	return $true
}

function Invoke-ElevatedCertificateStoreUpdate {
	param([Parameter(Mandatory = $true)][string]$Command)

	$encodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($Command))
	$process = Start-Process `
		-FilePath 'powershell.exe' `
		-ArgumentList "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand" `
		-Verb RunAs `
		-Wait `
		-PassThru
	if ($process.ExitCode -ne 0) {
		throw '[Nexus] The elevated local MSIX test certificate update did not complete.'
	}
}

function Ensure-LocalMachineCertificateTrust {
	param([Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

	if (Test-LocalMachineCertificateTrust -Thumbprint $Certificate.Thumbprint) {
		return
	}

	$certificatePath = Join-Path $env:TEMP "Nexus-Store-Local-Test-$($Certificate.Thumbprint).cer"
	Export-Certificate -Cert $Certificate -FilePath $certificatePath -Force | Out-Null
	$escapedCertificatePath = $certificatePath.Replace("'", "''")
	$escapedThumbprint = $Certificate.Thumbprint.Replace("'", "''")
	$command = @"
`$ErrorActionPreference = 'Stop'
`$certificatePath = '$escapedCertificatePath'
`$thumbprint = '$escapedThumbprint'
foreach (`$storePath in 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPeople') {
	if (-not @(Get-ChildItem -Path `$storePath | Where-Object { `$_.Thumbprint -eq `$thumbprint })) {
		Import-Certificate -FilePath `$certificatePath -CertStoreLocation `$storePath | Out-Null
	}
}
"@
	try {
		Write-Host '[Nexus] Requesting elevation to trust the local MSIX test certificate...'
		Invoke-ElevatedCertificateStoreUpdate -Command $command
	}
	finally {
		Remove-Item -LiteralPath $certificatePath -Force -ErrorAction SilentlyContinue
	}

	if (-not (Test-LocalMachineCertificateTrust -Thumbprint $Certificate.Thumbprint)) {
		throw '[Nexus] The local MSIX test certificate is not trusted by the local machine after elevation.'
	}
}

function Get-InstalledPackages {
	param([Parameter(Mandatory = $true)][string]$PackageName)

	return @(Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue)
}

function Get-LocalStateDirectories {
	param([Parameter(Mandatory = $true)][string]$PackageName)

	return @(Get-PackageDataDirectories -PackageName $PackageName |
		ForEach-Object { Join-Path $_ 'LocalState' } |
		Where-Object { Test-Path -LiteralPath $_ -PathType Container })
}

function Get-PackageDataDirectories {
	param([Parameter(Mandatory = $true)][string]$PackageName)

	$packagesRoot = Join-Path $env:LOCALAPPDATA 'Packages'
	if (-not (Test-Path -LiteralPath $packagesRoot -PathType Container)) {
		return @()
	}

	return @(Get-ChildItem -LiteralPath $packagesRoot -Directory -Filter "$PackageName`_*" -ErrorAction SilentlyContinue |
		ForEach-Object { $_.FullName })
}

function Remove-PackageData {
	param(
		[Parameter(Mandatory = $true)][string]$PackageName,
		[string[]]$PackageDataDirectories = @()
	)

	if ($PackageDataDirectories.Count -eq 0) {
		$PackageDataDirectories = @(Get-PackageDataDirectories -PackageName $PackageName)
	}
	Remove-StoreRuntimeDriveMappings -PackageName $PackageName

	foreach ($packageDataDirectory in $PackageDataDirectories) {
		$packagesRoot = (Join-Path $env:LOCALAPPDATA 'Packages')
		$resolvedPackagesRoot = [System.IO.Path]::GetFullPath($packagesRoot).TrimEnd([char[]]@([char]'\', [char]'/'))
		$resolvedPackageDataDirectory = [System.IO.Path]::GetFullPath($packageDataDirectory).TrimEnd([char[]]@([char]'\', [char]'/'))
		if (-not $resolvedPackageDataDirectory.StartsWith($resolvedPackagesRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
			throw "[Nexus] Refusing to remove data outside the package data root: $packageDataDirectory"
		}

		if (Test-Path -LiteralPath $packageDataDirectory -PathType Container) {
			Write-Host "[Nexus] Removing local Store test package data: $packageDataDirectory"
			Remove-Item -LiteralPath $packageDataDirectory -Recurse -Force
		}
	}
}

function Get-DirectorySizeBytes {
	param([Parameter(Mandatory = $true)][string]$Path)

	if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
		return 0
	}

	$size = (Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
		Measure-Object -Property Length -Sum).Sum
	if ($null -eq $size) {
		return 0
	}

	return [long]$size
}

function Get-NexusToolingLeaseRegistryPath {
	return Join-Path $env:LOCALAPPDATA 'NexusForComfyUI\Tooling\tooling-path-leases.json'
}

function Get-StoreRuntimeDriveMappings {
	param([Parameter(Mandatory = $true)][string]$PackageName)

	$runtimeRoots = @(Get-LocalStateDirectories -PackageName $PackageName |
		ForEach-Object { ([System.IO.Path]::GetFullPath((Join-Path $_ 'Nexus\LocalRuntime'))).TrimEnd([char[]]@([char]'\', [char]'/')) })
	if ($runtimeRoots.Count -eq 0) {
		return @()
	}

	$activeMappings = @{}
	& subst.exe | ForEach-Object {
		$parts = $_ -split ': => ', 2
		$driveRoot = if ($parts.Count -eq 2) { [string]$parts[0] } else { [string]::Empty }
		if ($driveRoot.Length -eq 3 -and
			[char]::IsLetter($driveRoot[0]) -and
			$driveRoot[1] -eq ':' -and
			$driveRoot[2] -eq '\') {
			$drive = $driveRoot.Substring(0, 2).ToUpperInvariant()
			$activeMappings[$drive] = ([System.IO.Path]::GetFullPath($parts[1])).TrimEnd([char[]]@([char]'\', [char]'/'))
		}
	}

	$ownedMappings = @{}
	$registryPath = Get-NexusToolingLeaseRegistryPath
	if (Test-Path -LiteralPath $registryPath -PathType Leaf) {
		try {
			$records = @(Get-Content -LiteralPath $registryPath -Raw -Encoding utf8 | ConvertFrom-Json)
		}
		catch {
			Write-Warning "[Nexus] Unable to read Nexus tooling lease registry: $registryPath"
			$records = @()
		}

		foreach ($record in $records) {
			if ($record.Schema -ne 'Nexus.ToolingPathLease.v2' -or
				[string]::IsNullOrWhiteSpace([string]$record.Drive) -or
				[string]::IsNullOrWhiteSpace([string]$record.Target)) {
				continue
			}

			$drive = ([string]$record.Drive).Substring(0, 2).ToUpperInvariant()
			$target = ([System.IO.Path]::GetFullPath([string]$record.Target)).TrimEnd([char[]]@([char]'\', [char]'/'))
			$belongsToStoreRuntime = $runtimeRoots | Where-Object {
				[string]::Equals($target, $_, [System.StringComparison]::OrdinalIgnoreCase) -or
				$target.StartsWith($_ + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
			}
			if ($belongsToStoreRuntime -and $activeMappings.ContainsKey($drive) -and
				[string]::Equals($activeMappings[$drive], $target, [System.StringComparison]::OrdinalIgnoreCase)) {
				$ownedMappings[$drive] = [pscustomobject]@{ Drive = $drive; Target = $target; Source = 'registry' }
			}
		}
	}

	# ResetData removes these LocalState roots immediately after this check, so an
	# unregistered legacy alias to the same runtime is safe to release as well.
	foreach ($entry in $activeMappings.GetEnumerator()) {
		$belongsToStoreRuntime = $runtimeRoots | Where-Object {
			[string]::Equals($entry.Value, $_, [System.StringComparison]::OrdinalIgnoreCase) -or
			$entry.Value.StartsWith($_ + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
		}
		if ($belongsToStoreRuntime -and -not $ownedMappings.ContainsKey($entry.Key)) {
			$ownedMappings[$entry.Key] = [pscustomobject]@{ Drive = $entry.Key; Target = $entry.Value; Source = 'local-state' }
		}
	}

	return @($ownedMappings.Values | Sort-Object Drive)
}
function Remove-StoreRuntimeDriveMappings {
	param([Parameter(Mandatory = $true)][string]$PackageName)

	foreach ($mapping in Get-StoreRuntimeDriveMappings -PackageName $PackageName) {
		Write-Host "[Nexus] Removing Store runtime temporary tooling drive mapping: $($mapping.Drive)"
		& subst.exe $mapping.Drive /d
		if ($LASTEXITCODE -ne 0) {
			throw "[Nexus] Unable to remove Store runtime drive mapping: $($mapping.Drive)"
		}
	}
}
function Remove-LocalTestPackageCopies {
	param([Parameter(Mandatory = $true)][string]$ProjectRoot)

	$buildRoot = Join-Path $ProjectRoot 'build'
	if (-not (Test-Path -LiteralPath $buildRoot -PathType Container)) {
		return
	}

	$testDirectories = @(Get-ChildItem -LiteralPath $buildRoot -Directory -Filter 'Store_*' -ErrorAction SilentlyContinue |
		ForEach-Object { Join-Path $_.FullName 'local-test' } |
		Where-Object { Test-Path -LiteralPath $_ -PathType Container })
	foreach ($testDirectory in $testDirectories) {
		foreach ($packageCopy in Get-ChildItem -LiteralPath $testDirectory -File -Filter '*-local-test.msix' -ErrorAction SilentlyContinue) {
			Write-Host "[Nexus] Removing local Store test package copy: $($packageCopy.FullName)"
			Remove-Item -LiteralPath $packageCopy.FullName -Force
		}

		if (-not @(Get-ChildItem -LiteralPath $testDirectory -Force -ErrorAction SilentlyContinue)) {
			Remove-Item -LiteralPath $testDirectory -Force
		}
	}
}

function Get-RunningPackageProcesses {
	param([Parameter(Mandatory = $true)][string]$PackageName)

	$packages = @(Get-InstalledPackages -PackageName $PackageName)
	return @(
		foreach ($package in $packages) {
			$installLocation = [string]$package.InstallLocation
			if ([string]::IsNullOrWhiteSpace($installLocation)) {
				continue
			}

			$normalizedInstallLocation = ([System.IO.Path]::GetFullPath($installLocation)).TrimEnd([char[]]@([char]'\', [char]'/'))
			foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
				try {
					$processPath = [string]$process.Path
					if (-not [string]::IsNullOrWhiteSpace($processPath) -and
						$processPath.StartsWith($normalizedInstallLocation + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
						[pscustomobject]@{
							Id = $process.Id
							Name = $process.ProcessName
							Path = $processPath
						}
					}
				}
				catch {
					# Some processes do not expose their executable path to the current user.
				}
			}
		}
	)
}

function Assert-PackageIsNotRunning {
	param([Parameter(Mandatory = $true)][string]$PackageName)

	$runningPackageProcesses = @(Get-RunningPackageProcesses -PackageName $PackageName)

	if ($runningPackageProcesses.Count -gt 0) {
		$processSummary = ($runningPackageProcesses | ForEach-Object { "$($_.Name) (PID $($_.Id))" }) -join ', '
		throw "[Nexus] The Store package is still running: $processSummary. Close Nexus from its own Exit command so it can safely stop its server, then run this command again."
	}
}

function Remove-InstalledPackage {
	param([Parameter(Mandatory = $true)][string]$PackageName)

	Assert-PackageIsNotRunning -PackageName $PackageName

	foreach ($package in Get-InstalledPackages -PackageName $PackageName) {
		Write-Host "[Nexus] Removing installed package: $($package.PackageFullName)"
		Remove-AppxPackage -Package $package.PackageFullName
	}
}

function Remove-TestCertificate {
	param([Parameter(Mandatory = $true)][string]$Publisher)

	$certificate = Get-ChildItem Cert:\CurrentUser\My |
		Where-Object {
			$_.FriendlyName -eq $script:TestCertificateFriendlyName -and
			$_.Subject -eq $Publisher
		} |
		Select-Object -First 1
	if ($null -eq $certificate) {
		return
	}

	$escapedThumbprint = $certificate.Thumbprint.Replace("'", "''")
	$command = @"
`$ErrorActionPreference = 'Stop'
`$thumbprint = '$escapedThumbprint'
foreach (`$storePath in 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPeople') {
	Get-ChildItem -Path `$storePath | Where-Object { `$_.Thumbprint -eq `$thumbprint } | Remove-Item -Force
}
"@
	Write-Host '[Nexus] Requesting elevation to remove the local MSIX test certificate trust...'
	Invoke-ElevatedCertificateStoreUpdate -Command $command

	foreach ($storeName in 'TrustedPeople', 'Root') {
		$store = [System.Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'CurrentUser')
		$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
		try {
			$store.Remove($certificate)
		}
		finally {
			$store.Close()
		}
	}

	Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force
	Write-Host '[Nexus] Removed the local MSIX test certificate.'
}

function Install-StorePackage {
	param(
		[Parameter(Mandatory = $true)][string]$SourcePackagePath,
		[Parameter(Mandatory = $true)][psobject]$StoreIdentity
	)

	$manifest = Get-PackageManifest -MsixPath $SourcePackagePath
	if ($manifest.Identity.Name -ne $StoreIdentity.Name -or $manifest.Identity.Publisher -ne $StoreIdentity.Publisher) {
		throw '[Nexus] The MSIX identity does not match Store.Build.props. Rebuild the Store package before testing.'
	}

	if ($ReplaceExisting -and $UpdateExisting) {
		throw '[Nexus] Use either -ReplaceExisting or -UpdateExisting, not both.'
	}

	if ($ResetData -and $UpdateExisting) {
		throw '[Nexus] -ResetData cannot be used with -UpdateExisting because Store updates retain package data.'
	}

	$installed = @(Get-InstalledPackages -PackageName $manifest.Identity.Name)
	if ($installed.Count -gt 0 -and -not $ReplaceExisting -and -not $UpdateExisting) {
		throw '[Nexus] A package with this Store identity is already installed. Re-run with -UpdateExisting to keep package data, or -ReplaceExisting to remove it first.'
	}

	if ($UpdateExisting -and $installed.Count -eq 0) {
		throw '[Nexus] No matching Store package is installed. Use install without -UpdateExisting for the first local test install.'
	}

	if ($UpdateExisting) {
		Assert-PackageIsNotRunning -PackageName $manifest.Identity.Name
		$nextVersion = [version]$manifest.Identity.Version
		$currentVersion = [version]$installed[0].Version
		if ($nextVersion -le $currentVersion) {
			throw "[Nexus] Store update requires a higher package version. Installed: $currentVersion. Selected: $nextVersion."
		}
	}

	$packageDataDirectories = @()
	if ($ResetData) {
		$packageDataDirectories = @(Get-PackageDataDirectories -PackageName $manifest.Identity.Name)
		Remove-StoreRuntimeDriveMappings -PackageName $manifest.Identity.Name
	}

	if ($installed.Count -gt 0 -and $ReplaceExisting) {
		Remove-InstalledPackage -PackageName $manifest.Identity.Name
	}
	if ($ResetData) {
		Remove-PackageData -PackageName $manifest.Identity.Name -PackageDataDirectories $packageDataDirectories
	}

	$signToolPath = Ensure-SignTool
	$certificate = Get-OrCreateTestCertificate -Publisher $StoreIdentity.Publisher
	Ensure-LocalMachineCertificateTrust -Certificate $certificate
	$testDirectory = Join-Path (Split-Path -Parent (Split-Path -Parent $SourcePackagePath)) 'local-test'
	New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
	$testPackageName = '{0}-local-test.msix' -f [System.IO.Path]::GetFileNameWithoutExtension($SourcePackagePath)
	$testPackagePath = Join-Path -Path $testDirectory -ChildPath $testPackageName
	Copy-Item -LiteralPath $SourcePackagePath -Destination $testPackagePath -Force

	Write-Host '[Nexus] Signing a local test copy of the Store MSIX...'
	& $signToolPath sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $testPackagePath
	if ($LASTEXITCODE -ne 0) {
		throw '[Nexus] Local MSIX signing failed.'
	}

	& $signToolPath verify /pa $testPackagePath
	if ($LASTEXITCODE -ne 0) {
		throw '[Nexus] Local MSIX signature verification failed.'
	}

	$installationVerb = if ($UpdateExisting) { 'Updating' } else { 'Installing' }
	Write-Host "[Nexus] $installationVerb the local Store package test copy..."
	Add-AppxPackage -Path $testPackagePath

	$installedPackage = @(Get-InstalledPackages -PackageName $manifest.Identity.Name) | Select-Object -First 1
	if ($null -eq $installedPackage) {
		throw '[Nexus] MSIX installation completed without a registered package.'
	}

	Write-Host ''
	Write-Host '[Nexus] Store package local test install complete.'
	Write-Host "[Nexus] Package : $($installedPackage.PackageFullName)"
	Write-Host "[Nexus] Install : $($installedPackage.InstallLocation)"
	Write-Host '[Nexus] Verify first-run setup, runtime installation, restart, shutdown, and relaunch from the Start menu.'

	if ($Launch) {
		$appId = $manifest.Applications.Application.Id
		if (-not [string]::IsNullOrWhiteSpace($appId)) {
			Start-Process "shell:AppsFolder\$($installedPackage.PackageFamilyName)!$appId"
		}
	}
}

switch ($Action) {
	'help' {
		Show-Help
	}
	default {
		$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath
		$storeIdentity = Get-StoreIdentity -ProjectRoot $projectRoot

		switch ($Action) {
			'status' {
				$packages = @(Get-InstalledPackages -PackageName $storeIdentity.Name)
				if ($packages.Count -eq 0) {
					Write-Host '[Nexus] No local Store package is installed.'
				}
				else {
					$packages | Select-Object Name, PackageFullName, PackageFamilyName, InstalledLocation | Format-List
				}

				$packageDataDirectories = @(Get-PackageDataDirectories -PackageName $storeIdentity.Name)
				if ($packageDataDirectories.Count -gt 0) {
					Write-Host '[Nexus] Package data directories:'
					$packageDataDirectories | ForEach-Object {
						$sizeMb = [math]::Round((Get-DirectorySizeBytes -Path $_) / 1MB, 2)
						Write-Host "[Nexus]   $_ ($sizeMb MB)"
					}
				}

				$runtimeMappings = @(Get-StoreRuntimeDriveMappings -PackageName $storeIdentity.Name)
				if ($runtimeMappings.Count -gt 0) {
					Write-Host '[Nexus] Store runtime drive mappings:'
					$runtimeMappings | ForEach-Object { Write-Host "[Nexus]   $($_.Drive) -> $($_.Target)" }
				}
			}
			'remove' {
				$packageDataDirectories = @()
				if ($ResetData) {
					$packageDataDirectories = @(Get-PackageDataDirectories -PackageName $storeIdentity.Name)
					Remove-StoreRuntimeDriveMappings -PackageName $storeIdentity.Name
				}
				Remove-InstalledPackage -PackageName $storeIdentity.Name
				if ($ResetData) {
					Remove-PackageData -PackageName $storeIdentity.Name -PackageDataDirectories $packageDataDirectories
				}
				Remove-LocalTestPackageCopies -ProjectRoot $projectRoot
				if ($RemoveCertificate) {
					Remove-TestCertificate -Publisher $storeIdentity.Publisher
				}
				Write-Host '[Nexus] Local Store package test cleanup complete.'
			}
			'install' {
				if ([string]::IsNullOrWhiteSpace($PackagePath)) {
					$PackagePath = Select-StoreMsix -ProjectRoot $projectRoot
					if ([string]::IsNullOrWhiteSpace($PackagePath)) {
						return
					}
				}
				if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
					throw "[Nexus] Store MSIX was not found: $PackagePath"
				}

				$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
				Install-StorePackage -SourcePackagePath $resolvedPackagePath -StoreIdentity $storeIdentity
			}
		}
	}
}
