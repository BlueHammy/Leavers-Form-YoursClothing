#Requires -Version 7.0
<#
.SYNOPSIS
  Create (or recreate) the PowerShell automation certificate and prove it is in the store.

  Run in a NORMAL (non-Administrator) PowerShell window so the cert lands in YOUR profile.
  Administrator windows use a different Cert:\CurrentUser\My — the app will not see it.

.EXAMPLE
  .\Install-LiveProcessingCertificate.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDir = (Join-Path $PSScriptRoot '.live-processing-test'),
    [string] $LocalSettingsPath = (Join-Path $PSScriptRoot 'LeaversForm.Web\appsettings.Development.local.json'),
    [string] $Subject = 'CN=LeaversForm PowerShell Automation (Test)',
    [string] $PfxPassword = 'LeaversForm-Automation-Test',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

function Wait-BeforeClose {
    Write-Host ''
    Read-Host 'Press Enter to close'
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-CertFromStore {
    param([string] $Thumbprint)
    foreach ($storePath in @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My')) {
        if (-not (Test-Path $storePath)) { continue }
        $match = Get-ChildItem $storePath -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -ieq $Thumbprint -and $_.HasPrivateKey } |
            Select-Object -First 1
        if ($match) { return $match }
    }
    return $null
}

try {
    Write-Host ''
    Write-Host 'Install LeaversForm automation certificate' -ForegroundColor Cyan

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    Write-Host "Running as: $($identity.Name)" -ForegroundColor DarkGray

    if (Test-IsAdministrator) {
        Write-Host ''
        Write-Host 'WARNING: You are running as Administrator.' -ForegroundColor Red
        Write-Host 'The cert will be stored in the ADMIN profile, not your normal login.'
        Write-Host 'Close this window and run again from a normal (non-Admin) PowerShell.' -ForegroundColor Red
        Write-Host ''
        $continue = Read-Host 'Continue anyway? [y/N]'
        if ($continue -notmatch '^[Yy]') { exit 1 }
    }

    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

    if (-not $Force) {
        $existing = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if ($existing) {
            Write-Host ''
            Write-Host 'Certificate already in Cert:\CurrentUser\My:' -ForegroundColor Green
            Write-Host "  Thumbprint: $($existing.Thumbprint)"
            Write-Host 'Use -Force to create a new one.' -ForegroundColor Yellow
            Wait-BeforeClose
            exit 0
        }
    }
    else {
        Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -eq $Subject } |
            ForEach-Object {
                Write-Host "Removing old certificate: $($_.Thumbprint)" -ForegroundColor DarkYellow
                Remove-Item "Cert:\CurrentUser\My\$($_.Thumbprint)" -Force
            }
    }

    Write-Host ''
    Write-Host 'Creating self-signed certificate in Cert:\CurrentUser\My ...' -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(2)

    Start-Sleep -Milliseconds 500

    $verified = Get-CertFromStore -Thumbprint $cert.Thumbprint
    if (-not $verified) {
        throw "Certificate was created but is not visible in Cert:\CurrentUser\My. Try logging out/in or check corporate certificate policy."
    }

    $cerPath = Join-Path $OutputDir 'LeaversForm-PowerShell-Automation.cer'
    $pfxPath = Join-Path $OutputDir 'LeaversForm-PowerShell-Automation.pfx'
    Export-Certificate -Cert $verified -FilePath $cerPath -Force | Out-Null
    Export-PfxCertificate -Cert $verified -FilePath $pfxPath -Password (ConvertTo-SecureString $PfxPassword -AsPlainText -Force) -Force | Out-Null

    Write-Host ''
    Write-Host 'SUCCESS' -ForegroundColor Green
    Write-Host "  Store:      Cert:\CurrentUser\My"
    Write-Host "  Subject:    $($verified.Subject)"
    Write-Host "  Thumbprint: $($verified.Thumbprint)"
    Write-Host "  Expires:    $($verified.NotAfter.ToString('yyyy-MM-dd'))"
    Write-Host "  Entra .cer: $cerPath"
    Write-Host "  Backup .pfx: $pfxPath (password: $PfxPassword)"

    if (Test-Path $LocalSettingsPath) {
        $settings = Get-Content $LocalSettingsPath -Raw | ConvertFrom-Json
        if ($settings.LeaversForm) {
            $settings.LeaversForm.PowerShellCertificateThumbprint = $verified.Thumbprint
            ($settings | ConvertTo-Json -Depth 6) | Set-Content -Path $LocalSettingsPath -Encoding UTF8
            Write-Host ''
            Write-Host "Updated thumbprint in $LocalSettingsPath" -ForegroundColor Green
        }
    }

    Write-Host ''
    Write-Host 'NEXT:' -ForegroundColor Cyan
    Write-Host '  1. Upload the NEW .cer to Entra (Certificates on your automation app)'
    Write-Host '  2. Confirm thumbprint in Entra matches above'
    Write-Host '  3. Restart the Leavers Form app'
    Write-Host ''
    Write-Host 'Verify with:'
    Write-Host "  Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq '$($verified.Thumbprint)'"

    Wait-BeforeClose
}
catch {
    Write-Host ''
    Write-Host 'ERROR' -ForegroundColor Red
    Write-Host $_.Exception.Message
    Wait-BeforeClose
    exit 1
}
