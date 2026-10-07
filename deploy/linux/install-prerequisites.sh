#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID:-}" -ne 0 ]]; then
  echo "Run with sudo: sudo $0"
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y curl nginx git

if ! command -v dotnet >/dev/null 2>&1; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore
  echo 'export DOTNET_ROOT=$HOME/.dotnet' >> /etc/profile.d/dotnet.sh
  echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools' >> /etc/profile.d/dotnet.sh
fi

if ! command -v pwsh >/dev/null 2>&1; then
  curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft.gpg
  echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/ubuntu/22.04/prod jammy main" \
    > /etc/apt/sources.list.d/microsoft-prod.list
  apt-get update
  apt-get install -y powershell
fi

mkdir -p /opt/leaversform/app /opt/leaversform/scripts /var/log/leaversform /etc/leaversform/secrets
chmod 750 /etc/leaversform/secrets

echo "Prerequisites installed. Next: docs/LINUX-DEPLOYMENT.md"
