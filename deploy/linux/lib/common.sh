#!/usr/bin/env bash
# Shared helpers for Linux deploy scripts. Source from deploy/linux/*.sh only.

find_dotnet() {
  if [[ -n "${DOTNET_ROOT:-}" && -x "${DOTNET_ROOT}/dotnet" ]]; then
    echo "${DOTNET_ROOT}/dotnet"
    return 0
  fi
  local candidate
  for candidate in /usr/share/dotnet/dotnet /usr/local/bin/dotnet /usr/lib/dotnet/dotnet /usr/bin/dotnet; do
    if [[ -x "$candidate" ]]; then
      echo "$candidate"
      return 0
    fi
  done
  command -v dotnet 2>/dev/null || true
}

resolve_dotnet_root() {
  local dotnet="$1"
  local sdk_dir
  sdk_dir="$("$dotnet" --list-sdks 2>/dev/null | awk '/^8\./ { gsub(/[\[\]]/, "", $2); print $2; exit }')"
  if [[ -n "$sdk_dir" && -x "${sdk_dir}/dotnet" ]]; then
    echo "${sdk_dir}/dotnet"
    return 0
  fi
  echo "$dotnet"
}

repo_root_from_script() {
  local here
  here="$(cd "$(dirname "${BASH_SOURCE[1]}")" && pwd)"
  cd "${here}/../.." && pwd
}
