# Regenerates docs/LeaversForm-IT-Guide.pdf from the HTML source.
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$docs = Split-Path -Parent $MyInvocation.MyCommand.Path
$html = Join-Path $docs 'LeaversForm-IT-Guide.html'
$pdf = Join-Path $docs 'LeaversForm-IT-Guide.pdf'

$edgeCandidates = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
)
$edge = $edgeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge is required to print the PDF.' }

if (-not (Test-Path $html)) { throw "Missing $html" }

Remove-Item $pdf -ErrorAction SilentlyContinue
Start-Process -FilePath $edge -ArgumentList @(
    '--headless=new',
    '--disable-gpu',
    '--run-all-compositor-stages-before-draw',
    '--virtual-time-budget=10000',
    "--print-to-pdf=$pdf",
    $html
) -Wait -NoNewWindow

if (-not (Test-Path $pdf)) { throw 'PDF generation failed.' }
Write-Host "Created $pdf" -ForegroundColor Green
