#!/usr/bin/env bash
# Linux equivalent of Final-Build.ps1.
#
# Test host (default): SQLite, script/email test mode, development sign-in.
#   sudo ./Final-Build.sh
#   Then open http://<this-machine>/
#
# Live host (Entra SSO, real offboarding — same job as Final-Build.ps1):
#   sudo ./Final-Build.sh --live \
#     --host-name leavers.internal.yoursclothing.co.uk \
#     --tenant-id 00000000-0000-0000-0000-000000000000 \
#     --client-id 00000000-0000-0000-0000-000000000000 \
#     --powershell-app-id 00000000-0000-0000-0000-000000000000 \
#     --sql-connection 'Server=SQL01;Database=LeaversForm;User Id=...;Password=...;Encrypt=True' \
#     --domain-controller dc01.yours.local
#
# Re-running publishes again and restarts the service. Existing local settings
# and the automation certificate are kept unless you pass --rewrite-config
# or --recreate-certificate.

set -euo pipefail

original_args=("$@")
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_PATH="/opt/leaversform/app"
SCRIPTS_DIR="/opt/leaversform/scripts"
SECRETS_DIR="/etc/leaversform/secrets"
LOG_DIR="/var/log/leaversform"
CHECKLIST_PATH="/opt/leaversform/FINAL-SETUP-CHECKLIST.txt"
PFX_PATH="${SECRETS_DIR}/automation.pfx"
CER_PATH="/opt/leaversform/LeaversForm-PowerShell-Automation.cer"
HOST_NAME=""
SQL_CONNECTION=""
TENANT_ID=""
CLIENT_ID=""
CLIENT_SECRET=""
POWERSHELL_APP_ID=""
DOMAIN_CONTROLLER=""
EXCHANGE_ORG="yoursclothing.onmicrosoft.com"
CERT_PASSWORD="LeaversForm-Cert-Backup"
LIVE=0
REWRITE_CONFIG=0
SKIP_NGINX=0
SKIP_MODULES=0
SKIP_TESTS=0
RECREATE_CERT=0

usage() {
  cat <<'EOF'
Usage: sudo ./Final-Build.sh [options]

Default (no --live) hosts a test site:
  SQLite database, ScriptTestMode and EmailTestMode on, development sign-in.
  Leaver accounts are not disabled.

Options:
  --live                     Production settings. Requires the Entra and SQL values below.
  --host-name NAME           Public host name (default: this machine's hostname)
  --app-path PATH            Publish directory (default: /opt/leaversform/app)
  --sql-connection STRING    ADO.NET connection string (SQLite if omitted in test mode)
  --tenant-id GUID           Web Entra tenant id
  --client-id GUID           Web Entra application (client) id
  --client-secret SECRET     Web Entra client secret (prompted for --live if omitted)
  --powershell-app-id GUID   Automation app id (not the web SSO app)
  --domain-controller HOST   On-prem domain controller
  --exchange-organization ORG
  --cert-password PASS       PFX password used when a certificate is created
  --recreate-certificate     Replace the automation certificate
  --rewrite-config           Replace the generated local settings file
  --skip-nginx               Do not configure nginx; listen on 0.0.0.0:5044
  --skip-modules             Do not install PowerShell modules
  --skip-tests               Do not run dotnet test before publish
  -h, --help                 Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --live) LIVE=1; shift ;;
    --rewrite-config) REWRITE_CONFIG=1; shift ;;
    --skip-nginx) SKIP_NGINX=1; shift ;;
    --skip-modules) SKIP_MODULES=1; shift ;;
    --skip-tests) SKIP_TESTS=1; shift ;;
    --recreate-certificate) RECREATE_CERT=1; shift ;;
    --host-name) HOST_NAME="${2:?--host-name needs a value}"; shift 2 ;;
    --app-path) APP_PATH="${2:?--app-path needs a value}"; shift 2 ;;
    --sql-connection) SQL_CONNECTION="${2:?--sql-connection needs a value}"; shift 2 ;;
    --tenant-id) TENANT_ID="${2:?--tenant-id needs a value}"; shift 2 ;;
    --client-id) CLIENT_ID="${2:?--client-id needs a value}"; shift 2 ;;
    --client-secret) CLIENT_SECRET="${2:?--client-secret needs a value}"; shift 2 ;;
    --powershell-app-id) POWERSHELL_APP_ID="${2:?--powershell-app-id needs a value}"; shift 2 ;;
    --domain-controller) DOMAIN_CONTROLLER="${2:?--domain-controller needs a value}"; shift 2 ;;
    --exchange-organization) EXCHANGE_ORG="${2:?--exchange-organization needs a value}"; shift 2 ;;
    --cert-password) CERT_PASSWORD="${2:?--cert-password needs a value}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

if [[ "${EUID}" -ne 0 ]]; then
  if [[ ${#original_args[@]} -eq 0 ]]; then
    exec sudo bash "$0"
  fi
  exec sudo bash "$0" "${original_args[@]}"
fi

step() { printf '\n==> %s\n' "$1"; }

if [[ -z "$HOST_NAME" ]]; then
  HOST_NAME="$(hostname -f 2>/dev/null || hostname)"
fi

if [[ "$LIVE" -eq 1 ]]; then
  if [[ -z "$CLIENT_SECRET" && -t 0 ]]; then
    read -rsp "Enter the LeaversForm web Entra client secret: " CLIENT_SECRET
    echo
  fi
  missing=()
  [[ -z "$TENANT_ID" ]] && missing+=(--tenant-id)
  [[ -z "$CLIENT_ID" ]] && missing+=(--client-id)
  [[ -z "$CLIENT_SECRET" ]] && missing+=(--client-secret)
  [[ -z "$POWERSHELL_APP_ID" ]] && missing+=(--powershell-app-id)
  [[ -z "$SQL_CONNECTION" ]] && missing+=(--sql-connection)
  [[ -z "$DOMAIN_CONTROLLER" ]] && missing+=(--domain-controller)
  if [[ ${#missing[@]} -gt 0 ]]; then
    echo "--live requires: ${missing[*]}" >&2
    exit 2
  fi
  ASPNET_ENV="Production"
  SETTINGS_NAME="appsettings.Production.local.json"
  if [[ "$SKIP_NGINX" -eq 1 ]]; then
    BASE_URL="https://${HOST_NAME}:5044"
  else
    BASE_URL="https://${HOST_NAME}"
  fi
else
  ASPNET_ENV="Development"
  SETTINGS_NAME="appsettings.Development.local.json"
  if [[ -z "$SQL_CONNECTION" ]]; then
    SQL_CONNECTION="Data Source=leaversform.db"
  fi
  if [[ "$SKIP_NGINX" -eq 1 ]]; then
    BASE_URL="http://${HOST_NAME}:5044"
  else
    BASE_URL="http://${HOST_NAME}"
  fi
fi

SETTINGS_PATH="${SECRETS_DIR}/${SETTINGS_NAME}"

step "Installing prerequisites (.NET 8 SDK, nginx, pwsh)"
bash "${ROOT}/deploy/linux/install-prerequisites.sh"

if [[ -f /etc/profile.d/dotnet.sh ]]; then
  # shellcheck disable=SC1091
  source /etc/profile.d/dotnet.sh
fi

find_dotnet() {
  if [[ -n "${DOTNET_ROOT:-}" && -x "${DOTNET_ROOT}/dotnet" ]]; then
    echo "${DOTNET_ROOT}/dotnet"
    return 0
  fi
  local candidate
  for candidate in /usr/share/dotnet/dotnet /usr/local/bin/dotnet /usr/bin/dotnet; do
    if [[ -x "$candidate" ]]; then
      echo "$candidate"
      return 0
    fi
  done
  command -v dotnet
}

DOTNET="$(find_dotnet)"
if ! "$DOTNET" --list-sdks 2>/dev/null | grep -q '^8\.'; then
  echo ".NET 8 SDK was not found after install-prerequisites.sh" >&2
  exit 1
fi
sdk_dir="$("$DOTNET" --list-sdks 2>/dev/null | awk '/^8\./ { gsub(/[\[\]]/, "", $2); print $2; exit }')"
DOTNET_ROOT="$(dirname "$sdk_dir")"
if [[ -x "${DOTNET_ROOT}/dotnet" ]]; then
  DOTNET="${DOTNET_ROOT}/dotnet"
fi

if ! command -v python3 >/dev/null 2>&1; then
  apt-get update
  apt-get install -y python3
fi
if ! command -v openssl >/dev/null 2>&1; then
  apt-get update
  apt-get install -y openssl
fi

if [[ "$SKIP_TESTS" -eq 0 ]]; then
  step "Running tests"
  "$DOTNET" test "${ROOT}/LeaversForm.sln" --nologo
fi

step "Publishing LeaversForm.Web to ${APP_PATH}"
bash "${ROOT}/deploy/linux/publish-local.sh" "$APP_PATH"
mkdir -p "$SCRIPTS_DIR" "$LOG_DIR" "$SECRETS_DIR" /opt/leaversform
chmod 750 "$SECRETS_DIR"
if [[ -f "${APP_PATH}/LeaverProcess.ps1" ]]; then
  cp -f "${APP_PATH}/LeaverProcess.ps1" "${SCRIPTS_DIR}/LeaverProcess.ps1"
elif [[ -f "${ROOT}/scripts/LeaverProcess.ps1" ]]; then
  cp -f "${ROOT}/scripts/LeaverProcess.ps1" "${SCRIPTS_DIR}/LeaverProcess.ps1"
fi

step "Creating the PowerShell automation certificate"
if [[ "$RECREATE_CERT" -eq 1 ]]; then
  rm -f "$PFX_PATH" "$CER_PATH"
fi
THUMBPRINT=""
if [[ ! -f "$PFX_PATH" ]]; then
  cert_tmp="$(mktemp -d)"
  trap 'rm -rf "$cert_tmp"' EXIT
  openssl req -x509 -newkey rsa:2048 -sha256 -days 730 -nodes \
    -keyout "${cert_tmp}/key.pem" \
    -out "${cert_tmp}/cert.pem" \
    -subj "/CN=LeaversForm PowerShell Automation" >/dev/null 2>&1
  openssl pkcs12 -export \
    -out "$PFX_PATH" \
    -inkey "${cert_tmp}/key.pem" \
    -in "${cert_tmp}/cert.pem" \
    -password "pass:${CERT_PASSWORD}"
  openssl x509 -in "${cert_tmp}/cert.pem" -outform DER -out "$CER_PATH"
  chmod 640 "$PFX_PATH"
  chmod 644 "$CER_PATH"
  fingerprint="$(openssl x509 -in "${cert_tmp}/cert.pem" -noout -fingerprint -sha1)"
  THUMBPRINT="${fingerprint#*=}"
  THUMBPRINT="${THUMBPRINT//:/}"
  rm -rf "$cert_tmp"
  trap - EXIT
  mkdir -p /etc/systemd/system/leaversform.service.d
  password_escaped="${CERT_PASSWORD//%/%%}"
  cat > /etc/systemd/system/leaversform.service.d/override.conf <<EOF
[Service]
Environment=LeaversForm__PowerShellCertificatePassword=${password_escaped}
EOF
  chmod 600 /etc/systemd/system/leaversform.service.d/override.conf
  echo "Created ${PFX_PATH}"
  echo "Public certificate for Entra upload: ${CER_PATH}"
  echo "Thumbprint: ${THUMBPRINT}"
else
  echo "Keeping existing ${PFX_PATH}"
  if [[ -z "$THUMBPRINT" ]]; then
    THUMBPRINT="$(openssl pkcs12 -in "$PFX_PATH" -nokeys -passin "pass:${CERT_PASSWORD}" 2>/dev/null | openssl x509 -noout -fingerprint -sha1 | sed 's/.*=//;s/://g' || true)"
  fi
fi

if [[ -f "$SETTINGS_PATH" && "$REWRITE_CONFIG" -eq 0 ]]; then
  step "Keeping existing ${SETTINGS_PATH} (pass --rewrite-config to replace it)"
else
  step "Writing ${SETTINGS_PATH}"
  LF_LIVE="$LIVE" \
  LF_HOST_NAME="$HOST_NAME" \
  LF_BASE_URL="$BASE_URL" \
  LF_SQL_CONNECTION="$SQL_CONNECTION" \
  LF_TENANT_ID="$TENANT_ID" \
  LF_CLIENT_ID="$CLIENT_ID" \
  LF_CLIENT_SECRET="$CLIENT_SECRET" \
  LF_PS_APP_ID="$POWERSHELL_APP_ID" \
  LF_EXCHANGE_ORG="$EXCHANGE_ORG" \
  LF_DC="$DOMAIN_CONTROLLER" \
  LF_THUMBPRINT="$THUMBPRINT" \
  LF_PFX="$PFX_PATH" \
  python3 - "$SETTINGS_PATH" <<'PY'
import json, os, sys
dest = sys.argv[1]
live = os.environ["LF_LIVE"] == "1"
cfg = {
    "AllowedHosts": os.environ["LF_HOST_NAME"] + ";localhost;127.0.0.1" if live else "*",
    "AzureAd": {
        "TenantId": os.environ.get("LF_TENANT_ID", ""),
        "ClientId": os.environ.get("LF_CLIENT_ID", ""),
        "ClientSecret": os.environ.get("LF_CLIENT_SECRET", ""),
    },
    "ConnectionStrings": {"DefaultConnection": os.environ["LF_SQL_CONNECTION"]},
    "Graph": {"EmailTestMode": not live, "RedirectAllEmailsTo": ""},
    "LeaversForm": {
        "BaseUrl": os.environ["LF_BASE_URL"],
        "EmailTestMode": not live,
        "ScriptTestMode": not live,
        "PowerShellCertificateThumbprint": os.environ.get("LF_THUMBPRINT", ""),
        "PowerShellCertificatePath": os.environ.get("LF_PFX", ""),
        "PowerShellTenantId": os.environ.get("LF_TENANT_ID", ""),
        "PowerShellAppId": os.environ.get("LF_PS_APP_ID", ""),
        "PowerShellExchangeOrganization": os.environ.get("LF_EXCHANGE_ORG", ""),
        "PowerShellDomainController": os.environ.get("LF_DC", ""),
        "LogFolderPath": "/var/log/leaversform",
    },
}
with open(dest, "w", encoding="utf-8") as handle:
    json.dump(cfg, handle, indent=2)
    handle.write("\n")
os.chmod(dest, 0o640)
PY
fi
cp -f "$SETTINGS_PATH" "${APP_PATH}/${SETTINGS_NAME}"

if [[ "$LIVE" -eq 1 && "$SKIP_MODULES" -eq 0 && -x "$(command -v pwsh || true)" ]]; then
  step "Installing PowerShell modules for LeaverProcess.ps1"
  if ! pwsh -NoProfile -NonInteractive -Command '
    $ErrorActionPreference = "Stop"
    if (-not (Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue)) {
      Register-PSRepository -Default -ErrorAction SilentlyContinue
    }
    Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
    $modules = @(
      "ExchangeOnlineManagement",
      "Microsoft.Graph.Authentication",
      "Microsoft.Graph.Users",
      "Microsoft.Graph.Users.Actions",
      "Microsoft.Graph.Groups",
      "Microsoft.Graph.Identity.DirectoryManagement",
      "Microsoft.Graph.Applications"
    )
    foreach ($module in $modules) {
      if (-not (Get-Module -ListAvailable -Name $module)) {
        Install-Module $module -Scope AllUsers -Force -AllowClobber
        Write-Host "Installed $module"
      } else {
        Write-Host "$module already installed"
      }
    }
  '; then
    echo "PowerShell module install failed. Re-run later or pass --skip-modules." >&2
  fi
elif [[ "$LIVE" -eq 1 && "$SKIP_MODULES" -eq 0 ]]; then
  echo "pwsh is not installed, so PowerShell modules were skipped." >&2
fi

if ! getent passwd www-data >/dev/null; then
  useradd --system --create-home --shell /usr/sbin/nologin www-data
fi

if [[ "$SKIP_NGINX" -eq 1 ]]; then
  APP_URLS="http://0.0.0.0:5044"
else
  APP_URLS="http://127.0.0.1:5044"
fi

step "Installing the systemd service"
cat > /etc/systemd/system/leaversform.service <<EOF
[Unit]
Description=LeaversForm (ASP.NET Core)
After=network.target

[Service]
WorkingDirectory=${APP_PATH}
ExecStart=${DOTNET} ${APP_PATH}/LeaversForm.Web.dll
Restart=always
RestartSec=10
User=www-data
Group=www-data
Environment=ASPNETCORE_ENVIRONMENT=${ASPNET_ENV}
Environment=ASPNETCORE_URLS=${APP_URLS}
Environment=DOTNET_ROOT=${DOTNET_ROOT}
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=DOTNET_NOLOGO=1

[Install]
WantedBy=multi-user.target
EOF

chown -R www-data:www-data "$APP_PATH" "$LOG_DIR"
chown root:www-data "$SECRETS_DIR" "$PFX_PATH" "$SETTINGS_PATH" "${APP_PATH}/${SETTINGS_NAME}" 2>/dev/null || true
chmod 750 "$APP_PATH" "$LOG_DIR" "$SECRETS_DIR"
chmod 640 "$SETTINGS_PATH" "${APP_PATH}/${SETTINGS_NAME}"
if [[ -f "$PFX_PATH" ]]; then
  chmod 640 "$PFX_PATH"
  chown root:www-data "$PFX_PATH"
fi

if [[ "$SKIP_NGINX" -eq 0 ]] && command -v nginx >/dev/null 2>&1; then
  step "Configuring nginx"
  nginx_conf="$(mktemp)"
  cat > "$nginx_conf" <<EOF
server {
    listen 80;
    server_name ${HOST_NAME};

    location / {
        proxy_pass http://127.0.0.1:5044;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host \$host;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache_bypass \$http_upgrade;
    }
}
EOF
  if grep -q 'sites-enabled' /etc/nginx/nginx.conf 2>/dev/null; then
    mkdir -p /etc/nginx/sites-available /etc/nginx/sites-enabled
    cp -f "$nginx_conf" /etc/nginx/sites-available/leaversform
    ln -sfn /etc/nginx/sites-available/leaversform /etc/nginx/sites-enabled/leaversform
    rm -f /etc/nginx/sites-enabled/default
  else
    mkdir -p /etc/nginx/conf.d
    cp -f "$nginx_conf" /etc/nginx/conf.d/leaversform.conf
  fi
  rm -f "$nginx_conf"
  nginx -t
  systemctl enable nginx
  systemctl reload nginx || systemctl restart nginx
else
  step "nginx skipped; the app listens on ${APP_URLS}"
fi

step "Starting leaversform"
systemctl daemon-reload
systemctl enable leaversform
systemctl restart leaversform

ready=0
http_code=""
for _ in $(seq 1 40); do
  http_code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 -H "Host: ${HOST_NAME}" "http://127.0.0.1:5044/" || true)"
  if [[ "$http_code" =~ ^(200|301|302|307|308)$ ]]; then
    ready=1
    break
  fi
  sleep 1
done

if [[ "$LIVE" -eq 1 ]]; then
  mode_line="LIVE — ScriptTestMode and EmailTestMode are off. Real leavers can be processed."
else
  mode_line="TEST — ScriptTestMode and EmailTestMode are on. Accounts are not disabled."
fi

cat > "$CHECKLIST_PATH" <<EOF
LeaversForm Linux setup
=======================
Generated: $(date -u +"%Y-%m-%dT%H:%M:%SZ")

${mode_line}

Site: ${BASE_URL}
App path: ${APP_PATH}
Environment: ${ASPNET_ENV}
Settings: ${SETTINGS_PATH}
Logs: ${LOG_DIR}
Automation certificate: ${CER_PATH}
PFX: ${PFX_PATH}
Thumbprint: ${THUMBPRINT:-unknown}

COMPLETED BY THIS SCRIPT
------------------------
- .NET 8 SDK, nginx, and pwsh installed where the package manager allowed it
- LeaversForm.Web published to ${APP_PATH}
- systemd service leaversform enabled
- Local settings written (unless an existing file was kept)

BEFORE REAL LEAVERS (--live)
----------------------------
[ ] Upload ${CER_PATH} to the PowerShell automation Entra app
[ ] Grant Graph and Exchange application permissions and admin consent
[ ] Confirm the PFX password is only in /etc/systemd/system/leaversform.service.d/override.conf
[ ] Put a trusted TLS certificate in front of nginx for ${BASE_URL}
[ ] Confirm SQL connectivity and that migrations applied on first start
[ ] ActiveDirectory / on-prem disable still needs a reachable domain controller
[ ] Do not run LeaversForm.Runner beside this service (the web app already processes leavers)

HTTP check: ${http_code:-none}
EOF

if [[ "$ready" -eq 1 ]]; then
  echo
  echo "LINUX HOST READY"
  echo "${mode_line}"
  echo "Open: ${BASE_URL}"
  echo "Checklist: ${CHECKLIST_PATH}"
  if [[ -n "$THUMBPRINT" ]]; then
    echo "Certificate thumbprint: ${THUMBPRINT}"
  fi
else
  echo "The service did not answer on http://127.0.0.1:5044/ (last HTTP status: ${http_code:-none})." >&2
  systemctl status leaversform --no-pager || true
  journalctl -u leaversform -n 40 --no-pager || true
  exit 1
fi
