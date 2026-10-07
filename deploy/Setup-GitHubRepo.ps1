<#
.SYNOPSIS
  Creates the GitHub repo "Leavers-Form-YoursClothing" and pushes main (run once from your dev PC).

.PREREQUISITE
  gh auth login   # complete browser/device sign-in first
#>
[CmdletBinding()]
param(
    [string] $RepoName = "Leavers-Form-YoursClothing",
    [ValidateSet("private", "public", "internal")]
    [string] $Visibility = "private"
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $Root
try {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) is not installed. Install with: winget install GitHub.cli"
    }

    gh auth status | Out-Null

    $existing = git remote get-url origin 2>$null
    if ($existing) {
        Write-Host "Remote origin already set: $existing"
        git push -u origin main
        return
    }

    gh repo create $RepoName `
        --$Visibility `
        --description "Leavers Form YoursClothing" `
        --source . `
        --remote origin `
        --push

    Write-Host ""
    Write-Host "Done. In GitHub repo Settings, set Display name to: Leavers Form YoursClothing" -ForegroundColor Green
    Write-Host "Next: docs/GITHUB-VM-SYNC.md - install self-hosted runner on the VM." -ForegroundColor Green
}
finally {
    Pop-Location
}
