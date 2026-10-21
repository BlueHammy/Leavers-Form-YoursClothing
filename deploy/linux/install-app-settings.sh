#!/usr/bin/env bash
# Seed /etc/leaversform/secrets from repo examples without overwriting existing secrets.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SECRETS_DIR="/etc/leaversform/secrets"
APP_PATH="${1:-/opt/leaversform/app}"
MODE="${2:-development}" # development | production

mkdir -p "$SECRETS_DIR"
chmod 750 "$SECRETS_DIR"

case "$MODE" in
  development)
    example="${ROOT}/deploy/linux/appsettings.Development.local.json.example"
    dest_name="appsettings.Development.local.json"
    ;;
  production)
    example="${ROOT}/deploy/linux/appsettings.Production.local.json.example"
    dest_name="appsettings.Production.local.json"
    ;;
  *)
    echo "Unknown mode: $MODE (use development or production)" >&2
    exit 2
    ;;
esac

dest="${SECRETS_DIR}/${dest_name}"
app_dest="${APP_PATH}/${dest_name}"

if [[ -f "$dest" ]]; then
  echo "Keeping existing ${dest}"
else
  if [[ ! -f "$example" ]]; then
    echo "Example missing: $example" >&2
    exit 1
  fi
  cp -f "$example" "$dest"
  chown root:www-data "$dest"
  chmod 640 "$dest"
  echo "Created ${dest} from example — edit Entra IDs and secrets before restart."
fi

mkdir -p "$APP_PATH"
cp -f "$dest" "$app_dest"
chown root:www-data "$app_dest"
chmod 640 "$app_dest"
echo "Linked app settings at ${app_dest}"
