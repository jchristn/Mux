#!/usr/bin/env bash
# Uninstall, rebuild and reinstall the mux global tool (Mux.Cli) from this checkout.
# It also builds the desktop app (Mux) and the tray agent (Mux Agent) as self-contained apps and installs
# them per-user in ${XDG_DATA_HOME:-~/.local/share}/mux/apps/{desktop,agent} (launchers in
# .../applications, icon in .../icons/hicolor/256x256/apps) and system-wide in /opt/mux/{desktop,agent}
# (launchers in /usr/share/applications, icon in /usr/share/icons). The system-wide copy is written
# directly when the folders are writable (root), otherwise with sudo; without sudo rights it warns and
# keeps the per-user install. MUX_SYSTEM_INSTALL=0 skips the system-wide install.
# The package is built under artifacts/tool-packages/<framework>, the apps under artifacts/linux-app.
# Usage: ./scripts/linux/reinstall-tool.sh [net8.0|net10.0]  (or --framework <tfm>; MUX_TFM is also honored)
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Linux ]; then
    die "This script is for Linux. Use scripts/macos on macOS or scripts\\windows on Windows."
fi
# WSL, not just a Microsoft kernel: Docker Desktop containers on the WSL 2 backend have one too.
if grep -qi microsoft /proc/version 2>/dev/null && { [ -n "${WSL_DISTRO_NAME:-}" ] || [ -d /run/WSL ] || ls /proc/sys/fs/binfmt_misc/WSLInterop* >/dev/null 2>&1; }; then
    die "This is WSL. Use the same script in scripts/wsl2 (WSL 2) or scripts/wsl (WSL 1); they handle the Windows PATH, line endings and Windows startup."
fi
# shellcheck source=../common/native-libs.sh
. "$REPO_ROOT/scripts/common/native-libs.sh"

ORIGINAL_DOTNET_ROOT="${DOTNET_ROOT:-}"
ORIGINAL_PATH="$PATH"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
# shellcheck source=../common/linux-apps.sh
. "$REPO_ROOT/scripts/common/linux-apps.sh"
# shellcheck disable=SC2034 # read by linux-apps.sh
LINUX_APPS_KILL_MUX="scripts/linux/kill-mux.sh"
# Print the help before touching the system.
case "${1:-}" in -h|--help) resolve_framework "$1" ;; esac
# Install the system packages mux needs when they are missing (INSTALL_SYSTEM_PACKAGES=0 to skip).
native_install_deps "mux" icu gui clipboard
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://learn.microsoft.com/dotnet/core/install/linux"
native_check_icu
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

echo "mux tool reinstall (Linux, $TFM)"
echo "[1/7] Checking for running mux processes..."
if mux_running; then
    die "A running mux process is using the global tool. Exit all mux sessions (or run scripts/linux/kill-mux.sh) and try again."
fi
# The apps are replaced in a later step; check both install locations now so a running app cannot
# leave a half-done install.
for component in desktop agent; do
    linux_app_not_running "$component" || exit 1
done
echo "[2/7] Removing the existing mux global tool..."
if tool_installed; then
    dotnet tool uninstall -g Mux.Cli
else
    echo "mux is not installed; continuing."
fi
echo "[3/7] Building the mux package..."
rm -rf "$PACKAGE_SOURCE"
mkdir -p "$PACKAGE_SOURCE"
dotnet pack "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" --configuration Release -p:TargetFrameworks="$TFM" --output "$PACKAGE_SOURCE"
echo "[4/7] Building the desktop app and tray agent..."
# Only the last step replaces the installed apps, so a failed build leaves them unchanged.
linux_publish_app desktop || die "Building the desktop app failed."
linux_publish_app agent || die "Building the tray agent failed."
echo "[5/7] Installing the mux global tool..."
dotnet tool install -g --source "$PACKAGE_SOURCE" --framework "$TFM" --disable-parallel Mux.Cli
echo "[6/7] Verifying the mux command..."
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
        echo "Add this line to ~/.bashrc (or ~/.profile):" >&2
        echo '  export PATH="$PATH:$HOME/.dotnet/tools"' >&2 ;;
esac
if [ -z "$ORIGINAL_DOTNET_ROOT" ] && [ "$(command -v dotnet)" = "$HOME/.dotnet/dotnet" ]; then
    echo "Note: .NET is installed in ~/.dotnet, so 'mux' also needs DOTNET_ROOT. Add this line to ~/.bashrc (or ~/.profile):" >&2
    echo '  export DOTNET_ROOT="$HOME/.dotnet"' >&2
fi
echo "[7/7] Installing the desktop app and tray agent..."
linux_install_apps || die "Installing the desktop app and tray agent failed."
echo "Done."
