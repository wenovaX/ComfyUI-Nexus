[CmdletBinding()]
param(
	[Parameter(Position = 0)]
	[ValidateSet('help', 'open', 'root', 'docs')]
	[string]$Action = 'help'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath

switch ($Action) {
	'open' {
		Write-Host 'From the repository root:'
		Write-Host '  PowerShell current shell: . .\dev-help.ps1 open'
		Write-Host '  PowerShell new shell:     .\dev-help.ps1 open'
		Write-Host '  Git Bash current shell:   . ./dev-help open'
		Write-Host '  Git Bash new shell:       ./dev-help open'
		Write-Host '  Command Prompt:           call dev-help.bat open'
		return
	}
	'root' {
		Start-Process explorer.exe -ArgumentList $projectRoot
		return
	}
	'docs' {
		Start-Process explorer.exe -ArgumentList (Join-Path $projectRoot 'docs')
		return
	}
}

Write-Host 'ComfyUI-Nexus developer tools'
Write-Host ''
Write-Host 'Common commands:'
Write-Host '  tools\windows\dev-build-as-binary.bat Release folder archive'
Write-Host '      Build a portable Windows release folder and ZIP archive.'
Write-Host '  tools\windows\dev-build-as-binary.bat Release app-store clean'
Write-Host '      Build a Microsoft Store upload package.'
Write-Host '  .\tools\windows\dev-reset-runtime.ps1'
Write-Host '      Stop the managed server and reset the local runtime.'
Write-Host '  .\tools\windows\dev-set-version.ps1'
Write-Host '      Validate and update the project version.'
Write-Host '  .\tools\windows\dev-test-store-package.ps1 help'
Write-Host '      Show local Store package test commands.'
Write-Host ''
Write-Host 'Shell entry points under tools\windows:'
Write-Host '  Command Prompt: dev-*.bat'
Write-Host '  PowerShell:     dev-*.ps1'
Write-Host '  Git Bash:       extensionless dev-*'
Write-Host ''
Write-Host 'The repository root intentionally contains only this dev-help guide.'
Write-Host 'Developer documentation: docs\DEVELOPERS.md'
Write-Host ''
Write-Host 'Shortcuts:'
Write-Host '  dev-help open  Enter tools\windows in the current or a new shell.'
Write-Host '  dev-help root  Open the repository root in Explorer.'
Write-Host '  dev-help docs  Open docs in Explorer.'
