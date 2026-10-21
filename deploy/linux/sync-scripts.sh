#!/usr/bin/env bash
# Copy LeaverProcess.ps1 and bootstrap into /opt/leaversform/scripts (run as root).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SCRIPTS_DIR="${1:-/opt/leaversform/scripts}"

mkdir -p "$SCRIPTS_DIR"

for name in LeaverProcess.ps1 LeaversForm-ModuleBootstrap.ps1; do
  if [[ -f "${ROOT}/scripts/${name}" ]]; then
    src="${ROOT}/scripts/${name}"
  elif [[ -f "${ROOT}/${name}" ]]; then
    src="${ROOT}/${name}"
  else
    echo "Missing ${name} in ${ROOT}/scripts" >&2
    exit 1
  fi
  install -m 644 "$src" "${SCRIPTS_DIR}/${name}"
done

chown www-data:www-data "$SCRIPTS_DIR" "${SCRIPTS_DIR}/"*.ps1 2>/dev/null || true
echo "Synced PowerShell scripts to ${SCRIPTS_DIR}"
