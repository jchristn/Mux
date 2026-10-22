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
mux -v

if [ "$(uname -s)" = Darwin ]; then
    REPO_ROOT="$ROOT_DIR"
    . "$ROOT_DIR/scripts/macos/app-bundle.sh"
    install_macos_app desktop
    install_macos_app agent
fi
