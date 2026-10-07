#Requires -Version 5.1
<#
.SYNOPSIS
  Marks orphaned Pending leaver requests as Failed when their offboarding log has stopped updating.

.EXAMPLE
  .\Reconcile-StuckLeavers.ps1
  .\Reconcile-StuckLeavers.ps1 -StuckMinutes 3
#>
[CmdletBinding()]
param(
    [string]$DatabasePath = (Join-Path $PSScriptRoot "..\LeaversForm.Web\leaversform.db"),
    [string]$LogFolderPath = (Join-Path $PSScriptRoot "..\logs"),
    [int]$StuckMinutes = 5
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DatabasePath)) {
    throw "Database not found: $DatabasePath"
}

Add-Type -Path (Get-ChildItem (Join-Path $PSScriptRoot "..\LeaversForm.Web\bin\Debug\net8.0") -Recurse -Filter "Microsoft.Data.Sqlite.dll" | Select-Object -First 1 -ExpandProperty FullName)

$connectionString = "Data Source=$DatabasePath"
$connection = New-Object Microsoft.Data.Sqlite.SqliteConnection $connectionString
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT Id, LeaverDisplayName, ScriptLastAttemptAt
FROM LeaverRequests
WHERE Status = 0 AND ScriptExecutedAt IS NULL
"@
    $reader = $command.ExecuteReader()
    $pending = @()
    while ($reader.Read()) {
        $pending += [pscustomobject]@{
            Id = [int]$reader.GetValue(0)
            Name = [string]$reader.GetValue(1)
            ScriptLastAttemptAt = if ($reader.IsDBNull(2)) { $null } else { [datetime]$reader.GetValue(2) }
        }
    }
    $reader.Close()

    if ($pending.Count -eq 0) {
        Write-Host "No pending leaver requests found." -ForegroundColor Green
        return
    }

    $nowUtc = [datetime]::UtcNow
    $threshold = [timespan]::FromMinutes($StuckMinutes)
    $updated = 0

    foreach ($row in $pending) {
        $latestLog = Get-ChildItem -Path $LogFolderPath -Filter "offboarding-$($row.Id)-*.log" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        $lastActivityUtc = if ($row.ScriptLastAttemptAt) {
            if ($row.ScriptLastAttemptAt.Kind -eq [System.DateTimeKind]::Unspecified) {
                [datetime]::SpecifyKind($row.ScriptLastAttemptAt, [System.DateTimeKind]::Utc)
            }
            else {
                $row.ScriptLastAttemptAt.ToUniversalTime()
            }
        }
        else {
            [datetime]::MinValue
        }

        if ($latestLog) {
            $logUtc = $latestLog.LastWriteTimeUtc
            if ($logUtc -gt $lastActivityUtc) { $lastActivityUtc = $logUtc }
        }

        $inactiveFor = $nowUtc - $lastActivityUtc
        if ($inactiveFor -lt $threshold) {
            Write-Host "Request $($row.Id) ($($row.Name)) still within ${StuckMinutes}m activity window ($([math]::Round($inactiveFor.TotalMinutes, 1))m inactive)." -ForegroundColor DarkGray
            continue
        }

        $logText = if ($latestLog) { Get-Content -Path $latestLog.FullName -Raw } else { $null }
        $entraDisabled = $logText -and $logText -match 'Disabled Entra ID account'
        $adDisabled = $logText -and $logText -match 'Disabled AD account'

        if ($entraDisabled -and $adDisabled) {
            $update = $connection.CreateCommand()
            $update.CommandText = @"
UPDATE LeaverRequests
SET Status = 2,
    ScriptExecutionStatus = 1,
    ScriptExecutedAt = datetime('now'),
    ScriptLastFailedAt = NULL
WHERE Id = @id AND Status = 0
"@
            $update.Parameters.AddWithValue("@id", $row.Id) | Out-Null
            if ($update.ExecuteNonQuery() -gt 0) {
                Write-Host "Request $($row.Id) ($($row.Name)) reconciled to Completed (core disable finished in log)." -ForegroundColor Green
                $updated++
            }
            continue
        }

        $detail = if (-not $latestLog) {
            "No offboarding log was written."
        }
        else {
            "Latest log stopped at $($latestLog.LastWriteTimeUtc.ToUniversalTime().ToString('u'))."
        }

        $fail = $connection.CreateCommand()
        $fail.CommandText = @"
UPDATE LeaverRequests
SET Status = 4,
    ScriptExecutionStatus = 2,
    ScriptExecutedAt = NULL,
    ScriptLastFailedAt = datetime('now')
WHERE Id = @id AND Status = 0
"@
        $fail.Parameters.AddWithValue("@id", $row.Id) | Out-Null
        if ($fail.ExecuteNonQuery() -gt 0) {
            Write-Host "Request $($row.Id) ($($row.Name)) marked Failed ($detail)" -ForegroundColor Yellow
            $updated++
        }
    }

    if ($updated -eq 0) {
        Write-Host "No stuck pending requests required reconciliation." -ForegroundColor Green
    }
}
finally {
    $connection.Close()
}
