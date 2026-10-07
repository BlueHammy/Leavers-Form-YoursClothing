#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${1:-/opt/leaversform/app}"

echo "Publishing LeaversForm.Web to $OUT ..."
dotnet publish "$ROOT/LeaversForm.Web/LeaversForm.Web.csproj" -c Release -o "$OUT"

mkdir -p /opt/leaversform/scripts
cp -f "$OUT/LeaverProcess.ps1" /opt/leaversform/scripts/ 2>/dev/null || cp -f "$ROOT/scripts/LeaverProcess.ps1" /opt/leaversform/scripts/

echo "Done. Restart: sudo systemctl restart leaversform"
