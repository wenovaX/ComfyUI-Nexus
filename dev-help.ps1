param(
	[Parameter(Position = 0)]
	[string]$Action = 'help',

	[Parameter(ValueFromRemainingArguments = $true)]
	[string[]]$RemainingArguments
)

$toolsPath = Join-Path $PSScriptRoot 'tools\windows'

if ($Action -eq 'open') {
	if ($MyInvocation.InvocationName -eq '.') {
		Set-Location -LiteralPath $toolsPath
		return
	}

	$hostExecutable = Join-Path $PSHOME 'pwsh.exe'
	if (-not (Test-Path -LiteralPath $hostExecutable)) {
		$hostExecutable = Join-Path $PSHOME 'powershell.exe'
	}

	Start-Process -FilePath $hostExecutable -ArgumentList @(
		'-NoExit',
		'-Command',
		"Set-Location -LiteralPath '$toolsPath'"
	)
	return
}

& (Join-Path $toolsPath 'dev-help.ps1') $Action @RemainingArguments
exit $LASTEXITCODE
