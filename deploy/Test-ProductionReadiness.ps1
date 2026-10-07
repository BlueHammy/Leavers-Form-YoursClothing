#Requires -Version 7.0
<#
.SYNOPSIS
  Verify a published LeaversForm server is ready for live leaver processing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $WebPath,
    [Parameter(Mandatory = $true)]
    [string] $CertificateThumbprint,
    [string] $AppPoolIdentity = 'ApplicationPoolIdentity',
    [ValidateSet('LocalMachine', 'CurrentUser')]
    [string] $CertificateStore = 'LocalMachine'
)

$ErrorActionPreference = 'Stop'
$failures = 0

function Pass([string] $Message) { Write-Host "  [OK]   $Message" -ForegroundColor Green }
function Fail([string] $Message) { Write-Host "  [FAIL] $Message" -ForegroundColor Red; $script:failures++ }

Write-Host ''
Write-Host 'LeaversForm production readiness' -ForegroundColor Cyan

if (-not (Get-Command pwsh -ErrorAction SilentlyContinue)) { Fail 'pwsh.exe is not installed.' } else { Pass 'pwsh.exe found' }

foreach ($module in @('ActiveDirectory', 'ExchangeOnlineManagement', 'Microsoft.Graph.Authentication')) {
    if (Get-Module -ListAvailable -Name $module) { Pass "$module installed" } else { Fail "$module missing" }
}

$scriptPath = Join-Path $WebPath 'LeaverProcess.ps1'
if (Test-Path $scriptPath) { Pass "Script found: $scriptPath" } else { Fail "Script missing: $scriptPath" }

$logsPath = Join-Path $WebPath 'logs'
if (Test-Path $logsPath) { Pass "Log folder: $logsPath" } else { Fail "Log folder missing: $logsPath" }

$configPath = Join-Path $WebPath 'appsettings.Production.local.json'
if (Test-Path $configPath) { Pass "Production config: $configPath" } else { Fail 'appsettings.Production.local.json missing' }

$storePath = "Cert:\$CertificateStore\My"
$cert = Get-ChildItem $storePath -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -ieq ($CertificateThumbprint -replace '\s', '') -and $_.HasPrivateKey } |
    Select-Object -First 1
if ($cert) {
    Pass "Certificate $CertificateThumbprint found in $storePath"
}
else {
    Fail "Certificate $CertificateThumbprint not found in $storePath"
}

Write-Host ''
if ($failures -eq 0) {
    Write-Host 'Production readiness checks passed.' -ForegroundColor Green
    exit 0
}

Write-Host "$failures check(s) failed." -ForegroundColor Red
exit 1
