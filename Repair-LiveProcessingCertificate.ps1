#Requires -Version 7.0
<#
.SYNOPSIS
  Restore the PowerShell automation certificate private key on this PC.

.DESCRIPTION
  The .cer file in .live-processing-test is PUBLIC ONLY (for Entra upload).
  Authentication requires the matching PRIVATE KEY in Cert:\CurrentUser\My.

  This script:
  1. Imports .live-processing-test\LeaversForm-PowerShell-Automation.pfx if present
  2. Or finds an existing LeaversForm cert already in the store
  3. Updates appsettings.Development.local.json with the correct thumbprint

  If neither .pfx nor a store cert exists, re-run Initialize-LiveProcessingTest.ps1
  with -RecreateCertificate and upload the new .cer to Entra again.

.EXAMPLE
  .\Repair-LiveProcessingCertificate.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDir = (Join-Path $PSScriptRoot '.live-processing-test'),
    [string] $LocalSettingsPath = (Join-Path $PSScriptRoot 'LeaversForm.Web\appsettings.Development.local.json'),
    [string] $PfxPath = (Join-Path $PSScriptRoot '.live-processing-test\LeaversForm-PowerShell-Automation.pfx'),
    [string] $PfxPassword = 'LeaversForm-Automation-Test',
    [string] $CertificateSubject = 'LeaversForm PowerShell Automation'
)

$ErrorActionPreference = 'Stop'

function Wait-BeforeClose {
    Write-Host ''
    Read-Host 'Press Enter to close'
}

function Get-SettingsHashtable {
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

function Find-StoreCertificate {
    param([string] $Thumbprint, [string] $SubjectPattern)

    foreach ($storePath in @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My')) {
        if (-not (Test-Path $storePath)) { continue }
        $certs = Get-ChildItem $storePath -ErrorAction SilentlyContinue |
            Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) }

        if ($Thumbprint) {
            $match = $certs | Where-Object { $_.Thumbprint -ieq ($Thumbprint -replace '\s', '') } | Select-Object -First 1
            if ($match) { return $match }
        }

        $match = $certs |
            Where-Object { $_.Subject -match [regex]::Escape($SubjectPattern) -or $_.Subject -match 'LeaversForm' } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if ($match) { return $match }
    }

    return $null
}

function Update-ThumbprintInSettings {
    param([string] $Path, [string] $Thumbprint)

    if (-not (Test-Path $Path)) {
        throw "Settings file not found: $Path"
    }

    $settings = Get-Content $Path -Raw | ConvertFrom-Json
    if (-not $settings.LeaversForm) {
        throw "No LeaversForm section in $Path — run .\\Initialize-LiveProcessingTest.ps1 first."
    }

    $settings.LeaversForm.PowerShellCertificateThumbprint = $Thumbprint
    ($settings | ConvertTo-Json -Depth 6) | Set-Content -Path $Path -Encoding UTF8
}

try {
    Write-Host ''
    Write-Host 'Leavers Form — repair automation certificate' -ForegroundColor Cyan
    Write-Host ''
    Write-Host 'IMPORTANT:' -ForegroundColor Yellow
    Write-Host '  .cer  = public certificate for Entra upload ONLY (cannot sign in)'
    Write-Host '  .pfx  = private key backup for THIS PC (required to run the script)'
    Write-Host '  Store = Cert:\CurrentUser\My (where the app looks at runtime)'
    Write-Host ''

    $configuredThumbprint = $null
    if (Test-Path $LocalSettingsPath) {
        $json = Get-Content $LocalSettingsPath -Raw | ConvertFrom-Json
        $configuredThumbprint = $json.LeaversForm.PowerShellCertificateThumbprint
        if ($configuredThumbprint) {
            Write-Host "Configured thumbprint: $configuredThumbprint" -ForegroundColor DarkGray
        }
    }

    $cerPath = Join-Path $OutputDir 'LeaversForm-PowerShell-Automation.cer'
    if (Test-Path $cerPath) {
        Write-Host "Found Entra upload file: $cerPath (this alone is not enough on this PC)" -ForegroundColor DarkGray
    }

    $cert = Find-StoreCertificate -Thumbprint $configuredThumbprint -SubjectPattern $CertificateSubject
    if ($cert) {
        Write-Host ''
        Write-Host 'Certificate with private key already in store:' -ForegroundColor Green
        Write-Host "  Subject:    $($cert.Subject)"
        Write-Host "  Thumbprint: $($cert.Thumbprint)"
        Write-Host "  Store:      $($cert.PSParentPath)"
        Update-ThumbprintInSettings -Path $LocalSettingsPath -Thumbprint $cert.Thumbprint
        Write-Host ''
        Write-Host "Updated thumbprint in $LocalSettingsPath" -ForegroundColor Green
        Wait-BeforeClose
        exit 0
    }

    if (Test-Path $PfxPath) {
        Write-Host ''
        Write-Host "Importing private key from $PfxPath ..." -ForegroundColor Cyan
        $secure = ConvertTo-SecureString $PfxPassword -AsPlainText -Force
        $cert = Import-PfxCertificate `
            -FilePath $PfxPath `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -Password $secure `
            -Exportable

        Write-Host 'Imported certificate:' -ForegroundColor Green
        Write-Host "  Subject:    $($cert.Subject)"
        Write-Host "  Thumbprint: $($cert.Thumbprint)"

        Update-ThumbprintInSettings -Path $LocalSettingsPath -Thumbprint $cert.Thumbprint
        Write-Host ''
        Write-Host "Updated thumbprint in $LocalSettingsPath" -ForegroundColor Green
        Write-Host 'Restart the app, then retry the leaver.' -ForegroundColor Yellow
        Wait-BeforeClose
        exit 0
    }

    throw @"
No certificate with a private key was found on this PC.

The .cer file cannot be imported for authentication — it has no private key.

Fix options:

  A) If you previously ran Initialize-LiveProcessingTest.ps1 on this PC but lost the store cert:
     Re-run (creates a NEW cert — upload the new .cer to Entra again):
       .\Initialize-LiveProcessingTest.ps1 -RecreateCertificate

  B) If you have a .pfx backup from Initialize (created after this update):
       .\Repair-LiveProcessingCertificate.ps1

  C) Check whether the cert is already installed under a different thumbprint:
       Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -match 'LeaversForm' | Format-List Subject, Thumbprint, NotAfter, HasPrivateKey
"@
}
catch {
    Write-Host ''
    Write-Host 'ERROR' -ForegroundColor Red
    Write-Host $_.Exception.Message
    Wait-BeforeClose
    exit 1
}
