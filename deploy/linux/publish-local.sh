#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${1:-/opt/leaversform/app}"

find_dotnet() {
  if [[ -n "${DOTNET_ROOT:-}" && -x "${DOTNET_ROOT}/dotnet" ]]; then
    echo "${DOTNET_ROOT}/dotnet"
    return 0
  fi
  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return 0
  fi
  for candidate in \
    /usr/local/bin/dotnet \
    /usr/bin/dotnet \
    /usr/share/dotnet/dotnet \
    "$HOME/.dotnet/dotnet" \
    /root/.dotnet/dotnet; do
    if [[ -x "$candidate" ]]; then
      echo "$candidate"
      return 0
    fi
  done
  return 1
}

DOTNET="$(find_dotnet)" || {
  echo "dotnet not found. Publishing requires the .NET 8 SDK (not runtime only)."
  echo "On the VM run:  sudo ./deploy/linux/install-prerequisites.sh"
  echo "Then open a new shell or run:  export PATH=\$PATH:/usr/bin"
  exit 127
}

if ! "$DOTNET" --list-sdks 2>/dev/null | grep -q '^8\.'; then
  echo "No .NET 8 SDK found (publish needs SDK). Run: sudo ./deploy/linux/install-prerequisites.sh"
  "$DOTNET" --list-sdks 2>/dev/null || true
  exit 127
fi

echo "Publishing LeaversForm.Web to $OUT ..."
echo "Using: $DOTNET ($("$DOTNET" --version))"

# Stale DLLs left from older publishes cause MissingMethodException at runtime.
preserve_tmp="$(mktemp -d)"
for keep in appsettings.Development.local.json appsettings.Production.local.json leaversform.db; do
  if [[ -f "${OUT}/${keep}" ]]; then
    cp -a "${OUT}/${keep}" "${preserve_tmp}/${keep}"
  fi
done
if [[ -d "$OUT" ]]; then
  rm -rf "${OUT:?}"/*
fi
mkdir -p "$OUT"

"$DOTNET" publish "$ROOT/LeaversForm.Web/LeaversForm.Web.csproj" -c Release -o "$OUT" --no-incremental

for keep in appsettings.Development.local.json appsettings.Production.local.json leaversform.db; do
  if [[ -f "${preserve_tmp}/${keep}" ]]; then
    cp -a "${preserve_tmp}/${keep}" "${OUT}/${keep}"
  fi
done
rm -rf "$preserve_tmp"

if [[ -w /opt/leaversform/scripts ]] || [[ "${EUID:-}" -eq 0 ]]; then
  bash "$(dirname "$0")/sync-scripts.sh" /opt/leaversform/scripts 2>/dev/null || {
    SCRIPTS_DIR="/opt/leaversform/scripts"
    mkdir -p "$SCRIPTS_DIR"
    cp -f "$OUT/LeaverProcess.ps1" "$SCRIPTS_DIR/" 2>/dev/null || cp -f "$ROOT/scripts/LeaverProcess.ps1" "$SCRIPTS_DIR/"
    cp -f "$OUT/LeaversForm-ModuleBootstrap.ps1" "$SCRIPTS_DIR/" 2>/dev/null || cp -f "$ROOT/scripts/LeaversForm-ModuleBootstrap.ps1" "$SCRIPTS_DIR/"
  }
else
  echo "Note: run as root to sync /opt/leaversform/scripts (deploy/linux/sync-scripts.sh)"
fi

echo "Done. Restart: sudo systemctl restart leaversform"
