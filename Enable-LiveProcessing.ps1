#Requires -Version 7.0
<#
.SYNOPSIS
  Turn OFF ScriptTestMode for live leaver processing on this laptop/dev install.

.NOTES
  Always restart the web app after changing this flag. Run Test-LiveProcessingReadiness.ps1 to verify.
#>
[CmdletBinding()]
param(
    [string] $LocalSettingsPath = (Join-Path $PSScriptRoot 'LeaversForm.Web\appsettings.Development.local.json'),
    [switch] $SkipReadinessTest
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $LocalSettingsPath)) {
    throw "Settings file not found: $LocalSettingsPath. Run Set-EntraSecrets.ps1 or Initialize-LiveProcessingTest.ps1 first."
}

$json = Get-Content $LocalSettingsPath -Raw | ConvertFrom-Json
if (-not $json.LeaversForm) {
    throw 'LeaversForm section missing from local settings.'
}

$json.LeaversForm | Add-Member -NotePropertyName ScriptTestMode -NotePropertyValue $false -Force
($json | ConvertTo-Json -Depth 6) | Set-Content -Path $LocalSettingsPath -Encoding UTF8

Write-Host 'ScriptTestMode is now FALSE (live processing).' -ForegroundColor Yellow
Write-Host 'Restart Start-LanTest.ps1 and confirm: ScriptTestMode: OFF' -ForegroundColor Red

if (-not $SkipReadinessTest) {
    & (Join-Path $PSScriptRoot 'Test-LiveProcessingReadiness.ps1') -LocalSettingsPath $LocalSettingsPath -NoPause
}
