#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID:-}" -ne 0 ]]; then
  echo "Run with sudo: sudo $0"
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y curl nginx git ca-certificates gnupg

install_dotnet_apt() {
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
    echo "dotnet SDK 8.x already installed: $(command -v dotnet)"
    return 0
  fi

  local pkg_deb="/tmp/packages-microsoft-prod.deb"
  curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -o "$pkg_deb"
  dpkg -i "$pkg_deb" || apt-get install -f -y
  apt-get update
  apt-get install -y dotnet-sdk-8.0 aspnetcore-runtime-8.0
  echo "Installed: $(dotnet --version)"
}

install_dotnet_apt

if ! command -v pwsh >/dev/null 2>&1; then
  if [[ ! -f /usr/share/keyrings/microsoft.gpg ]]; then
    curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft.gpg
    echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/ubuntu/22.04/prod jammy main" \
      > /etc/apt/sources.list.d/microsoft-prod.list
    apt-get update
  fi
  apt-get install -y powershell
fi

mkdir -p /opt/leaversform/app /opt/leaversform/scripts /var/log/leaversform /etc/leaversform/secrets
chmod 750 /etc/leaversform/secrets

echo ""
echo "Prerequisites OK. dotnet=$(command -v dotnet) pwsh=$(command -v pwsh || echo n/a)"
echo "Next: git pull && ./deploy/linux/publish-local.sh /opt/leaversform/app"
echo "See docs/LINUX-DEPLOYMENT.md"
