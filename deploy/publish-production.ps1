<#
.SYNOPSIS
  Publish LeaversForm Web and Runner for internal IIS deployment.

.EXAMPLE
  .\publish-production.ps1
  .\publish-production.ps1 -OutputPath "D:\Deploy\LeaversForm"
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\publish-output"),
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$WebPublish = Join-Path $OutputPath "Web"
$RunnerPublish = Join-Path $OutputPath "Runner"

Write-Host "Restoring and publishing LeaversForm ($Configuration)..."

Push-Location $Root
try {
    dotnet restore LeaversForm.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

    dotnet publish (Join-Path $Root "LeaversForm.Web\LeaversForm.Web.csproj") `
        -c $Configuration `
        -o $WebPublish `
        --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Web publish failed" }

    dotnet publish (Join-Path $Root "LeaversForm.Runner\LeaversForm.Runner.csproj") `
        -c $Configuration `
        -o $RunnerPublish `
        --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Runner publish failed" }

    Copy-Item (Join-Path $Root "docs\PRODUCTION-DEPLOYMENT.md") (Join-Path $OutputPath "PRODUCTION-DEPLOYMENT.md") -Force
    Copy-Item (Join-Path $Root "deploy\configure-iis.ps1") (Join-Path $OutputPath "configure-iis.ps1") -Force
    Copy-Item (Join-Path $Root "deploy\configure-firewall-outbound.ps1") (Join-Path $OutputPath "configure-firewall-outbound.ps1") -Force
    Copy-Item (Join-Path $Root "deploy\register-runner-task.ps1") (Join-Path $OutputPath "register-runner-task.ps1") -Force
    Copy-Item (Join-Path $Root "deploy\appsettings.Production.local.json.example") (Join-Path $OutputPath "appsettings.Production.local.json.example") -Force

    Write-Host ""
    Write-Host "Published to: $OutputPath"
    Write-Host "  Web:    $WebPublish"
    Write-Host "  Runner: $RunnerPublish"
    Write-Host ""
    Write-Host "See docs/PRODUCTION-DEPLOYMENT.md for IIS, firewall, and Entra ID setup."
}
finally {
    Pop-Location
}
