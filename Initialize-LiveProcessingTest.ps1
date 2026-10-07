#Requires -Version 7.0
<#
.SYNOPSIS
  One-time laptop setup for testing live leaver processing before Final-Build.

.DESCRIPTION
  1. Creates a PowerShell automation certificate on THIS PC (private key in CurrentUser\My)
  2. Exports the public .cer file to upload to Entra
  3. Writes step-by-step Entra portal instructions with your values filled in
  4. Saves appsettings.Development.local.json (ScriptTestMode off, cert + app ID)
  5. Copies LeaverProcess.ps1 to C:\Scripts
  6. Runs readiness checks and prints a -WhatIf dry-run command

  Does NOT replace Final-Build.ps1 for production IIS — use this to test safely first.

.EXAMPLE
  .\Initialize-LiveProcessingTest.ps1

.EXAMPLE
  .\Initialize-LiveProcessingTest.ps1 -InstallModules
#>
[CmdletBinding()]
param(
    [string] $LocalSettingsPath = (Join-Path $PSScriptRoot 'LeaversForm.Web\appsettings.Development.local.json'),
    [string] $OutputDir = (Join-Path $PSScriptRoot '.live-processing-test'),
    [string] $ScriptDestinationDir = (Join-Path $PSScriptRoot 'scripts'),
    [string] $LogFolderPath = (Join-Path $PSScriptRoot 'logs'),
    [string] $CertificateSubject = 'CN=LeaversForm PowerShell Automation (Test)',
    [string] $DefaultExchangeOrganization = 'yoursclothing.onmicrosoft.com',
    [string] $DefaultDomainController = 'sh-dc-01.yours.local',
    [switch] $InstallModules,
    [switch] $RecreateCertificate,
    [switch] $SkipEntraWait
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

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

function Get-OrCreate-AutomationCertificate {
    param(
        [string] $Subject,
        [switch] $ForceNew
    )

    if (-not $ForceNew) {
        $existing = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if ($existing) { return $existing }
    }
    else {
        Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $Subject } |
            ForEach-Object { Remove-Item "Cert:\CurrentUser\My\$($_.Thumbprint)" -Force }
    }

    return New-SelfSignedCertificate `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(2)
}

function Write-EntraSetupGuide {
    param(
        [string] $Path,
        [string] $TenantId,
        [string] $CerPath,
        [string] $Thumbprint,
        [string] $ExchangeOrg,
        [string] $DomainController
    )

    $portalBase = "https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps"
    $text = @"
LeaversForm — PowerShell automation Entra setup (TEST / laptop)
Generated: $(Get-Date -Format u)

Use a NEW app registration — NOT the Leavers Form web SSO app.

================================================================================
STEP 1 — Create the app registration
================================================================================
1. Open: $portalBase/ApplicationsListBlade
2. New registration
   Name:           LeaversForm PowerShell Automation (Test)
   Supported types: Accounts in this organizational directory only
   Redirect URI:   leave blank
3. Register
4. Copy the Application (client) ID — you will paste it into Initialize-LiveProcessingTest.ps1

================================================================================
STEP 2 — Upload this certificate
================================================================================
Certificates & secrets → Certificates → Upload certificate

Upload file (public key — for Entra only):
  $CerPath

Keep this file on this PC only (private key backup — do NOT upload to Entra):
  $(Split-Path $CerPath -Parent)\LeaversForm-PowerShell-Automation.pfx
  Password: LeaversForm-Automation-Test

If the certificate disappears from this PC, run:
  .\Repair-LiveProcessingCertificate.ps1

After upload, confirm the thumbprint matches:
  $Thumbprint

================================================================================
STEP 3 — API permissions (Application) — then Grant admin consent
================================================================================
Microsoft Graph:
  • User.ReadWrite.All
  • Group.ReadWrite.All
  • Directory.ReadWrite.All   (covers disable account, revoke sessions, licences)

Exchange Online (if shown under APIs my organization uses):
  • Exchange.ManageAsApp

If Exchange.ManageAsApp is not listed under Graph, add it via:
  APIs my organization uses → Exchange Online → Application permissions

Then: Grant admin consent for [your tenant]

================================================================================
STEP 4 — Exchange Online service principal (required for Connect-ExchangeOnline)
================================================================================
Run in Exchange Online PowerShell (as Exchange admin), substituting YOUR app client ID:

  Install-Module ExchangeOnlineManagement -Scope CurrentUser
  Connect-ExchangeOnline
  New-ServicePrincipal -AppId <PASTE_APPLICATION_CLIENT_ID_HERE> -ObjectId <OBJECT_ID_FROM_ENTRA_ENTERPRISE_APPS>

Or use the Entra enterprise app Object ID from:
  Entra → Enterprise applications → LeaversForm PowerShell Automation (Test)

Assign the app an Exchange admin role if your tenant requires it for automation:
  Entra → Roles and administrators → Exchange Administrator → Add assignments → select the enterprise app

(Microsoft docs vary by tenant — if Connect-ExchangeOnline fails, ask IT to register the service principal.)

================================================================================
STEP 5 — Values for this PC (saved automatically after you paste App ID)
================================================================================
Tenant ID:              $TenantId
Certificate thumbprint: $Thumbprint
Exchange organization:  $ExchangeOrg
Domain controller:      $DomainController

================================================================================
STEP 5b — On-prem AD permissions (easy to forget on a laptop!)
================================================================================
LeaverProcess.ps1 disables on-prem AD accounts using the Windows identity that
runs the app — NOT the Entra automation app.

  • Laptop testing: the account logged in when you run .\Start-LanTest.ps1 must
    be allowed to reset passwords and disable users in AD (delegated rights on
    the leaver OUs, or membership in an AD admin group). Running PowerShell
    "as Administrator" for cert setup is wrong — but your normal domain account
    still needs AD write access or disable steps fail with "Access is denied".

  • Production IIS: configure the app pool to run as a dedicated domain service
    account with those AD permissions — ApplicationPoolIdentity cannot touch AD.

================================================================================
STEP 6 — Safe dry run (after setup completes)
================================================================================
Replace TEST_USER with a real UPN/sam you are allowed to test against:

  pwsh -File C:\Scripts\LeaverProcess.ps1 `
    -UserPrincipalName TEST_USER@yoursclothing.co.uk `
    -SamAccountName TEST_SAM `
    -TenantId $TenantId `
    -AppId <YOUR_APP_CLIENT_ID> `
    -CertificateThumbprint $Thumbprint `
    -ExchangeOrganization $ExchangeOrg `
    -DomainController $DomainController `
    -WhatIf

-WhatIf shows what would happen without changing anything.

================================================================================
Production note
================================================================================
Final-Build.ps1 creates a separate cert in LocalMachine\My on the IIS server.
You can reuse THIS Entra app for testing, or create a production app later.
"@
    Set-Content -Path $Path -Value $text -Encoding UTF8
}

try {
    $repoRoot = $PSScriptRoot
    $sourceScript = Join-Path $repoRoot 'LeaverProcess.ps1'
    if (-not (Test-Path $sourceScript)) {
        throw "Could not find $sourceScript"
    }

    Write-Host ''
    Write-Host 'Leavers Form — initialize LIVE processing test (laptop)' -ForegroundColor Cyan
    Write-Host 'Creates cert + Entra checklist + local config. Does not run Final-Build.' -ForegroundColor DarkGray
    Write-Host "Running as: $([Security.Principal.WindowsIdentity]::GetCurrent().Name)" -ForegroundColor DarkGray
    if (Test-IsAdministrator) {
        Write-Host ''
        Write-Host 'WARNING: Running as Administrator — cert goes in the ADMIN profile, not your normal login.' -ForegroundColor Red
        Write-Host 'Use a normal PowerShell window, or run .\\Install-LiveProcessingCertificate.ps1 first.' -ForegroundColor Red
        Write-Host ''
    }
    Write-Host ''

    $settings = Get-ExistingSettings -Path $LocalSettingsPath
    if (-not $settings.Contains('AzureAd') -or [string]::IsNullOrWhiteSpace($settings.AzureAd.TenantId)) {
        throw @"
Azure AD web SSO is not configured.

Run .\Set-EntraSecrets.ps1 first (web login app), then run this script again.
"@
    }

    $tenantId = $settings.AzureAd.TenantId.ToString().Trim()

    if ($InstallModules) {
        Write-Host 'Installing PowerShell modules (CurrentUser)...' -ForegroundColor Cyan
        $modules = @(
            'ExchangeOnlineManagement',
            'Microsoft.Graph.Authentication',
            'Microsoft.Graph.Users',
            'Microsoft.Graph.Users.Actions',
            'Microsoft.Graph.Groups',
            'Microsoft.Graph.Identity.DirectoryManagement',
            'Microsoft.Graph.Applications'
        )
        foreach ($name in $modules) {
            Write-Host "  $name"
            Install-Module $name -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
        }
        Write-Host 'RSAT ActiveDirectory is a Windows optional feature — enable "RSAT: Active Directory Domain Services and Lightweight Directory Services Tools" in Settings → Optional features if AD commands fail.' -ForegroundColor Yellow
        Write-Host ''
    }

    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    New-Item -ItemType Directory -Path $ScriptDestinationDir -Force | Out-Null
    New-Item -ItemType Directory -Path $LogFolderPath -Force | Out-Null

    Write-Host 'Creating automation certificate (CurrentUser\My)...' -ForegroundColor Cyan
    $cert = Get-OrCreate-AutomationCertificate -Subject $CertificateSubject -ForceNew:$RecreateCertificate
    Start-Sleep -Milliseconds 500
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -ieq $cert.Thumbprint -and $_.HasPrivateKey } | Select-Object -First 1
    if (-not $cert) {
        throw @"
Certificate creation appeared to succeed but the cert is not visible in Cert:\CurrentUser\My.

Common causes:
  • This PowerShell window is running as Administrator (cert went to the admin profile)
  • Corporate policy blocked self-signed certificates

Try:
  1. Close Admin PowerShell
  2. Open a normal PowerShell window
  3. Run: .\Install-LiveProcessingCertificate.ps1 -Force
"@
    }
    Write-Host "  Subject:     $($cert.Subject)" -ForegroundColor Green
    Write-Host "  Thumbprint:  $($cert.Thumbprint)" -ForegroundColor Green
    Write-Host "  Expires:     $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor Green

    $cerPath = Join-Path $OutputDir 'LeaversForm-PowerShell-Automation.cer'
    $pfxPath = Join-Path $OutputDir 'LeaversForm-PowerShell-Automation.pfx'
    $pfxPassword = 'LeaversForm-Automation-Test'

    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
    Write-Host "  Entra upload (.cer): $cerPath" -ForegroundColor Green

    $securePfxPassword = ConvertTo-SecureString $pfxPassword -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePfxPassword -Force | Out-Null
    Write-Host "  Private key backup (.pfx): $pfxPath" -ForegroundColor Green
    Write-Host ''
    Write-Host '  .cer → upload to Entra (public key only)' -ForegroundColor Yellow
    Write-Host '  .pfx → keep on this PC only (contains private key — never commit or email)' -ForegroundColor Yellow
    Write-Host "  PFX password: $pfxPassword" -ForegroundColor Yellow
    Write-Host '  If the store cert is lost later, run: .\Repair-LiveProcessingCertificate.ps1' -ForegroundColor Yellow

    $exchangeOrg = Read-Host "Exchange Online organization [$DefaultExchangeOrganization]"
    if ([string]::IsNullOrWhiteSpace($exchangeOrg)) { $exchangeOrg = $DefaultExchangeOrganization }

    $domainController = Read-Host "Domain controller [$DefaultDomainController]"
    if ([string]::IsNullOrWhiteSpace($domainController)) { $domainController = $DefaultDomainController }

    $guidePath = Join-Path $OutputDir 'ENTRA-SETUP.txt'
    Write-EntraSetupGuide `
        -Path $guidePath `
        -TenantId $tenantId `
        -CerPath $cerPath `
        -Thumbprint $cert.Thumbprint `
        -ExchangeOrg $exchangeOrg.Trim() `
        -DomainController $domainController.Trim()

    Write-Host ''
    Write-Host 'Entra setup guide written:' -ForegroundColor Cyan
    Write-Host "  $guidePath" -ForegroundColor Green
    Write-Host ''
    Write-Host 'Do these in the Entra portal BEFORE continuing:' -ForegroundColor Yellow
    Write-Host '  1. Create app "LeaversForm PowerShell Automation (Test)"'
    Write-Host "  2. Upload certificate: $cerPath"
    Write-Host '  3. Add API permissions + Grant admin consent (see guide)'
    Write-Host '  4. Register Exchange service principal if needed (see guide)'
    Write-Host ''
    Write-Host 'Make sure you also have this (see STEP 5b in the guide):' -ForegroundColor Yellow
    Write-Host '  • Your Windows login (or IIS service account on the server) can reset/disable AD accounts'
    Write-Host '  • Entra app alone is NOT enough for on-prem AD — that uses your Windows identity'
    Write-Host ''

    try {
        Start-Process notepad.exe -ArgumentList $guidePath
        Start-Process explorer.exe -ArgumentList "/select,`"$cerPath`""
    }
    catch {
        Write-Warning "Could not open guide automatically: $($_.Exception.Message)"
    }

    if (-not $SkipEntraWait) {
        Write-Host 'Complete the Entra steps above, then continue here.' -ForegroundColor Cyan
        Read-Host 'Press Enter when the certificate is uploaded and admin consent is granted'
    }

    Write-Host ''
    $psAppId = Read-Host 'Paste Application (client) ID from the NEW automation app'
    if ([string]::IsNullOrWhiteSpace($psAppId)) {
        throw 'Application (client) ID is required.'
    }

    $destScript = Join-Path $ScriptDestinationDir 'LeaverProcess.ps1'
    Copy-Item -Path $sourceScript -Destination $destScript -Force
    Write-Host "Copied LeaverProcess.ps1 → $destScript" -ForegroundColor Green

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
    Write-Host "Saved live settings → $LocalSettingsPath" -ForegroundColor Green

    $summaryPath = Join-Path $OutputDir 'YOUR-VALUES.txt'
    @"
LeaversForm live test — saved values
Generated: $(Get-Date -Format u)

Tenant ID:              $tenantId
App ID (automation):    $($psAppId.Trim())
Certificate thumbprint: $($cert.Thumbprint)
Certificate (.cer for Entra): $cerPath
Certificate (.pfx for this PC): $pfxPath
PFX password:           $pfxPassword
Script:                 $destScript
Exchange organization:  $($exchangeOrg.Trim())
Domain controller:      $($domainController.Trim())
Log folder:             $LogFolderPath

ScriptTestMode:         false (LIVE — accounts will be disabled)

Restart the app:
  Stop Start-LanTest.ps1 (Ctrl+C), then .\Start-LanTest.ps1

Dry run (-WhatIf):
  pwsh -File `"$destScript`" `
    -UserPrincipalName TEST_USER@yoursclothing.co.uk `
    -SamAccountName TEST_SAM `
    -TenantId $tenantId `
    -AppId $($psAppId.Trim()) `
    -CertificateThumbprint $($cert.Thumbprint) `
    -ExchangeOrganization $($exchangeOrg.Trim()) `
    -DomainController $($domainController.Trim()) `
    -WhatIf
"@ | Set-Content -Path $summaryPath -Encoding UTF8

    Write-Host ''
    Write-Host 'Summary saved:' -ForegroundColor Cyan
    Write-Host "  $summaryPath" -ForegroundColor Green

    Write-Host ''
    Write-Host 'Running readiness checks...' -ForegroundColor Cyan
    & (Join-Path $repoRoot 'Test-LiveProcessingReadiness.ps1') -LocalSettingsPath $LocalSettingsPath -NoPause

    Write-Host ''
    Write-Host 'Next steps:' -ForegroundColor Cyan
    Write-Host '  1. Confirm AD permissions (guide STEP 5b) — without this, Graph works but AD fails'
    Write-Host '  2. Run the -WhatIf command in YOUR-VALUES.txt against a safe test account'
    Write-Host '  3. Restart .\Start-LanTest.ps1 — confirm "ScriptTestMode: OFF" banner'
    Write-Host '  4. Submit leaver with Disable ASAP for immediate processing'
    Write-Host ''
    Write-Host 'When happy, use Final-Build.ps1 on the IIS server for production.' -ForegroundColor DarkGray

    Wait-BeforeClose
}
catch {
    Write-Host ''
    Write-Host 'ERROR' -ForegroundColor Red
    Write-Host $_.Exception.Message
    if ($_.ScriptStackTrace) {
        Write-Host ''
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    }
    Wait-BeforeClose
    exit 1
}
