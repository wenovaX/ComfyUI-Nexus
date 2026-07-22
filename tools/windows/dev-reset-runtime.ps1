param(
	[string]$ProjectRoot = (Join-Path $PSScriptRoot '..\..')
)

$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path -LiteralPath $ProjectRoot).ProviderPath
$runtimeRoot = Join-Path $projectRoot 'LocalRuntime'
$packagesRoot = Join-Path $runtimeRoot 'Packages'
$resolvedProjectRoot = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\')
$resolvedRuntimeRoot = [System.IO.Path]::GetFullPath($runtimeRoot).TrimEnd('\')
$resolvedPackagesRoot = [System.IO.Path]::GetFullPath($packagesRoot).TrimEnd('\')

if (-not $resolvedRuntimeRoot.StartsWith("$resolvedProjectRoot\", [System.StringComparison]::OrdinalIgnoreCase)) {
	throw "Refusing to reset a runtime outside the project root: $resolvedRuntimeRoot"
}

if (-not (Test-Path -LiteralPath $resolvedRuntimeRoot -PathType Container)) {
	throw "LocalRuntime was not found: $resolvedRuntimeRoot"
}

if (-not (Test-Path -LiteralPath $resolvedPackagesRoot -PathType Container)) {
	throw "Runtime packages were not found: $resolvedPackagesRoot"
}

$runningNexus = Get-Process -Name 'ComfyUI-Nexus' -ErrorAction SilentlyContinue
if ($null -ne $runningNexus) {
	throw 'ComfyUI-Nexus is still running. Exit Nexus before resetting LocalRuntime.'
}

function Get-NexusServerPort {
	param(
		[Parameter(Mandatory = $true)]
		[string]$ProjectRoot
	)

	$settingsPath = Join-Path $ProjectRoot 'nexus_settings.json'
	if (Test-Path -LiteralPath $settingsPath) {
		try {
			$settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding utf8 | ConvertFrom-Json
			$port = $settings.active_server_launch_settings.server_port
			$parsedPort = 0
			if ($null -eq $port) {
				$port = $settings.server_port
			}

			if ([int]::TryParse("$port", [ref]$parsedPort) -and $parsedPort -gt 0 -and $parsedPort -le 65535) {
				return $parsedPort
			}
		}
		catch {
			Write-Warning "[Nexus] Unable to read the configured server port: $($_.Exception.Message)"
		}
	}

	return 8188
}

function Get-PersistedServerProcessId {
	param(
		[Parameter(Mandatory = $true)]
		[string]$RuntimeRoot
	)

	$statePath = Join-Path $RuntimeRoot 'State\comfy-server-process.json'
	if (-not (Test-Path -LiteralPath $statePath)) {
		return $null
	}

	try {
		$state = Get-Content -LiteralPath $statePath -Raw -Encoding utf8 | ConvertFrom-Json
		$processId = 0
		if ([int]::TryParse("$($state.process_id)", [ref]$processId) -and $processId -gt 0) {
			return $processId
		}
	}
	catch {
		Write-Warning "[Nexus] Unable to read the persisted server process: $($_.Exception.Message)"
	}

	return $null
}

function Stop-NexusProcess {
	param(
		[Parameter(Mandatory = $true)]
		[int]$ProcessId,
		[Parameter(Mandatory = $true)]
		[string]$Description
	)

	Write-Host "[Nexus] Stopping $Description (PID: $ProcessId)"
	Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
	Wait-Process -Id $ProcessId -Timeout 5 -ErrorAction SilentlyContinue
	if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
		throw "$Description did not exit within 5 seconds."
	}
}

function Test-NexusRuntimeProcess {
	param(
		[Parameter(Mandatory = $true)]
		[int]$ProcessId,
		[Parameter(Mandatory = $true)]
		[string]$RuntimeRoot
	)

	$process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction SilentlyContinue
	if ($null -eq $process) {
		return $false
	}

	$runtimePrefix = "$RuntimeRoot\\"
	$executablePath = $process.ExecutablePath
	$commandLine = $process.CommandLine
	return ($executablePath -and $executablePath.StartsWith($runtimePrefix, [System.StringComparison]::OrdinalIgnoreCase)) -or
		($commandLine -and $commandLine.IndexOf($RuntimeRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
}

function Stop-NexusRuntimeServerListener {
	param(
		[Parameter(Mandatory = $true)]
		[string]$ProjectRoot,
		[Parameter(Mandatory = $true)]
		[string]$RuntimeRoot
	)

	$serverPort = Get-NexusServerPort -ProjectRoot $ProjectRoot
	$persistedProcessId = Get-PersistedServerProcessId -RuntimeRoot $RuntimeRoot
	try {
		$listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq $serverPort)
	}
	catch {
		Write-Warning "[Nexus] Unable to inspect server listener on port ${serverPort}: $($_.Exception.Message)"
		return
	}

	foreach ($listener in $listeners) {
		$processId = [int]$listener.OwningProcess
		$isPersistedServer = $persistedProcessId -eq $processId
		$isRuntimeServer = Test-NexusRuntimeProcess -ProcessId $processId -RuntimeRoot $RuntimeRoot
		if (-not $isRuntimeServer) {
			continue
		}

		$description = if ($isPersistedServer) {
			"persisted Nexus runtime server on port $serverPort"
		}
		else {
			"Nexus runtime server on port $serverPort"
		}
		Stop-NexusProcess -ProcessId $processId -Description $description
	}
}

function Stop-NexusRuntimeProcesses {
	param(
		[Parameter(Mandatory = $true)]
		[string]$RuntimeRoot
	)

	$runtimePrefix = "$RuntimeRoot\\"
	$processes = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
		$processName = $_.Name
		if ($processName -notin @('python.exe', 'pythonw.exe', 'git.exe')) {
			return $false
		}

		$executablePath = $_.ExecutablePath
		$commandLine = $_.CommandLine
		return ($executablePath -and $executablePath.StartsWith($runtimePrefix, [System.StringComparison]::OrdinalIgnoreCase)) -or
			($commandLine -and $commandLine.IndexOf($RuntimeRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
	})

	foreach ($process in $processes) {
		try {
			Stop-NexusProcess -ProcessId $process.ProcessId -Description "runtime process $($process.Name)"
		}
		catch {
			throw "Unable to stop runtime process $($process.Name) (PID: $($process.ProcessId)): $($_.Exception.Message)"
		}
	}
}

Stop-NexusRuntimeServerListener -ProjectRoot $resolvedProjectRoot -RuntimeRoot $resolvedRuntimeRoot
Stop-NexusRuntimeProcesses -RuntimeRoot $resolvedRuntimeRoot

function Clear-RuntimeItem {
	param(
		[Parameter(Mandatory = $true)]
		[System.IO.FileSystemInfo]$Item
	)

	if (-not (Test-Path -LiteralPath $Item.FullName)) {
		return
	}

	if ($Item.PSIsContainer) {
		try {
			$children = @(Get-ChildItem -LiteralPath $Item.FullName -Force -ErrorAction Stop)
		}
		catch [System.Management.Automation.ItemNotFoundException] {
			return
		}
		catch [System.IO.DirectoryNotFoundException] {
			return
		}

		$children | ForEach-Object {
			Clear-RuntimeItem -Item $_
		}

		try {
			$remainingChildren = @(Get-ChildItem -LiteralPath $Item.FullName -Force -ErrorAction Stop)
		}
		catch [System.Management.Automation.ItemNotFoundException] {
			return
		}
		catch [System.IO.DirectoryNotFoundException] {
			return
		}

		if ($remainingChildren.Count -eq 0) {
			try {
				Remove-Item -LiteralPath $Item.FullName -Force -ErrorAction Stop
			}
			catch [System.Management.Automation.ItemNotFoundException] {
			}
			catch [System.IO.DirectoryNotFoundException] {
			}
		}
		return
	}

	if ($Item.Name -ne '.gitkeep') {
		try {
			Remove-Item -LiteralPath $Item.FullName -Force -ErrorAction Stop
		}
		catch [System.Management.Automation.ItemNotFoundException] {
		}
		catch [System.IO.DirectoryNotFoundException] {
		}
	}
}

Get-ChildItem -LiteralPath $resolvedRuntimeRoot -Force | ForEach-Object {
	$target = [System.IO.Path]::GetFullPath($_.FullName).TrimEnd('\')
	if ([string]::Equals($target, $resolvedPackagesRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
		$_.Name -eq '.gitkeep') {
		return
	}

	if (-not $target.StartsWith("$resolvedRuntimeRoot\", [System.StringComparison]::OrdinalIgnoreCase)) {
		throw "Refusing to remove a path outside LocalRuntime: $target"
	}

	Write-Host "[Nexus] Removing: $($_.Name)"
	Clear-RuntimeItem -Item $_
}

foreach ($fileName in @('nexus_settings.json', 'nexus_settings.json.tmp', 'nexus-session-state.json')) {
	$target = Join-Path $resolvedProjectRoot $fileName
	if (-not (Test-Path -LiteralPath $target)) {
		continue
	}

	Write-Host "[Nexus] Removing: $fileName"
	try {
		Remove-Item -LiteralPath $target -Force -ErrorAction Stop
	}
	catch [System.Management.Automation.ItemNotFoundException] {
	}
}

Write-Host '[Nexus] LocalRuntime reset complete.'
Write-Host '[Nexus] Preserved: Packages required for a fresh Nexus installation and tracked .gitkeep files.'
