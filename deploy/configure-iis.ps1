#Requires -RunAsAdministrator
#Requires -Modules WebAdministration
<#
.SYNOPSIS
  Create IIS site and app pool for internal LeaversForm hosting.

.EXAMPLE
  .\configure-iis.ps1 `
    -SiteName "LeaversForm" `
    -HostName "leavers.internal.yoursclothing.co.uk" `
    -PhysicalPath "C:\inetpub\LeaversForm\Web" `
    -AppPoolIdentity "YOURDOMAIN\svc-leaversform"
#>
[CmdletBinding()]
param(
    [string] $SiteName = "LeaversForm",
    [Parameter(Mandatory = $true)]
    [string] $HostName,
    [Parameter(Mandatory = $true)]
    [string] $PhysicalPath,
    [string] $AppPoolIdentity = "ApplicationPoolIdentity",
    [int] $HttpsPort = 443
)

$ErrorActionPreference = "Stop"
$AppPoolName = $SiteName

if (-not (Test-Path $PhysicalPath)) {
    throw "Physical path not found: $PhysicalPath. Publish the app first (deploy\publish-production.ps1)."
}

Import-Module WebAdministration

if (-not (Get-WebAppPoolState -Name $AppPoolName -ErrorAction SilentlyContinue)) {
    New-WebAppPool -Name $AppPoolName | Out-Null
    Write-Host "Created app pool: $AppPoolName"
}

Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)

if ($AppPoolIdentity -ne "ApplicationPoolIdentity") {
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value 3
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.userName -Value $AppPoolIdentity
    Write-Host "App pool identity: $AppPoolIdentity (ensure password is configured in IIS if required)"
}

if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) {
    Write-Host "Site already exists: $SiteName"
} else {
    New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port $HttpsPort -HostHeader $HostName -Ssl | Out-Null
    Write-Host "Created site: $SiteName at https://${HostName}:${HttpsPort}"
}

Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
Set-ItemProperty "IIS:\Sites\$SiteName" -Name preloadEnabled -Value $true

# Production environment variable for ASP.NET Core module
$aspNetCorePath = "system.webServer/aspNetCore"
Filter-WebConfigurationProperty -PSPath "IIS:\Sites\$SiteName" -Filter $aspNetCorePath -Name "." -ErrorAction SilentlyContinue | Out-Null

Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Bind an internal TLS certificate for $HostName in IIS"
Write-Host "  2. Set app pool environment: ASPNETCORE_ENVIRONMENT=Production"
Write-Host "  3. Set AzureAd secrets via environment variables (see docs/PRODUCTION-DEPLOYMENT.md)"
Write-Host "  4. Restrict site access to corporate network / VPN only"
Write-Host "  5. Run deploy\configure-firewall-outbound.ps1 for Microsoft outbound access"
