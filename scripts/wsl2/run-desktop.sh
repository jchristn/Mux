#!/usr/bin/env bash
# Launch the mux desktop application (Avalonia) from source.
# Usage: ./scripts/wsl2/run-desktop.sh [net8.0|net10.0]
# WSL 2 version: needs WSLg for the window. WSLg has no system tray, so no tray icon appears.
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
if [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
    die "No display found. The mux desktop app is a GUI app and needs WSLg (Windows 11, or Windows 10 with the Microsoft Store WSL). From Windows run: wsl --update, then wsl --shutdown, and reopen this shell."
fi
wsl_check_libs error "The mux desktop app" "$WSL_AVALONIA_LIBS"
# Running mux from source can start the agent (the TUI and desktop app launch it when none is running),
# and the agent registers itself to start at login on its first run unless this marker exists. Login
# startup is run-at-startup.sh's job, so write the marker to keep source runs from registering a build.
MUX_CONFIG_HOME="${MUX_CONFIG_DIR:-$HOME/.mux}"
mkdir -p "$MUX_CONFIG_HOME"
[ -f "$MUX_CONFIG_HOME/startup-registered" ] || date -u +%Y-%m-%dT%H:%M:%SZ > "$MUX_CONFIG_HOME/startup-registered"
# Give the app the Windows PATH entries back (clip.exe, explorer.exe, wslview) and BROWSER=wslview.
wsl_prepare_app_env
dotnet run --project "$REPO_ROOT/src/Mux.Desktop/Mux.Desktop.csproj" --framework "$TFM" -p:TargetFrameworks="$TFM"
