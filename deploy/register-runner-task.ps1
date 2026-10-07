#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Register a Task Scheduler job to run LeaversForm.Runner every 5 minutes.

.EXAMPLE
  .\register-runner-task.ps1 `
    -TaskName "LeaversForm Runner" `
    -RunnerPath "C:\inetpub\LeaversForm\Runner\LeaversForm.Runner.exe" `
    -RunAs "YOURDOMAIN\svc-leaversform"
#>
[CmdletBinding()]
param(
    [string] $TaskName = "LeaversForm Runner",
    [Parameter(Mandatory = $true)]
    [string] $RunnerPath,
    [Parameter(Mandatory = $true)]
    [string] $RunAs,
    [string] $RunAsPassword
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $RunnerPath)) {
    throw "Runner executable not found: $RunnerPath"
}

$RunnerDir = Split-Path $RunnerPath -Parent
$Action = New-ScheduledTaskAction -Execute $RunnerPath -WorkingDirectory $RunnerDir
$Trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).Date -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration ([TimeSpan]::MaxValue)
$Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew

$Principal = if ($RunAsPassword) {
    $Secure = ConvertTo-SecureString $RunAsPassword -AsPlainText -Force
    New-ScheduledTaskPrincipal -UserId $RunAs -LogonType Password -RunLevel Highest
} else {
    New-ScheduledTaskPrincipal -UserId $RunAs -LogonType Password -RunLevel Highest
}

Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Settings $Settings -Principal $Principal -Force | Out-Null

Write-Host "Registered scheduled task: $TaskName"
Write-Host "  Runs every 5 minutes as $RunAs"
Write-Host "  Executable: $RunnerPath"
Write-Host ""
Write-Host "Set environment variables on the task (Task Scheduler -> task -> Environment):"
Write-Host "  ASPNETCORE_ENVIRONMENT=Production"
Write-Host "  AzureAd__TenantId, AzureAd__ClientId, AzureAd__ClientSecret (if not in appsettings)"
