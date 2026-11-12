#!/usr/bin/env bash
# Installs .NET 8 SDK (for publish), ASP.NET runtime, nginx, git, and optionally pwsh.
# Works on most Linux VMs (apt optional). Run: sudo ./deploy/linux/install-prerequisites.sh
set -uo pipefail

DOTNET_DIR="/usr/share/dotnet"
DOTNET_BIN="/usr/local/bin/dotnet"

if [[ "${EUID:-}" -ne 0 ]]; then
  echo "Run with sudo: sudo $0"
  exit 1
fi

log() { echo "[install-prerequisites] $*"; }

has_sdk_8() {
  local dotnet="$1"
  [[ -x "$dotnet" ]] && "$dotnet" --list-sdks 2>/dev/null | grep -q '^8\.'
}

install_base_packages() {
  if command -v apt-get >/dev/null 2>&1; then
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y curl ca-certificates git nginx gnupg wget || true
  elif command -v dnf >/dev/null 2>&1; then
    dnf install -y curl ca-certificates git nginx wget || true
  elif command -v yum >/dev/null 2>&1; then
    yum install -y curl ca-certificates git nginx wget || true
  else
    log "No apt-get/dnf/yum — ensure curl and git are installed."
  fi
}

install_dotnet_via_microsoft_script() {
  log "Installing .NET 8 SDK to $DOTNET_DIR (official install script)..."
  local script="/tmp/dotnet-install.sh"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$script"
  chmod +x "$script"

  # SDK (required for dotnet publish on the VM)
  bash "$script" --channel 8.0 --install-dir "$DOTNET_DIR"
  # ASP.NET runtime (required to run LeaversForm.Web.dll)
  bash "$script" --channel 8.0 --runtime aspnetcore --install-dir "$DOTNET_DIR"

  ln -sf "$DOTNET_DIR/dotnet" "$DOTNET_BIN"
  cat > /etc/profile.d/dotnet.sh <<EOF
export DOTNET_ROOT=$DOTNET_DIR
export PATH="\$PATH:\$DOTNET_ROOT:\$DOTNET_ROOT/tools"
EOF
  chmod 644 /etc/profile.d/dotnet.sh
  export DOTNET_ROOT="$DOTNET_DIR"
  export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"
}

try_install_dotnet_apt() {
  command -v apt-get >/dev/null 2>&1 || return 1

  local ubuntu_version=""
  if [[ -f /etc/os-release ]]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    ubuntu_version="${VERSION_ID:-22.04}"
  fi
  [[ -z "$ubuntu_version" ]] && ubuntu_version="22.04"

  log "Trying apt package dotnet-sdk-8.0 (Ubuntu/Debian, version hint $ubuntu_version)..."
  local pkg_deb="/tmp/packages-microsoft-prod.deb"
  if ! curl -fsSL "https://packages.microsoft.com/config/ubuntu/${ubuntu_version}/packages-microsoft-prod.deb" -o "$pkg_deb"; then
    curl -fsSL "https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb" -o "$pkg_deb" || return 1
  fi
  dpkg -i "$pkg_deb" 2>/dev/null || apt-get install -f -y
  apt-get update
  apt-get install -y dotnet-sdk-8.0 aspnetcore-runtime-8.0
}

install_powershell_apt() {
  command -v apt-get >/dev/null 2>&1 || return 0
  command -v pwsh >/dev/null 2>&1 && return 0

  log "Installing PowerShell (pwsh)..."
  if [[ ! -f /usr/share/keyrings/microsoft.gpg ]]; then
    curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft.gpg
    echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/ubuntu/22.04/prod jammy main" \
      > /etc/apt/sources.list.d/microsoft-prod.list
    apt-get update || true
  fi
  apt-get install -y powershell || log "pwsh install skipped or failed — install manually if you need LeaverProcess.ps1"
}

install_base_packages

if has_sdk_8 "$(command -v dotnet 2>/dev/null || true)"; then
  log "dotnet SDK 8.x already present: $(command -v dotnet)"
elif has_sdk_8 "$DOTNET_BIN"; then
  log "dotnet SDK 8.x already present: $DOTNET_BIN"
elif try_install_dotnet_apt && has_sdk_8 "$(command -v dotnet 2>/dev/null || true)"; then
  log "Installed via apt: $(command -v dotnet)"
else
  log "apt install unavailable or failed — using Microsoft install script."
  install_dotnet_via_microsoft_script
fi

install_powershell_apt

mkdir -p /opt/leaversform/app /opt/leaversform/scripts /var/log/leaversform /etc/leaversform/secrets
chmod 750 /etc/leaversform/secrets

# Verify
VERIFY="$DOTNET_BIN"
if ! has_sdk_8 "$VERIFY" && command -v dotnet >/dev/null 2>&1; then
  VERIFY="$(command -v dotnet)"
fi

echo ""
if has_sdk_8 "$VERIFY"; then
  log "SUCCESS: $VERIFY — $("$VERIFY" --version)"
  "$VERIFY" --list-sdks
  log "Publish with: sudo ./deploy/linux/publish-local.sh /opt/leaversform/app"
  log "If 'dotnet' still missing in your shell, run:  source /etc/profile.d/dotnet.sh"
  exit 0
fi

log "FAILED: .NET 8 SDK not found after install."
log "Run diagnostics: ./deploy/linux/diagnose-environment.sh"
log "Or manually: curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir $DOTNET_DIR"
exit 1
