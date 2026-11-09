#!/usr/bin/env bash
set -e

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
PACKAGE_SOURCE="$ROOT_DIR/artifacts/tool-packages"
cd "$ROOT_DIR"

. "$ROOT_DIR/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
FRAMEWORK="$TFM"
[ "$#" -eq 0 ] || { echo "Unexpected argument: $1" >&2; exit 1; }

echo "Removing mux..."
dotnet tool uninstall -g Mux.Cli 2>/dev/null || true
rm -rf "$PACKAGE_SOURCE"
mkdir -p "$PACKAGE_SOURCE"
echo "Building mux for $FRAMEWORK..."
dotnet pack src/Mux.Cli/Mux.Cli.csproj --configuration Release -p:TargetFrameworks="$FRAMEWORK" --output "$PACKAGE_SOURCE"
echo "Installing mux..."
dotnet tool install -g --source "$PACKAGE_SOURCE" --framework "$FRAMEWORK" --disable-parallel Mux.Cli
# dotnet tool install puts mux in ~/.dotnet/tools, which is often not on PATH yet.
if command -v mux >/dev/null 2>&1; then
    mux -v
elif [ -x "$HOME/.dotnet/tools/mux" ]; then
    "$HOME/.dotnet/tools/mux" -v
    echo "Note: $HOME/.dotnet/tools is not on PATH, so new shells will not find 'mux'." >&2
    echo "Add this line to your shell profile (~/.profile, ~/.bashrc or ~/.zprofile):" >&2
    echo '  export PATH="$PATH:$HOME/.dotnet/tools"' >&2
else
    echo "mux was installed, but it is not on PATH or in $HOME/.dotnet/tools." >&2
    exit 1
fi
