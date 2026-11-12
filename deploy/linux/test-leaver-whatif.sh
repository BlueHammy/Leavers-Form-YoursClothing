#!/usr/bin/env bash
# Dry-run LeaverProcess.ps1 using values from appsettings.Development.local.json on the VM.
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: sudo bash deploy/linux/test-leaver-whatif.sh USER@domain SAM [settings.json]" >&2
  exit 2
fi

UPN="$1"
SAM="$2"
SETTINGS="${3:-/opt/leaversform/app/appsettings.Development.local.json}"
LOG="/opt/leaversform/logs/manual-whatif-$(date +%Y%m%d-%H%M%S).log"

if [[ ! -f "$SETTINGS" ]]; then
  echo "Settings not found: $SETTINGS" >&2
  exit 1
fi

read_json() {
  python3 - "$SETTINGS" "$1" <<'PY'
import json, sys
path, key = sys.argv[1], sys.argv[2]
with open(path, encoding="utf-8") as f:
    data = json.load(f)
cur = data
for part in key.split("."):
    cur = cur[part]
print(cur)
PY
}

TENANT="$(read_json LeaversForm.PowerShellTenantId)"
APPID="$(read_json LeaversForm.PowerShellAppId)"
THUMB="$(read_json LeaversForm.PowerShellCertificateThumbprint)"
EXO="$(read_json LeaversForm.PowerShellExchangeOrganization)"
DC="$(read_json LeaversForm.PowerShellDomainController)"
PFX="$(read_json LeaversForm.PowerShellCertificatePath)"
SCRIPT="$(read_json LeaversForm.PowerShellScriptPath)"

export LEAVERSFORM_PS_CERTIFICATE_PATH="$PFX"
export LEAVERSFORM_PS_CERTIFICATE_PASSWORD="${LeaversForm__PowerShellCertificatePassword:-LeaversForm-Cert-Backup}"

pwsh -NoProfile -File "$SCRIPT" \
  -UserPrincipalName "$UPN" \
  -SamAccountName "$SAM" \
  -TenantId "$TENANT" \
  -AppId "$APPID" \
  -CertificateThumbprint "$THUMB" \
  -ExchangeOrganization "$EXO" \
  -DomainController "$DC" \
  -LogPath "$LOG" \
  -WhatIf

echo "Log: $LOG"
