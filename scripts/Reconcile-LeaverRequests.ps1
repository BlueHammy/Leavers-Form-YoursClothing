#Requires -Version 5.1
<#
.SYNOPSIS
  Runs leaver reconciliation once (updates stuck Pending rows from offboarding logs).
#>
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\LeaversForm.Web\LeaversForm.Web.csproj")
)

$ErrorActionPreference = "Stop"
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Start app, wait for startup reconcile, then stop.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$logOut = Join-Path $repoRoot "logs\reconcile-run.log"
$logErr = Join-Path $repoRoot "logs\reconcile-run.err"

$p = Start-Process -FilePath "dotnet" -ArgumentList @(
    "run", "--project", $ProjectPath, "--no-build", "--no-launch-profile"
) -WorkingDirectory $repoRoot -PassThru -RedirectStandardOutput $logOut -RedirectStandardError $logErr -WindowStyle Hidden

try {
    Start-Sleep -Seconds 18
}
finally {
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Reconciliation run complete. Recent log output:" -ForegroundColor Cyan
if (Test-Path $logOut) { Get-Content $logOut -Tail 8 }
& (Join-Path $PSScriptRoot "reset-leaver-requests.ps1") -List
