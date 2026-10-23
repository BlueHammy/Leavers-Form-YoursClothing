#!/usr/bin/env bash
# Install or refresh leaversform.service and optional certificate password drop-in.
set -euo pipefail

# shellcheck source=lib/common.sh
source "$(dirname "$0")/lib/common.sh"

APP_PATH="${1:-/opt/leaversform/app}"
ASPNET_ENV="${2:-Development}"
BIND_ALL="${3:-0}" # 1 = listen on all interfaces (direct HTTP, no nginx TLS on same port)
BACKEND_PORT="${4:-5044}" # use 5045 when nginx terminates TLS on 5044

safe_source /etc/profile.d/dotnet.sh

DOTNET="$(find_dotnet)"
if [[ -z "$DOTNET" ]]; then
  echo "dotnet not found. Run deploy/linux/install-prerequisites.sh first." >&2
  exit 127
fi
DOTNET="$(resolve_dotnet_root "$DOTNET")"
DOTNET_ROOT="$(dirname "$DOTNET")"

if [[ "$BIND_ALL" == "1" ]]; then
  APP_URLS="http://0.0.0.0:${BACKEND_PORT}"
else
  APP_URLS="http://127.0.0.1:${BACKEND_PORT}"
fi

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

mkdir -p /etc/systemd/system/leaversform.service.d
if [[ ! -f /etc/systemd/system/leaversform.service.d/override.conf ]]; then
  cat > /etc/systemd/system/leaversform.service.d/override.conf <<'EOF'
[Service]
Environment=LeaversForm__PowerShellCertificatePassword=LeaversForm-Cert-Backup
EOF
  chmod 600 /etc/systemd/system/leaversform.service.d/override.conf
  echo "Created /etc/systemd/system/leaversform.service.d/override.conf (edit if your PFX password differs)."
fi

systemctl daemon-reload
systemctl enable leaversform
echo "Installed leaversform.service (ExecStart=${DOTNET} ...)"
