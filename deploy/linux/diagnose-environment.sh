#!/usr/bin/env bash
# Paste output when troubleshooting VM setup. No changes made.
set -u

echo "=== OS ==="
cat /etc/os-release 2>/dev/null || uname -a

echo ""
echo "=== Shell PATH ==="
echo "$PATH"

echo ""
echo "=== dotnet candidates ==="
for p in dotnet /usr/local/bin/dotnet /usr/bin/dotnet /usr/share/dotnet/dotnet "$HOME/.dotnet/dotnet" /root/.dotnet/dotnet; do
  if command -v "$p" >/dev/null 2>&1 || [[ -x "$p" ]]; then
    echo -n "$p: "
    "$p" --version 2>&1 || echo "(exists but failed)"
    "$p" --list-sdks 2>&1 || true
  fi
done

echo ""
echo "=== profile.d ==="
cat /etc/profile.d/dotnet.sh 2>/dev/null || echo "(no /etc/profile.d/dotnet.sh)"

echo ""
echo "=== pwsh ==="
command -v pwsh || echo "pwsh not found"
