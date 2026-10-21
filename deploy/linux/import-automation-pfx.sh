#!/usr/bin/env bash
# Import automation PFX into LocalMachine\My so pwsh LeaverProcess.ps1 can use it (optional;
# the script also loads LEAVERSFORM_PS_CERTIFICATE_PATH directly when set).
set -euo pipefail

PFX_PATH="${1:-/etc/leaversform/secrets/automation.pfx}"
CERT_PASSWORD="${2:-LeaversForm-Cert-Backup}"

if [[ ! -f "$PFX_PATH" ]]; then
  echo "PFX not found: $PFX_PATH" >&2
  exit 1
fi

if ! command -v pwsh >/dev/null 2>&1; then
  echo "pwsh is not installed." >&2
  exit 1
fi

pwsh -NoProfile -Command "
  \$ErrorActionPreference = 'Stop'
  \$pwd = ConvertTo-SecureString '${CERT_PASSWORD}' -AsPlainText -Force
  \$cert = Import-PfxCertificate -FilePath '${PFX_PATH}' -Password \$pwd -CertStoreLocation 'Cert:\\LocalMachine\\My' -Exportable
  Write-Output \"Imported thumbprint: \$(\$cert.Thumbprint)\"
"
