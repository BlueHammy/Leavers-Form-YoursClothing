#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Grant an IIS app-pool identity read access to the LeaversForm automation certificate private key.

.EXAMPLE
  .\Grant-LeaversFormCertificateAccess.ps1 -Thumbprint "ABC123..." -AppPoolIdentity "SIZEUP\svc-leaversform"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Thumbprint,
    [Parameter(Mandatory = $true)]
    [string] $AppPoolIdentity,
    [ValidateSet('LocalMachine', 'CurrentUser')]
    [string] $StoreLocation = 'LocalMachine'
)

$ErrorActionPreference = 'Stop'

$clean = $Thumbprint -replace '\s', ''
$storePath = "Cert:\$StoreLocation\My"
$cert = Get-ChildItem $storePath | Where-Object { $_.Thumbprint -ieq $clean -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) {
    throw "Certificate $Thumbprint was not found in $storePath with a private key."
}

$rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
if (-not $rsa) {
    throw "Could not access the private key for certificate $Thumbprint."
}

$keyPath = $null
if ($rsa -is [System.Security.Cryptography.RSACng]) {
    $keyPath = $rsa.Key.UniqueName
}
elseif ($rsa -is [System.Security.Cryptography.RSACryptoServiceProvider]) {
    $keyPath = $rsa.CspKeyContainerInfo.UniqueKeyContainerName
}

if ([string]::IsNullOrWhiteSpace($keyPath)) {
    throw 'Could not resolve the certificate private key container path.'
}

$machineKeyDir = Join-Path $env:ProgramData "Microsoft\Crypto\RSA\MachineKeys"
$keyFile = Get-ChildItem $machineKeyDir -Filter $keyPath -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $keyFile) {
    $keyFile = Get-ChildItem $machineKeyDir -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*$keyPath*" } |
        Select-Object -First 1
}

if (-not $keyFile) {
    throw "Private key file not found under $machineKeyDir for certificate $Thumbprint."
}

$account = if ($AppPoolIdentity -eq 'ApplicationPoolIdentity') {
    'IIS AppPool\LeaversForm'
} else {
    $AppPoolIdentity
}

Write-Host "Granting $account read access to $($keyFile.FullName)" -ForegroundColor Cyan
$acl = Get-Acl $keyFile.FullName
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $account,
    'Read',
    'Allow')
$acl.AddAccessRule($rule)
Set-Acl $keyFile.FullName $acl
Write-Host 'Done.' -ForegroundColor Green
