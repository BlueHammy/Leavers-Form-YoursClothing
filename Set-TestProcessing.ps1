#Requires -Version 7.0
<#
.SYNOPSIS
  Turn ON ScriptTestMode so submit/retry does not disable real accounts.

.EXAMPLE
  .\Set-TestProcessing.ps1
#>
[CmdletBinding()]
param(
    [string] $LocalSettingsPath = (Join-Path $PSScriptRoot 'LeaversForm.Web\appsettings.Development.local.json')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $LocalSettingsPath)) {
    throw "Settings file not found: $LocalSettingsPath"
}

$json = Get-Content $LocalSettingsPath -Raw | ConvertFrom-Json
if (-not $json.LeaversForm) {
    $json | Add-Member -NotePropertyName LeaversForm -NotePropertyValue ([pscustomobject]@{})
}
$json.LeaversForm | Add-Member -NotePropertyName ScriptTestMode -NotePropertyValue $true -Force
($json | ConvertTo-Json -Depth 6) | Set-Content -Path $LocalSettingsPath -Encoding UTF8

Write-Host 'ScriptTestMode is now TRUE (safe testing).' -ForegroundColor Green
Write-Host 'Restart Start-LanTest.ps1 and confirm the yellow ScriptTestMode: ON banner.' -ForegroundColor Yellow
