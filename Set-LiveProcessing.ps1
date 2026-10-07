#Requires -Version 7.0
<#
.SYNOPSIS
  Enable real leaver processing on this machine (disables ScriptTestMode).

.DESCRIPTION
  Merges live PowerShell settings into appsettings.Development.local.json, copies
  LeaverProcess.ps1 to C:\Scripts, and runs readiness checks.

  Run AFTER Set-EntraSecrets.ps1. Uses a separate Entra app registration for
  PowerShell automation (certificate auth) — not the same as the web login app.

.EXAMPLE
  .\Set-LiveProcessing.ps1
#>
[CmdletBinding()]
param(
    [string] $LocalSettingsPath = (Join-Path $PSScriptRoot 'LeaversForm.Web\appsettings.Development.local.json'),
    [string] $ScriptDestinationDir = (Join-Path $PSScriptRoot 'scripts'),
    [string] $LogFolderPath = (Join-Path $PSScriptRoot 'logs'),
    [switch] $SkipReadinessTest,
    [switch] $WhatIfOnly
)

$ErrorActionPreference = 'Stop'

function Wait-BeforeClose {
    Write-Host ''
    Read-Host 'Press Enter to close'
}

function Get-ExistingSettings {
    param([string] $Path)
    $settings = [ordered]@{}
    if (Test-Path $Path) {
        $existing = Get-Content $Path -Raw | ConvertFrom-Json
        foreach ($prop in $existing.PSObject.Properties) {
            $settings[$prop.Name] = $prop.Value
        }
    }
    return $settings
}

function Find-AutomationCertificate {
    param([string] $SubjectPattern = 'LeaversForm PowerShell Automation')
    $stores = @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My')
    foreach ($store in $stores) {
        if (-not (Test-Path $store)) { continue }
        $match = Get-ChildItem $store -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -match [regex]::Escape($SubjectPattern) -or $_.Subject -match 'LeaversForm' } |
            Where-Object { $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if ($match) { return $match }
    }
    return $null
}

try {
    $repoRoot = $PSScriptRoot
    $sourceScript = Join-Path $repoRoot 'LeaverProcess.ps1'
    if (-not (Test-Path $sourceScript)) {
        throw "Could not find $sourceScript"
    }

    Write-Host ''
    Write-Host 'Leavers Form — enable LIVE processing' -ForegroundColor Cyan
    Write-Host 'This turns OFF ScriptTestMode. Submitting a leaver (or Disable ASAP) will run LeaverProcess.ps1 and disable the account.'
    Write-Host ''
    Write-Host 'You need:' -ForegroundColor Yellow
    Write-Host '  • PowerShell automation Entra app (certificate auth) — separate from the web login app'
    Write-Host '  • Automation certificate installed locally with private key'
    Write-Host '  • RSAT Active Directory module and ExchangeOnlineManagement / Microsoft.Graph modules'
    Write-Host '  • Network access to your domain controller and Exchange Online'
    Write-Host '  • AD rights for the Windows account running the app (reset password + disable user) — see Initialize-LiveProcessingTest guide STEP 5b'
    Write-Host ''

    if ($WhatIfOnly) {
        Write-Host 'WhatIfOnly: no settings will be saved.' -ForegroundColor DarkGray
    }

    $settings = Get-ExistingSettings -Path $LocalSettingsPath
    if (-not $settings.Contains('AzureAd') -or [string]::IsNullOrWhiteSpace($settings.AzureAd.TenantId)) {
        throw @"
Azure AD is not configured in $LocalSettingsPath.

Run .\Set-EntraSecrets.ps1 first, then run this script again.
"@
    }

    $tenantId = $settings.AzureAd.TenantId.ToString().Trim()

    $existingLeavers = $null
    if ($settings.Contains('LeaversForm')) {
        $existingLeavers = $settings.LeaversForm
    }

    $defaultExchange = if ($existingLeavers?.PowerShellExchangeOrganization) { $existingLeavers.PowerShellExchangeOrganization } else { 'yoursclothing.onmicrosoft.com' }
    $defaultDc = if ($existingLeavers?.PowerShellDomainController) { $existingLeavers.PowerShellDomainController } else { 'sh-dc-01.yours.local' }

    Write-Host "Tenant ID (from Azure AD config): $tenantId" -ForegroundColor DarkGray
    Write-Host ''
    Write-Host 'PowerShell automation app — from Entra → App registrations → your AUTOMATION app (not the web SSO app):'
    Write-Host ''

    $psAppId = Read-Host 'Application (client) ID for PowerShell automation'
    if ([string]::IsNullOrWhiteSpace($psAppId)) {
        throw 'PowerShell automation App ID is required.'
    }

    $cert = Find-AutomationCertificate
    if ($cert) {
        Write-Host ''
        Write-Host "Found certificate: $($cert.Subject)" -ForegroundColor Green
        Write-Host "  Thumbprint: $($cert.Thumbprint)"
        Write-Host "  Store:      $($cert.PSParentPath)"
        $useFound = Read-Host 'Use this certificate? [Y/n]'
        if ($useFound -match '^[Nn]') { $cert = $null }
    }

    if (-not $cert) {
        $thumbprint = (Read-Host 'Certificate thumbprint (from Cert:\CurrentUser\My or Cert:\LocalMachine\My)').Trim()
        if ([string]::IsNullOrWhiteSpace($thumbprint)) {
            throw 'Certificate thumbprint is required.'
        }
        $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object Thumbprint -eq $thumbprint |
            Select-Object -First 1
        if (-not $cert) {
            throw "No certificate with thumbprint $thumbprint was found in CurrentUser\My or LocalMachine\My."
        }
    }

    $exchangeOrg = Read-Host "Exchange Online organization [$defaultExchange]"
    if ([string]::IsNullOrWhiteSpace($exchangeOrg)) { $exchangeOrg = $defaultExchange }

    $domainController = Read-Host "Domain controller [$defaultDc]"
    if ([string]::IsNullOrWhiteSpace($domainController)) { $domainController = $defaultDc }

    if (-not $WhatIfOnly) {
        New-Item -ItemType Directory -Path $ScriptDestinationDir -Force | Out-Null
        New-Item -ItemType Directory -Path $LogFolderPath -Force | Out-Null

        $destScript = Join-Path $ScriptDestinationDir 'LeaverProcess.ps1'
        Copy-Item -Path $sourceScript -Destination $destScript -Force
        Write-Host ''
        Write-Host "Copied LeaverProcess.ps1 to $destScript" -ForegroundColor Green

        $settings['LeaversForm'] = [ordered]@{
            ScriptTestMode = $false
            PowerShellScriptPath = $destScript
            PowerShellWorkingDirectory = $ScriptDestinationDir
            PowerShellExecutable = 'pwsh.exe'
            PowerShellTenantId = $tenantId
            PowerShellAppId = $psAppId.Trim()
            PowerShellCertificateThumbprint = $cert.Thumbprint
            PowerShellExchangeOrganization = $exchangeOrg.Trim()
            PowerShellDomainController = $domainController.Trim()
            LogFolderPath = $LogFolderPath
        }

        ($settings | ConvertTo-Json -Depth 6) | Set-Content -Path $LocalSettingsPath -Encoding UTF8
        Write-Host "Saved live processing settings to $LocalSettingsPath" -ForegroundColor Green
        Write-Host ''
        Write-Host 'ScriptTestMode is now FALSE.' -ForegroundColor Yellow
        Write-Host 'Restart the app (Stop Start-LanTest.ps1 with Ctrl+C, then run it again).' -ForegroundColor Yellow
    }

    if (-not $SkipReadinessTest) {
        Write-Host ''
        Write-Host 'Running readiness checks...' -ForegroundColor Cyan
        & (Join-Path $repoRoot 'Test-LiveProcessingReadiness.ps1') -LocalSettingsPath $LocalSettingsPath -NoPause
    }

    Write-Host ''
    Write-Host 'Before submitting the real leaver:' -ForegroundColor Cyan
    Write-Host '  • Tick "Disable ASAP" on the form if the account must be disabled immediately'
    Write-Host '  • Otherwise processing runs at 17:00 UK time on the leaving date (checked every 5 minutes)'
    Write-Host '  • Confirm samAccountName is populated (select the user from search — do not type manually)'
    Write-Host '  • Optional dry run: pwsh -File C:\Scripts\LeaverProcess.ps1 -UserPrincipalName user@domain -WhatIf ...'
}
catch {
    Write-Host ''
    Write-Host 'ERROR' -ForegroundColor Red
    Write-Host $_.Exception.Message
    Wait-BeforeClose
    exit 1
}
