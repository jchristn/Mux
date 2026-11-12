#!/usr/bin/env bash
# Launch the mux desktop application (Avalonia).
# On macOS this builds and installs ~/Applications/Mux.app, then opens it. It first installs or refreshes
# ~/Applications/Mux Agent.app when it is missing or out of date, and starts it unless one is running.
# Usage: ./scripts/macos/run-desktop.sh [net8.0|net10.0]
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
# shellcheck source=app-bundle.sh
. "$SCRIPT_DIR/app-bundle.sh"
# Running mux from source can start the agent (the TUI and desktop app launch it when none is running),
# and the agent registers itself to start at login on its first run unless this marker exists. Login
# startup is run-at-startup.sh's job, so write the marker to keep source runs from registering a build.
MUX_CONFIG_HOME="${MUX_CONFIG_DIR:-$HOME/.mux}"
mkdir -p "$MUX_CONFIG_HOME"
[ -f "$MUX_CONFIG_HOME/startup-registered" ] || date -u +%Y-%m-%dT%H:%M:%SZ > "$MUX_CONFIG_HOME/startup-registered"
# Every user surface starts the tray agent. Install or refresh ~/Applications/Mux Agent.app first, but only
# when it is missing or its version differs from this checkout (MUX_REFRESH_AGENT=1 forces it); a running
# copy is never replaced. MUX_AGENT_AUTOSTART=0 skips this and the start below, as it does in the app.
macos_ensure_user_agent || die "Installing Mux Agent failed."
install_macos_app desktop --user-only
# Start the agent unless one is running; Mux.app then finds it running and leaves it be.
macos_start_user_agent
open -a "$HOME/Applications/Mux.app"
