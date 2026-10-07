<#

.SYNOPSIS

    Builds and prepares a live LeaversForm installation on an internal server.



.DESCRIPTION

    Run this script from an elevated PowerShell 7 window on the target server.

    It publishes the Web and Runner applications, installs the PowerShell modules

    used by LeaverProcess.ps1, creates an automation certificate (with .cer + .pfx

    backup), grants the IIS app pool read access to the private key, configures IIS,

    runs readiness checks, and writes a deployment checklist.



    The only manual Entra step after this script is uploading the generated .cer

    file to the PowerShell automation app registration and granting admin consent.



.EXAMPLE

    .\Final-Build.ps1 `

      -HostName "leavers.internal.yoursclothing.co.uk" `

      -PhysicalPath "C:\inetpub\LeaversForm\Web" `

      -AppPoolIdentity "YOURDOMAIN\svc-leaversform" `

      -SqlConnectionString "Server=SQL01;Database=LeaversForm;Trusted_Connection=True;Encrypt=True" `

      -AzureTenantId "00000000-0000-0000-0000-000000000000" `

      -AzureClientId "00000000-0000-0000-0000-000000000000" `

      -PowerShellAppId "00000000-0000-0000-0000-000000000000" `

      -AzureClientSecret (Read-Host "Azure client secret" -AsSecureString) `

      -PowerShellDomainController "dc01.yourdomain.local" `

      -EnableLiveMode



.NOTES

    Uses a dedicated PowerShell automation Entra app (PowerShellAppId) — not the web SSO app.

    Protect the generated .pfx backup; it contains the certificate private key.

#>



#Requires -RunAsAdministrator

#Requires -Version 7.0



[CmdletBinding()]

param(

    [string] $OutputPath = (Join-Path $PSScriptRoot "final-publish"),

    [string] $SiteName = "LeaversForm",

    [Parameter(Mandatory = $true)]

    [string] $HostName,

    [string] $PhysicalPath = "C:\inetpub\LeaversForm\Web",

    [string] $AppPoolIdentity = "ApplicationPoolIdentity",

    [string] $HttpsCertificateThumbprint,

    [string] $SqlConnectionString,

    [Parameter(Mandatory = $true)]

    [guid] $AzureTenantId,

    [Parameter(Mandatory = $true)]

    [guid] $AzureClientId,

    [SecureString] $AzureClientSecret,

    [guid] $PowerShellAppId,

    [string] $PowerShellCertificateSubject = "CN=LeaversForm PowerShell Automation",

    [string] $PowerShellExchangeOrganization = "yoursclothing.onmicrosoft.com",

    [string] $PowerShellDomainController,

    [string] $CertificateBackupPassword = "LeaversForm-Cert-Backup",

    [switch] $SkipModuleInstall,

    [switch] $RecreateCertificate,

    [switch] $EnableLiveMode,

    [switch] $SkipIis,

    [switch] $SkipDatabase

)



$ErrorActionPreference = "Stop"

Set-StrictMode -Version Latest



function Write-Step([string] $Message) {

    Write-Host "`n==> $Message" -ForegroundColor Cyan

}



function Require-Value([string] $Name, [string] $Value) {

    if ([string]::IsNullOrWhiteSpace($Value)) {

        throw "$Name is required."

    }

}



if (-not $EnableLiveMode) {

    throw "This is the live build script. Re-run with -EnableLiveMode after reviewing the checklist."

}



if (-not $AzureClientSecret) {

    $AzureClientSecret = Read-Host "Enter the LeaversForm web Entra client secret" -AsSecureString

}



if (-not $PowerShellAppId) {

    $psAppInput = Read-Host "Enter the PowerShell automation Entra app (client) ID (NOT the web SSO app)"

    if ([string]::IsNullOrWhiteSpace($psAppInput)) {

        throw "PowerShellAppId is required for live leaver processing."

    }

    $PowerShellAppId = [guid]$psAppInput

}



Require-Value "SqlConnectionString" $SqlConnectionString

Require-Value "PowerShellDomainController" $PowerShellDomainController



if ($AppPoolIdentity -eq "ApplicationPoolIdentity") {

    Write-Warning "ApplicationPoolIdentity cannot normally access on-prem AD. Use a dedicated domain service account for live processing."

}



$root = (Resolve-Path $PSScriptRoot).Path

$publishScript = Join-Path $root "deploy\publish-production.ps1"

$grantCertScript = Join-Path $root "deploy\Grant-LeaversFormCertificateAccess.ps1"

$readinessScript = Join-Path $root "deploy\Test-ProductionReadiness.ps1"

$publishRoot = [IO.Path]::GetFullPath($OutputPath)

$webOutput = Join-Path $publishRoot "Web"

$runnerOutput = Join-Path $publishRoot "Runner"

$logsOutput = Join-Path $webOutput "logs"

$checklistPath = Join-Path $publishRoot "FINAL-SETUP-CHECKLIST.txt"

$automationCerPath = Join-Path $publishRoot "LeaversForm-PowerShell-Automation.cer"

$automationPfxPath = Join-Path $publishRoot "LeaversForm-PowerShell-Automation.pfx"



Write-Step "Checking required tools"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {

    throw ".NET SDK/runtime is not installed. Install the .NET 8 SDK or hosting bundle first."

}

if (-not (Get-Command pwsh -ErrorAction SilentlyContinue)) {

    throw "PowerShell 7 (pwsh.exe) is required by LeaverProcess.ps1."

}

if (-not (Get-Module -ListAvailable -Name WebAdministration)) {

    throw "IIS/WebAdministration is not available. Install IIS management tools first."

}



if (-not $SkipModuleInstall) {

    Write-Step "Installing PowerShell modules"

    $modules = @(

        "ExchangeOnlineManagement",

        "Microsoft.Graph.Authentication",

        "Microsoft.Graph.Users",

        "Microsoft.Graph.Users.Actions",

        "Microsoft.Graph.Groups",
        "Microsoft.Graph.Identity.DirectoryManagement",
        "Microsoft.Graph.Applications",

        "Microsoft.Graph.Identity.DirectoryManagement"

    )

    foreach ($module in $modules) {

        if (-not (Get-Module -ListAvailable -Name $module)) {

            Install-Module $module -Scope AllUsers -Force -AllowClobber -ErrorAction Stop

            Write-Host "Installed $module" -ForegroundColor Green

        }

        else {

            Write-Host "$module already installed" -ForegroundColor DarkGray

        }

    }

    Write-Host "ActiveDirectory is a Windows feature — install RSAT-AD-PowerShell if not present." -ForegroundColor Yellow

}

else {

    Write-Warning "Skipped PowerShell module install (-SkipModuleInstall). Ensure Exchange/Graph modules are present."

}



Write-Step "Creating the PowerShell automation certificate"

if ($RecreateCertificate) {

    Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue |

        Where-Object { $_.Subject -eq $PowerShellCertificateSubject } |

        ForEach-Object { Remove-Item "Cert:\LocalMachine\My\$($_.Thumbprint)" -Force }

}



$automationCert = Get-ChildItem Cert:\LocalMachine\My |

    Where-Object { $_.Subject -eq $PowerShellCertificateSubject -and $_.HasPrivateKey } |

    Where-Object NotAfter -gt (Get-Date) |

    Sort-Object NotAfter -Descending |

    Select-Object -First 1



if (-not $automationCert) {

    $automationCert = New-SelfSignedCertificate `

        -Subject $PowerShellCertificateSubject `

        -CertStoreLocation "Cert:\LocalMachine\My" `

        -KeyAlgorithm RSA `

        -KeyLength 2048 `

        -KeyExportPolicy Exportable `

        -NotAfter (Get-Date).AddYears(2)

}



Export-Certificate -Cert $automationCert -FilePath $automationCerPath -Force | Out-Null

$pfxPassword = ConvertTo-SecureString $CertificateBackupPassword -AsPlainText -Force

Export-PfxCertificate -Cert $automationCert -FilePath $automationPfxPath -Password $pfxPassword -Force | Out-Null



Write-Host "Automation certificate thumbprint: $($automationCert.Thumbprint)" -ForegroundColor Yellow

Write-Host "Public certificate for Entra upload: $automationCerPath" -ForegroundColor Yellow

Write-Host "Private key backup (.pfx): $automationPfxPath (password: $CertificateBackupPassword)" -ForegroundColor Yellow



Write-Step "Granting IIS app pool access to certificate private key"

& $grantCertScript -Thumbprint $automationCert.Thumbprint -AppPoolIdentity $AppPoolIdentity -StoreLocation LocalMachine



Write-Step "Publishing Web and Runner"

& $publishScript -OutputPath $publishRoot -Configuration Release

if ($LASTEXITCODE -ne 0) {

    throw "Production publish failed."

}



New-Item -ItemType Directory -Path $logsOutput -Force | Out-Null



Write-Step "Copying maintenance scripts to publish folder"

$maintenanceScripts = @(

    "Repair-LiveProcessingCertificate.ps1",

    "Test-LiveProcessingReadiness.ps1"

)

foreach ($scriptName in $maintenanceScripts) {

    $source = Join-Path $root $scriptName

    if (Test-Path $source) {

        Copy-Item $source (Join-Path $publishRoot $scriptName) -Force

    }

}



Write-Step "Writing live server configuration"

$webLocalConfig = @{

    AzureAd = @{

        TenantId = $AzureTenantId.ToString()

        ClientId = $AzureClientId.ToString()

        ClientSecret = ConvertFrom-SecureString $AzureClientSecret -AsPlainText

    }

    ConnectionStrings = @{

        DefaultConnection = $SqlConnectionString

    }

    Graph = @{

        SenderMailbox = "IT@yoursclothing.co.uk"

        ItCcAddress = "IT@yoursclothing.co.uk"

        ErrorAlertMailbox = "it@yoursclothing.co.uk"

        RedirectAllEmailsTo = ""

        EmailTestMode = $false

    }

    LeaversForm = @{

        BaseUrl = "https://$HostName"

        EmailTestMode = $false

        ScriptTestMode = $false

        PowerShellScriptPath = (Join-Path $webOutput "LeaverProcess.ps1")

        PowerShellWorkingDirectory = $webOutput

        PowerShellExecutable = "pwsh.exe"

        PowerShellTenantId = $AzureTenantId.ToString()

        PowerShellAppId = $PowerShellAppId.ToString()

        PowerShellCertificateThumbprint = $automationCert.Thumbprint

        PowerShellExchangeOrganization = $PowerShellExchangeOrganization

        PowerShellDomainController = $PowerShellDomainController

        LogFolderPath = $logsOutput

    }

} | ConvertTo-Json -Depth 5



$webLocalPath = Join-Path $webOutput "appsettings.Production.local.json"

$webLocalConfig | Set-Content -Path $webLocalPath -Encoding UTF8

Copy-Item $webLocalPath (Join-Path $runnerOutput "appsettings.Production.local.json") -Force



if (-not $SkipIis) {

    Write-Step "Configuring IIS"

    & (Join-Path $root "deploy\configure-iis.ps1") `

        -SiteName $SiteName `

        -HostName $HostName `

        -PhysicalPath $webOutput `

        -AppPoolIdentity $AppPoolIdentity

}



if (-not $SkipDatabase) {

    Write-Step "Database reminder"

    Write-Host "Run the EF migration/database setup using the SQL connection in the generated local configuration."

}



Write-Step "Running production readiness checks"

& $readinessScript `

    -WebPath $webOutput `

    -CertificateThumbprint $automationCert.Thumbprint `

    -AppPoolIdentity $AppPoolIdentity `

    -CertificateStore LocalMachine

if ($LASTEXITCODE -ne 0) {

    Write-Warning "Some readiness checks failed — review output above before processing real leavers."

}



$checklist = @"

LeaversForm final setup

=======================

Generated: $(Get-Date -Format u)



ONE MANUAL ENTRA STEP

---------------------

[ ] Upload this public certificate to the PowerShell automation Entra app ($PowerShellAppId):

    $automationCerPath

[ ] Grant required Graph + Exchange application permissions and admin consent



AUTOMATION CERTIFICATE

----------------------

Thumbprint: $($automationCert.Thumbprint)

Entra upload (.cer): $automationCerPath

Private backup (.pfx): $automationPfxPath

PFX password: $CertificateBackupPassword



If the cert is ever lost from the server store, run from the publish folder:

  .\Repair-LiveProcessingCertificate.ps1 -PfxPath "$automationPfxPath" -PfxPassword "$CertificateBackupPassword"



COMPLETED BY THIS SCRIPT

------------------------

- Production Web and Runner published to $publishRoot

- PowerShell modules installed (unless -SkipModuleInstall)

- Exportable automation certificate in LocalMachine\My

- IIS app pool granted read access to certificate private key

- EmailTestMode=false, ScriptTestMode=false

- LeaverProcess.ps1 and logs folder under the Web publish path

- appsettings.Production.local.json written

- IIS configured (unless -SkipIis)

- Production readiness checks run



STILL REQUIRED BEFORE REAL LEAVERS

----------------------------------

[ ] Install RSAT-AD-PowerShell and verify AD permissions for $AppPoolIdentity

[ ] Install/bind a trusted TLS certificate for https://$HostName

[ ] Confirm SQL connection and run database migrations

[ ] Run LeaverProcess.ps1 manually with -WhatIf against a test account

[ ] Confirm Web app pool is AlwaysRunning with idle timeout 0

[ ] Run one controlled ASAP test leaver



CONFIG SUMMARY

--------------

Web URL: https://$HostName

Web path: $webOutput

Logs: $logsOutput

Domain controller: $PowerShellDomainController

Exchange org: $PowerShellExchangeOrganization

PowerShell app ID: $PowerShellAppId



SAFETY

------

- Do not run the standalone Runner at the same time as the Web hosted background service.

- Protect the publish directory — it contains the Entra client secret and .pfx backup.

"@

$checklist | Set-Content -Path $checklistPath -Encoding UTF8



Write-Host "`nFINAL BUILD READY" -ForegroundColor Green

Write-Host "Published Web: $webOutput"

Write-Host "Published Runner: $runnerOutput"

Write-Host "Checklist: $checklistPath"

Write-Host "Upload to Entra: $automationCerPath" -ForegroundColor Yellow

Write-Host "Certificate thumbprint: $($automationCert.Thumbprint)" -ForegroundColor Yellow

