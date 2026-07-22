[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[ValidateNotNullOrEmpty()]
	[string]$Name
)

. "$PSScriptRoot\dev-code-signing.ps1"
$certificate = Get-NexusCodeSigningCertificate -Name $Name

Write-Host "[Nexus] Code-signing certificate ready: $($certificate.Subject) ($($certificate.Thumbprint))"
