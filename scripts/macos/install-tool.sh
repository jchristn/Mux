#!/usr/bin/env bash
# Build and install the mux global tool (Mux.Cli) from this checkout. Fails if mux is already
# installed; use reinstall-tool.sh to replace an existing install.
# On macOS this also builds and installs ~/Applications/Mux.app and ~/Applications/Mux Agent.app.
# The package is built under artifacts/tool-packages/<framework>.
# Usage: ./scripts/macos/install-tool.sh [net8.0|net10.0]  (or --framework <tfm>; MUX_TFM is also honored)
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

ORIGINAL_DOTNET_ROOT="${DOTNET_ROOT:-}"
ORIGINAL_PATH="$PATH"
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"

# True when a process named mux (the installed global tool) is running.
mux_running() { pgrep -u "$(id -u)" -x mux >/dev/null 2>&1; }
# True when the Mux.Cli global tool is installed.
tool_installed() {
    dotnet tool list -g 2>/dev/null | awk 'tolower($1) == "mux.cli" { found = 1 } END { exit !found }'
}

PACKAGE_SOURCE="$REPO_ROOT/artifacts/tool-packages/$TFM"

echo "mux tool install (macOS, $TFM)"
echo "[1/6] Checking for running mux processes..."
if mux_running; then
    die "A running mux process is using the global tool. Exit all mux sessions (or run scripts/macos/kill-mux.sh) and try again."
fi
# The apps are replaced in the last step; check now so a running app cannot leave a half-done install.
for app in "Mux" "Mux Agent"; do
    if pgrep -u "$(id -u)" -f "$HOME/Applications/$app.app/Contents/MacOS/" >/dev/null 2>&1; then
        die "$app is running. Quit it (or run scripts/macos/kill-mux.sh) and try again."
    fi
done
echo "[2/6] Checking for an existing install..."
if tool_installed; then
    die "mux is already installed. Run scripts/macos/reinstall-tool.sh to replace it."
fi
echo "[3/6] Building the mux package..."
rm -rf "$PACKAGE_SOURCE"
mkdir -p "$PACKAGE_SOURCE"
dotnet pack "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --configuration Release -p:TargetFrameworks="$TFM" --output "$PACKAGE_SOURCE"
echo "[4/6] Installing the mux global tool..."
dotnet tool install -g --source "$PACKAGE_SOURCE" --framework "$TFM" --disable-parallel Mux.Cli
echo "[5/6] Verifying the mux command..."
if command -v mux >/dev/null 2>&1; then
    mux -v
elif [ -x "$HOME/.dotnet/tools/mux" ]; then
    "$HOME/.dotnet/tools/mux" -v
else
    die "mux was installed, but it is not on PATH or in $HOME/.dotnet/tools."
fi
# 'dotnet tool install -g' puts mux in ~/.dotnet/tools, and a user-local .NET in ~/.dotnet also
# needs DOTNET_ROOT, or new shells cannot run mux.
case ":$ORIGINAL_PATH:" in
    *":$HOME/.dotnet/tools:"*) ;;
    *)
        echo "Note: $HOME/.dotnet/tools is not on your PATH, so new shells will not find 'mux'." >&2
        echo "Add this line to ~/.zprofile:" >&2
        echo '  export PATH="$PATH:$HOME/.dotnet/tools"' >&2 ;;
esac
if [ -z "$ORIGINAL_DOTNET_ROOT" ] && [ "$(command -v dotnet)" = "$HOME/.dotnet/dotnet" ]; then
    echo "Note: .NET is installed in ~/.dotnet, so 'mux' also needs DOTNET_ROOT. Add this line to ~/.zprofile:" >&2
    echo '  export DOTNET_ROOT="$HOME/.dotnet"' >&2
fi
echo "[6/6] Installing the macOS apps..."
# shellcheck source=app-bundle.sh
. "$SCRIPT_DIR/app-bundle.sh"
install_macos_app desktop
install_macos_app agent
echo "Done."
