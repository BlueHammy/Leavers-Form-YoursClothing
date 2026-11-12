#!/usr/bin/env bash
# Read-only check of VM settings for Entra / automation (no secrets printed).
set -euo pipefail

SETTINGS="${1:-/opt/leaversform/app/appsettings.Development.local.json}"

if [[ ! -f "$SETTINGS" ]]; then
  echo "MISSING: $SETTINGS"
  exit 1
fi

python3 - "$SETTINGS" <<'PY'
import json, os, sys
path = sys.argv[1]
with open(path, encoding="utf-8") as f:
    cfg = json.load(f)

def ok(label, val):
    good = bool(val and str(val).strip() and not str(val).startswith("YOUR-"))
    mark = "OK" if good else "MISSING"
    print(f"  [{mark}] {label}")
    return good

print("Web sign-in (AzureAd) — removes Michael Newton bypass when TenantId is set:")
azure = cfg.get("AzureAd") or {}
ok("AzureAd:TenantId", azure.get("TenantId"))
ok("AzureAd:ClientId (web SSO app, not automation)", azure.get("ClientId"))
ok("AzureAd:ClientSecret", azure.get("ClientSecret"))

print("")
print("LeaversForm automation (Graph/Exchange via LeaverProcess.ps1):")
lf = cfg.get("LeaversForm") or {}
ok("PowerShellTenantId", lf.get("PowerShellTenantId"))
ok("PowerShellAppId", lf.get("PowerShellAppId"))
ok("PowerShellCertificateThumbprint", lf.get("PowerShellCertificateThumbprint"))
pfx = lf.get("PowerShellCertificatePath") or ""
ok("PowerShellCertificatePath file exists", pfx if pfx and os.path.isfile(pfx) else "")
ok("PowerShellExchangeOrganization", lf.get("PowerShellExchangeOrganization"))
ok("PowerShellDomainController", lf.get("PowerShellDomainController"))
stm = lf.get("ScriptTestMode")
print(f"  [INFO] ScriptTestMode={stm!r} (true = accounts not disabled)")

print("")
print("Graph mail:")
g = cfg.get("Graph") or {}
print(f"  [INFO] Graph:EmailTestMode={g.get('EmailTestMode')!r}")

if not azure.get("TenantId"):
    print("")
    print("→ Still in development bypass until AzureAd:TenantId is set and the service is restarted.")
PY

if [[ -f /etc/systemd/system/leaversform.service.d/override.conf ]]; then
  echo ""
  echo "systemd override: present (PFX password env)"
else
  echo ""
  echo "MISSING: /etc/systemd/system/leaversform.service.d/override.conf (LeaversForm__PowerShellCertificatePassword)"
fi
