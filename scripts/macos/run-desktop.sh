#!/usr/bin/env bash
# Launch the mux desktop application (Avalonia).
# Usage: ./scripts/macos/run-desktop.sh [net8.0|net10.0]
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
. "$SCRIPT_DIR/../common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || { echo "Unexpected argument: $1" >&2; exit 1; }
if [ "$(uname -s)" = Darwin ]; then
    . "$SCRIPT_DIR/app-bundle.sh"
    install_macos_app desktop
    open -a "$HOME/Applications/Mux.app"
else
    dotnet run --project "$REPO_ROOT/src/Mux.Desktop/Mux.Desktop.csproj" --framework "$TFM" -p:TargetFrameworks="$TFM"
fi
