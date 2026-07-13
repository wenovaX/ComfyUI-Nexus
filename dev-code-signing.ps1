function Get-NexusCodeSigningCertificate {
	param(
		[Parameter(Mandatory = $true)]
		[ValidateNotNullOrEmpty()]
		[string]$Name
	)

	if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
		throw '[Nexus] Local Authenticode signing is supported only on Windows.'
	}

	$now = Get-Date
	$codeSigningEku = '1.3.6.1.5.5.7.3.3'
	$certificate = Get-ChildItem 'Cert:\CurrentUser\My', 'Cert:\LocalMachine\My' -ErrorAction SilentlyContinue |
		Where-Object {
			$_.HasPrivateKey -and
			($_.Subject -eq "CN=$Name" -or $_.FriendlyName -eq $Name) -and
			$_.NotBefore -le $now -and
			$_.NotAfter -gt $now -and
			($_.EnhancedKeyUsageList.ObjectId -contains $codeSigningEku)
		} |
		Select-Object -First 1

	if ($null -eq $certificate) {
		throw "[Nexus] Code-signing certificate was not found, is expired, lacks a private key, or cannot sign: $Name"
	}

	return $certificate
}
