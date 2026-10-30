#!/usr/bin/env bash
# Shared helpers for Linux deploy scripts. Source from deploy/linux/*.sh only.

# Ubuntu's Microsoft dotnet package ships /etc/profile.d/dotnet.sh, which can
# reference optional vars and break under bash nounset (set -u).
safe_source() {
  local file="$1"
  [[ -f "$file" ]] || return 0
  set +u
  # shellcheck disable=SC1090
  source "$file"
  set -u
}

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

# Real path to the dotnet host binary (avoid guessing /usr/lib/dotnet/dotnet when only /usr/bin/dotnet exists).
canonical_dotnet() {
  local dotnet="$1"
  if [[ ! -x "$dotnet" ]]; then
    return 1
  fi
  if command -v readlink >/dev/null 2>&1; then
    readlink -f "$dotnet"
    return 0
  fi
  echo "$dotnet"
}

repo_root_from_script() {
  local here
  here="$(cd "$(dirname "${BASH_SOURCE[1]}")" && pwd)"
  cd "${here}/../.." && pwd
}
