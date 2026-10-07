#Requires -Version 7.0
<#
.SYNOPSIS
  Store Entra app credentials locally for Leavers Form.

  Saves to appsettings.Development.local.json (reliable) and also tries user secrets.

.EXAMPLE
  .\Set-EntraSecrets.ps1
#>
[CmdletBinding()]
param(
    [string] $Project = (Join-Path $PSScriptRoot 'LeaversForm.Web\LeaversForm.Web.csproj'),
    [string] $LocalSettingsPath = (Join-Path $PSScriptRoot 'LeaversForm.Web\appsettings.Development.local.json')
)

$ErrorActionPreference = 'Stop'

function Wait-BeforeClose {
    Write-Host ''
    Read-Host 'Press Enter to close'
}

try {
    if (-not (Test-Path $Project)) {
        throw "Could not find $Project"
    }

    Write-Host ''
    Write-Host 'Leavers Form — Entra local setup' -ForegroundColor Cyan
    Write-Host 'These values come from Entra → App registrations → your app → Overview / Certificates & secrets.'
    Write-Host 'They are saved locally on this PC only (gitignored).'
    Write-Host ''
    Write-Host 'Copy these from Entra → App registrations → YOUR Leavers Form app → Overview:'
    Write-Host ''
    Write-Host '  Directory (tenant) ID  →  AzureAd:TenantId   (your organisation ID)' -ForegroundColor DarkGray
    Write-Host '  Application (client) ID → AzureAd:ClientId   (this app''s ID — different from tenant ID)' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host 'For the client secret: paste the secret VALUE shown once at creation.' -ForegroundColor Yellow
    Write-Host 'Do NOT paste the Secret ID — that causes AADSTS7000215 invalid_client.' -ForegroundColor Yellow
    Write-Host ''

    $tenantId = Read-Host 'Directory (tenant) ID — from Overview, NOT the application ID'
    $clientId = Read-Host 'Application (client) ID — from Overview, for this app registration'
    $clientSecretPlain = (Read-Host 'Client secret value (visible while typing)').Trim()

    if ([string]::IsNullOrWhiteSpace($tenantId) -or
        [string]::IsNullOrWhiteSpace($clientId) -or
        [string]::IsNullOrWhiteSpace($clientSecretPlain)) {
        throw 'Tenant ID, Client ID, and Client secret are all required.'
    }

    if ($tenantId.Trim().Equals($clientId.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Tenant ID and Client ID must be different. You likely pasted the Application (client) ID into both fields. On the Entra Overview page, use Directory (tenant) ID for the first prompt and Application (client) ID for the second.'
    }

    $settings = [ordered]@{}
    if (Test-Path $LocalSettingsPath) {
        $existing = Get-Content $LocalSettingsPath -Raw | ConvertFrom-Json
        foreach ($prop in $existing.PSObject.Properties) {
            $settings[$prop.Name] = $prop.Value
        }
    }

    $settings['AzureAd'] = [ordered]@{
        Instance = 'https://login.microsoftonline.com/'
        Domain = 'yoursclothing.co.uk'
        TenantId = $tenantId.Trim()
        ClientId = $clientId.Trim()
        ClientSecret = $clientSecretPlain
        CallbackPath = '/signin-oidc'
        SignedOutCallbackPath = '/signout-callback-oidc'
    }

    ($settings | ConvertTo-Json -Depth 5) | Set-Content -Path $LocalSettingsPath -Encoding UTF8
    Write-Host "Saved Azure AD settings to $LocalSettingsPath" -ForegroundColor Green

    try {
        dotnet user-secrets set "AzureAd:TenantId" $tenantId.Trim() --project $Project | Out-Null
        dotnet user-secrets set "AzureAd:ClientId" $clientId.Trim() --project $Project | Out-Null
        dotnet user-secrets set "AzureAd:ClientSecret" $clientSecretPlain --project $Project | Out-Null
        Write-Host 'Also saved to dotnet user secrets.' -ForegroundColor DarkGreen
    }
    catch {
        Write-Warning 'Could not save dotnet user secrets, but appsettings.Development.local.json was saved and will be used.'
    }

    Write-Host ''
    Write-Host 'Setup complete. You can now run .\Start-LanTest.ps1' -ForegroundColor Cyan
}
catch {
    Write-Host ''
    Write-Host 'ERROR' -ForegroundColor Red
    Write-Host $_.Exception.Message
    Wait-BeforeClose
    exit 1
}

Wait-BeforeClose
