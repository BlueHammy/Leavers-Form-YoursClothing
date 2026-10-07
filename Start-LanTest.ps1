#Requires -Version 7.0
<#
.SYNOPSIS
  Run Leavers Form on port 5044 over HTTPS so colleagues on your network can test with Entra SSO.

  Entra only accepts redirect URIs starting with https:// or http://localhost — plain http://
  on a LAN IP or PC name is rejected. This script creates a self-signed certificate and runs HTTPS.

.EXAMPLE
  .\Start-LanTest.ps1
#>
[CmdletBinding()]
param(
    [int] $Port = 5044,
    [string] $CertPassword = 'LeaversForm-LanTest',
    [switch] $NoPause,
    [switch] $RecreateCertificate
)

$ErrorActionPreference = 'Stop'

function Wait-BeforeClose {
    if ($NoPause) { return }
    Write-Host ''
    Read-Host 'Press Enter to close'
}

try {
    $repoRoot = $PSScriptRoot
    $project = Join-Path $repoRoot 'LeaversForm.Web\LeaversForm.Web.csproj'
    $webRoot = Join-Path $repoRoot 'LeaversForm.Web'
    $certDir = Join-Path $repoRoot '.lan-test'
    $certPath = Join-Path $certDir 'lan-test.pfx'
    $lanSettingsPath = Join-Path $webRoot 'appsettings.LanTest.local.json'

    if (-not (Test-Path $project)) {
        throw "Could not find $project. Run this script from the repo root."
    }

    function Test-AzureAdConfigured {
        if ($env:AzureAd__TenantId) { return $true }
        if ($env:AZUREAD__TENANTID) { return $true }

        foreach ($file in @('appsettings.Development.local.json', 'appsettings.SemiLive.local.json', 'appsettings.LanTest.local.json')) {
            $path = Join-Path $webRoot $file
            if (-not (Test-Path $path)) { continue }
            try {
                $json = Get-Content $path -Raw | ConvertFrom-Json
                if ($json.AzureAd.TenantId) { return $true }
            }
            catch {
                Write-Warning "Could not read $file"
            }
        }

        $secretsPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\leaversform-web-dev\secrets.json'
        if (Test-Path $secretsPath) {
            try {
                $json = Get-Content $secretsPath -Raw | ConvertFrom-Json
                if ($json.'AzureAd:TenantId') { return $true }
            }
            catch { }
        }

        $secretList = & dotnet user-secrets list --project $project 2>$null
        if ($LASTEXITCODE -eq 0 -and ($secretList | Out-String) -match 'AzureAd:TenantId') { return $true }

        return $false
    }

    if (-not (Test-AzureAdConfigured)) {
        throw @"
Azure AD is not configured on this machine.

Setting up Entra in the portal is not enough — the app also needs the tenant/client/secret stored locally on this PC.

Run this once from the repo root:

  .\Set-EntraSecrets.ps1

That saves credentials to appsettings.Development.local.json on this PC.
If you already ran dotnet user-secrets successfully, run Set-EntraSecrets.ps1 once anyway —
user secrets did not persist correctly on this machine earlier.
"@
    }

    $hostname = [System.Net.Dns]::GetHostName()
    try {
        $fqdn = [System.Net.Dns]::GetHostEntry($hostname).HostName
    }
    catch {
        $fqdn = $hostname
    }

    $ipCandidates = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object {
            $_.IPAddress -notlike '127.*' -and
            $_.PrefixOrigin -ne 'WellKnown' -and
            $_.InterfaceAlias -notmatch 'Loopback|vEthernet|VirtualBox|VMware|Hyper-V|WSL|Docker|TAP|Bluetooth'
        }

    $ip = $ipCandidates |
        Where-Object { $_.PrefixOrigin -eq 'Dhcp' } |
        Sort-Object InterfaceMetric |
        Select-Object -First 1 -ExpandProperty IPAddress

    if (-not $ip) {
        $ip = $ipCandidates |
            Sort-Object InterfaceMetric |
            Select-Object -First 1 -ExpandProperty IPAddress
    }

    $dnsNames = @($hostname, $fqdn, 'localhost') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    if ($ip) { $dnsNames += $ip }
    $dnsNames = $dnsNames | Select-Object -Unique

    $needsCert = $RecreateCertificate -or -not (Test-Path $certPath)
    if (-not $needsCert -and (Test-Path $certPath)) {
        try {
            $existingSan = (Get-PfxData -FilePath $certPath -Password (ConvertTo-SecureString $CertPassword -AsPlainText -Force)).EndEntityCertificates[0].DnsNameList |
                ForEach-Object { $_.Unicode.ToLowerInvariant() }
            foreach ($name in $dnsNames) {
                if ($existingSan -notcontains $name.ToLowerInvariant()) {
                    $needsCert = $true
                    break
                }
            }
        }
        catch {
            $needsCert = $true
        }
    }

    if ($needsCert) {
        if (Test-Path $certPath) {
            Remove-Item $certPath -Force
            Write-Host 'Recreating LAN test certificate with updated host names...' -ForegroundColor Cyan
        }

        New-Item -ItemType Directory -Path $certDir -Force | Out-Null
        Write-Host "Creating self-signed certificate for: $($dnsNames -join ', ')" -ForegroundColor Cyan
        $cert = New-SelfSignedCertificate `
            -Subject "CN=$fqdn" `
            -DnsName $dnsNames `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -KeyExportPolicy Exportable `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -NotAfter (Get-Date).AddMonths(6)

        $securePassword = ConvertTo-SecureString -String $CertPassword -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $certPath -Password $securePassword | Out-Null
        Write-Host "Saved certificate to $certPath" -ForegroundColor DarkGreen
    }

    $shareUrls = @()
    if ($ip) { $shareUrls += "https://${ip}:$Port" }
    if ($fqdn -and "https://${fqdn}:$Port" -notin $shareUrls) { $shareUrls += "https://${fqdn}:$Port" }
    if ("https://${hostname}:$Port" -notin $shareUrls) { $shareUrls += "https://${hostname}:$Port" }

    $primaryUrl = $shareUrls[0]
    $ruleName = "LeaversForm LAN test ($Port)"

    $lanSettings = @{
        Kestrel = @{
            Endpoints = @{
                Https = @{
                    Url = "https://0.0.0.0:$Port"
                    Certificate = @{
                        Path = ($certPath -replace '\\', '/')
                        Password = $CertPassword
                    }
                }
            }
        }
        LeaversForm = @{
            BaseUrl = $primaryUrl
            RequireEntraSso = $true
        }
    } | ConvertTo-Json -Depth 5

    Set-Content -Path $lanSettingsPath -Value $lanSettings -Encoding UTF8

    $devLocalPath = Join-Path $webRoot 'appsettings.Development.local.json'
    $scriptTestMode = $true
    if (Test-Path $devLocalPath) {
        try {
            $devLocal = Get-Content $devLocalPath -Raw | ConvertFrom-Json
            if ($null -ne $devLocal.LeaversForm.ScriptTestMode) {
                $scriptTestMode = [bool]$devLocal.LeaversForm.ScriptTestMode
            }
        }
        catch { }
    }

    Write-Host ''
    Write-Host 'Leavers Form — LAN test mode (HTTPS)' -ForegroundColor Cyan
    if ($scriptTestMode) {
        Write-Host 'ScriptTestMode: ON — accounts will NOT be disabled on submit.' -ForegroundColor Yellow
        Write-Host 'To process a real leaver, run .\Set-LiveProcessing.ps1 then restart this script.' -ForegroundColor Yellow
    }
    else {
        Write-Host 'ScriptTestMode: OFF — LIVE leaver processing is enabled.' -ForegroundColor Red
        Write-Host 'Submitting a leaver (or Disable ASAP) will run LeaverProcess.ps1 and disable the account.' -ForegroundColor Red
        Write-Host 'AD disable uses YOUR Windows login — you need delegated AD rights (reset + disable). See Initialize-LiveProcessingTest STEP 5b.' -ForegroundColor Yellow
    }
    Write-Host 'IMPORTANT: use https:// (not http://). http:// on this port gives "did not send any data".' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Share one of these URLs with testers (IP is most reliable):' -ForegroundColor Cyan
    foreach ($url in $shareUrls) {
        Write-Host "  $url" -ForegroundColor Green
    }
    Write-Host ''
    Write-Host 'Testers will see a certificate warning (self-signed) — choose Advanced / Continue / Proceed.'
    Write-Host ''
    Write-Host 'Add these redirect URIs in Entra (Authentication → Web):'
    Write-Host "  $primaryUrl/signin-oidc"
    Write-Host "  $primaryUrl/signout-callback-oidc"
    if ($shareUrls.Count -gt 1) {
        Write-Host "  $($shareUrls[1])/signin-oidc"
        Write-Host "  $($shareUrls[1])/signout-callback-oidc"
    }
    Write-Host ''

    if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
        try {
            New-NetFirewallRule `
                -DisplayName $ruleName `
                -Direction Inbound `
                -Protocol TCP `
                -LocalPort $Port `
                -Action Allow `
                -Profile Any | Out-Null
            Write-Host "Added Windows Firewall rule: $ruleName" -ForegroundColor DarkGreen
        }
        catch {
            Write-Warning "Could not add firewall rule automatically. Run this script as Administrator or allow inbound TCP $Port manually."
            Write-Warning $_.Exception.Message
        }
    }
    else {
        Write-Host "Firewall rule already exists: $ruleName" -ForegroundColor DarkGreen
    }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
    $env:LeaversForm__BaseUrl = $primaryUrl

    Write-Host ''
    Write-Host "Starting app on https://0.0.0.0:$Port (Ctrl+C to stop)..." -ForegroundColor Cyan
    Write-Host ''

    Push-Location $repoRoot
    try {
        dotnet run --project $project --no-launch-profile
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
            throw "The app exited with code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-Host ''
    Write-Host 'ERROR' -ForegroundColor Red
    Write-Host '-----' -ForegroundColor Red
    Write-Host $_.Exception.Message
    if ($_.ScriptStackTrace) {
        Write-Host ''
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    }
    Wait-BeforeClose
    exit 1
}
