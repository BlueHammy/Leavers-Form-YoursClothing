#Requires -Version 7.0
<#
.SYNOPSIS
  Preflight checks before enabling or using live leaver processing.

.EXAMPLE
  .\Test-LiveProcessingReadiness.ps1
#>
[CmdletBinding()]
param(
    [string] $LocalSettingsPath = (Join-Path $PSScriptRoot 'LeaversForm.Web\appsettings.Development.local.json'),
    [switch] $NoPause
)

$ErrorActionPreference = 'Continue'
$failures = 0
$warnings = 0

function Wait-BeforeClose {
    if ($NoPause) { return }
    Write-Host ''
    Read-Host 'Press Enter to close'
}

function Write-Pass([string] $Message) { Write-Host "  [OK]   $Message" -ForegroundColor Green }
function Write-Fail([string] $Message) { Write-Host "  [FAIL] $Message" -ForegroundColor Red; $script:failures++ }
function Write-Warn([string] $Message) { Write-Host "  [WARN] $Message" -ForegroundColor Yellow; $script:warnings++ }

Write-Host ''
Write-Host 'Live processing readiness' -ForegroundColor Cyan
Write-Host '-------------------------'

if (-not (Get-Command pwsh -ErrorAction SilentlyContinue)) {
    Write-Fail 'PowerShell 7 (pwsh.exe) is not on PATH. Install PowerShell 7.'
}
else {
    Write-Pass "pwsh found: $((Get-Command pwsh).Source)"
}

$requiredModules = @(
    'ActiveDirectory',
    'ExchangeOnlineManagement',
    'Microsoft.Graph.Authentication'
)
foreach ($name in $requiredModules) {
    $mod = Get-Module -ListAvailable -Name $name | Sort-Object Version -Descending | Select-Object -First 1
    if ($mod) {
        Write-Pass "$name $($mod.Version)"
    }
    else {
        Write-Fail "$name is not installed. Install RSAT / ExchangeOnlineManagement / Microsoft.Graph from PSGallery."
    }
}

if (-not (Test-Path $LocalSettingsPath)) {
    Write-Fail "Settings file not found: $LocalSettingsPath (run Set-LiveProcessing.ps1)"
}
else {
    Write-Pass "Settings file exists: $LocalSettingsPath"
    try {
        $json = Get-Content $LocalSettingsPath -Raw | ConvertFrom-Json
        $lf = $json.LeaversForm
        if (-not $lf) {
            Write-Fail 'LeaversForm section missing — run Set-LiveProcessing.ps1'
        }
        else {
            if ($lf.ScriptTestMode -eq $true) {
                Write-Fail 'ScriptTestMode is still true — run Set-LiveProcessing.ps1'
            }
            else {
                Write-Pass 'ScriptTestMode is false (live processing enabled)'
            }

            foreach ($key in @(
                    'PowerShellScriptPath',
                    'PowerShellAppId',
                    'PowerShellCertificateThumbprint',
                    'PowerShellExchangeOrganization',
                    'PowerShellTenantId'
                )) {
                if ([string]::IsNullOrWhiteSpace($lf.$key)) {
                    Write-Fail "LeaversForm:$key is empty"
                }
            }

            if ($lf.PowerShellScriptPath -and -not (Test-Path $lf.PowerShellScriptPath)) {
                Write-Fail "Script not found: $($lf.PowerShellScriptPath)"
            }
            elseif ($lf.PowerShellScriptPath) {
                Write-Pass "Script found: $($lf.PowerShellScriptPath)"
            }

            if ($lf.LogFolderPath) {
                if (Test-Path $lf.LogFolderPath) {
                    Write-Pass "Log folder: $($lf.LogFolderPath)"
                }
                else {
                    try {
                        New-Item -ItemType Directory -Path $lf.LogFolderPath -Force | Out-Null
                        Write-Pass "Created log folder: $($lf.LogFolderPath)"
                    }
                    catch {
                        Write-Fail "Cannot create log folder $($lf.LogFolderPath): $($_.Exception.Message)"
                    }
                }
            }

            if ($lf.PowerShellCertificateThumbprint) {
                $tp = $lf.PowerShellCertificateThumbprint
                $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
                    Where-Object Thumbprint -eq $tp |
                    Select-Object -First 1
                if ($cert) {
                    if ($cert.NotAfter -lt (Get-Date)) {
                        Write-Fail "Certificate expired: $tp"
                    }
                    elseif (-not $cert.HasPrivateKey) {
                        Write-Fail "Certificate has no private key: $tp"
                    }
                    else {
                        Write-Pass "Certificate $tp valid until $($cert.NotAfter.ToString('yyyy-MM-dd'))"
                    }
                }
                else {
                    Write-Fail "Certificate thumbprint not found locally: $tp"
                }
            }

            if ($lf.PowerShellDomainController) {
                $dc = $lf.PowerShellDomainController
                if (Test-Connection -ComputerName $dc -Count 1 -Quiet -ErrorAction SilentlyContinue) {
                    Write-Pass "Domain controller reachable: $dc"
                }
                else {
                    Write-Warn "Could not ping $dc — AD steps may fail if you are off VPN/LAN"
                }
            }

            if ($lf.ScriptTestMode -eq $false) {
                $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
                Write-Warn "Live mode: on-prem AD disable runs as $identity — that account needs delegated rights to reset/disable users (not just the Entra app). See Initialize-LiveProcessingTest STEP 5b."
            }
        }
    }
    catch {
        Write-Fail "Could not parse $LocalSettingsPath : $($_.Exception.Message)"
    }
}

Write-Host ''
if ($failures -eq 0) {
    Write-Host "Ready for live processing ($warnings warning(s))." -ForegroundColor Green
    Wait-BeforeClose
    exit 0
}
else {
    Write-Host "$failures check(s) failed, $warnings warning(s). Fix failures before submitting a real leaver." -ForegroundColor Red
    Wait-BeforeClose
    exit 1
}
