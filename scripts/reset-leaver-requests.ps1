# Clears leaver request rows from the dev SQLite database.
# Authorising managers, exclusions, and app users are preserved.
#
# Examples:
#   .\reset-leaver-requests.ps1 -List
#   .\reset-leaver-requests.ps1 -Name Bachu
#   .\reset-leaver-requests.ps1 -Id 18
#   .\reset-leaver-requests.ps1 -All

param(
    [string]$DatabasePath = (Join-Path $PSScriptRoot "..\LeaversForm.Web\leaversform.db"),
    [int]$Id = 0,
    [string]$Name = "",
    [switch]$List,
    [switch]$All
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "ResetLeaverRequests\ResetLeaverRequests.csproj"

$argsList = @($DatabasePath)
if ($List) { $argsList += "--list" }
elseif ($Id -gt 0) { $argsList += "--id=$Id" }
elseif ($Name) { $argsList += "--name=$Name" }
elseif ($All) { $argsList += "--all" }
else {
    Write-Host "Specify -List, -Id, -Name, or -All" -ForegroundColor Yellow
    exit 1
}

dotnet run --project $project -- @argsList
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Done." -ForegroundColor Green