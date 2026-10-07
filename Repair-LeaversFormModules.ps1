#Requires -Version 7.0

<#

.SYNOPSIS

  Repair Microsoft Graph and Exchange PowerShell modules for LeaversForm.



.DESCRIPTION

  Downloads known-good module versions to .psmodules (outside OneDrive) and verifies

  Exchange + Graph connect. The leaver script prefers this path automatically.



.EXAMPLE

  .\Repair-LeaversFormModules.ps1

#>

[CmdletBinding()]

param()



$ErrorActionPreference = 'Stop'



$bootstrap = Join-Path $PSScriptRoot 'scripts\LeaversForm-ModuleBootstrap.ps1'

if (-not (Test-Path $bootstrap)) {

    $bootstrap = Join-Path $PSScriptRoot 'LeaversForm-ModuleBootstrap.ps1'

}

if (-not (Test-Path $bootstrap)) {

    throw 'LeaversForm-ModuleBootstrap.ps1 not found.'

}



. $bootstrap

Initialize-LeaversFormModulePath -BootstrapPath $bootstrap



Write-Host ''

Write-Host 'LeaversForm module repair' -ForegroundColor Cyan

Install-LeaversFormLocalModules

Clear-LeaversFormSessionModules

Test-LeaversFormModuleConnectivity



Write-Host ''

Write-Host 'Graph and Exchange modules repaired and verified.' -ForegroundColor Green

