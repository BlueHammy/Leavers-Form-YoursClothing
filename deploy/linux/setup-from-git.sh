#!/usr/bin/env bash
# After git checkout on the VM: publish the app, sync scripts, systemd, and settings templates.
#
#   cd ~/Source/Leavers-Form-YoursClothing
#   git fetch origin && git checkout bluehammy/linux-final-build-fb33
#   sudo bash deploy/linux/setup-from-git.sh
#
# Does not overwrite /etc/leaversform/secrets/*.json if they already exist.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_PATH="/opt/leaversform/app"
SCRIPTS_DIR="/opt/leaversform/scripts"
LOG_DIR="/var/log/leaversform"
SECRETS_DIR="/etc/leaversform/secrets"
SKIP_PREREQ=0
SKIP_TESTS=0
SKIP_NGINX=0
BIND_LAN=0
IMPORT_PFX=1

usage() {
  cat <<'EOF'
Usage: sudo bash deploy/linux/setup-from-git.sh [options]

Publishes LeaversForm.Web to /opt/leaversform/app, syncs PowerShell scripts,
installs systemd (Development test host), and seeds settings from examples.

Options:
  --skip-prerequisites   Do not run install-prerequisites.sh
  --skip-tests           Do not run dotnet test before publish
  --skip-nginx           Do not configure nginx
  --bind-lan             Listen on 0.0.0.0:5044 (for http://hostname:5044/)
  --no-import-pfx        Skip import-automation-pfx.sh
  -h, --help             Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-prerequisites) SKIP_PREREQ=1; shift ;;
    --skip-tests) SKIP_TESTS=1; shift ;;
    --skip-nginx) SKIP_NGINX=1; shift ;;
    --bind-lan) BIND_LAN=1; shift ;;
    --no-import-pfx) IMPORT_PFX=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

if [[ "${EUID}" -ne 0 ]]; then
  exec sudo bash "$0" "$@"
fi

step() { printf '\n==> %s\n' "$1"; }

HOST_NAME="$(hostname -f 2>/dev/null || hostname)"

# shellcheck source=lib/common.sh
source "${ROOT}/deploy/linux/lib/common.sh"

if [[ "$SKIP_PREREQ" -eq 0 ]]; then
  step "Prerequisites"
  bash "${ROOT}/deploy/linux/install-prerequisites.sh"
fi

safe_source /etc/profile.d/dotnet.sh
DOTNET="$(find_dotnet)"
if [[ -z "$DOTNET" ]] || ! "$DOTNET" --list-sdks 2>/dev/null | grep -q '^8\.'; then
  echo ".NET 8 SDK required. Run without --skip-prerequisites or install-prerequisites.sh." >&2
  exit 1
fi

if [[ "$SKIP_TESTS" -eq 0 ]]; then
  step "Tests"
  "$DOTNET" test "${ROOT}/LeaversForm.sln" --nologo
fi

step "Publish"
bash "${ROOT}/deploy/linux/publish-local.sh" "$APP_PATH"

step "Scripts and directories"
bash "${ROOT}/deploy/linux/sync-scripts.sh" "$SCRIPTS_DIR"
mkdir -p /opt/leaversform/logs "$LOG_DIR" "$SECRETS_DIR"
chmod 750 "$SECRETS_DIR"
chown -R www-data:www-data /opt/leaversform/logs "$SCRIPTS_DIR"
chown www-data:www-data "$APP_PATH"
chmod 750 "$APP_PATH"

step "App settings (examples only if missing)"
bash "${ROOT}/deploy/linux/install-app-settings.sh" "$APP_PATH" development

step "systemd"
bash "${ROOT}/deploy/linux/install-systemd.sh" "$APP_PATH" Development "$BIND_LAN"

if [[ "$IMPORT_PFX" -eq 1 ]]; then
  for pfx in "${SECRETS_DIR}/automation.pfx" "${SECRETS_DIR}/automation-new.pfx"; do
    if [[ -f "$pfx" ]]; then
      step "Import PFX for pwsh: $pfx"
      bash "${ROOT}/deploy/linux/import-automation-pfx.sh" "$pfx" || echo "PFX import failed (LeaverProcess can still use LEAVERSFORM_PS_CERTIFICATE_PATH)." >&2
      break
    fi
  done
fi

if [[ "$SKIP_NGINX" -eq 0 ]] && command -v nginx >/dev/null 2>&1; then
  step "nginx"
  nginx_conf="$(mktemp)"
  sed "s/__HOST_NAME__/${HOST_NAME}/g" "${ROOT}/deploy/linux/nginx-leaversform.conf" > "$nginx_conf"
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
fi

step "Start leaversform"
systemctl restart leaversform
sleep 2
systemctl is-active leaversform || true

cat <<EOF

Setup from git finished.

  Edit secrets (if new):  sudo nano ${SECRETS_DIR}/appsettings.Development.local.json
  Then:                   sudo cp ${SECRETS_DIR}/appsettings.Development.local.json ${APP_PATH}/
                          sudo systemctl restart leaversform

  Test HTTP:  curl -fsSL http://127.0.0.1:5044/ | grep -E 'Leaver requests|Development auth bypass'
  Entra guide: docs/LINUX-ENTRA-SETUP.md

EOF
