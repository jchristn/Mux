#!/usr/bin/env bash
# Launch the mux agent (the tray host that runs the local REST server). Starts the per-user install in
# ${XDG_DATA_HOME:-~/.local/share}/mux/apps/agent, first installing or refreshing it when it is missing or its
# version differs from this checkout. Set MUX_REFRESH_AGENT=1 to rebuild it anyway (to test agent changes).
# A running agent is never replaced: a current one is reported, a stale one gets a note to quit it first.
# This is an explicit request, so MUX_AGENT_AUTOSTART does not stop it.
# Usage: ./scripts/linux/run-agent.sh [net8.0|net10.0]
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

command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://learn.microsoft.com/dotnet/core/install/linux"
native_check_icu
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
# shellcheck source=../common/linux-apps.sh
. "$REPO_ROOT/scripts/common/linux-apps.sh"
# shellcheck disable=SC2034 # read by linux-apps.sh
LINUX_APPS_KILL_MUX="scripts/linux/kill-mux.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] || die "No display found (DISPLAY and WAYLAND_DISPLAY are unset). The mux agent is a GUI app; run it from a desktop session."
native_check_libs error "The mux agent" "$NATIVE_AVALONIA_LIBS"
# Running mux from source can start the agent (the TUI and desktop app launch it when none is running),
# and the agent registers itself to start at login on its first run unless this marker exists. Login
# startup is run-at-startup.sh's job, so write the marker to keep source runs from registering a build.
MUX_CONFIG_HOME="${MUX_CONFIG_DIR:-$HOME/.mux}"
mkdir -p "$MUX_CONFIG_HOME"
[ -f "$MUX_CONFIG_HOME/startup-registered" ] || date -u +%Y-%m-%dT%H:%M:%SZ > "$MUX_CONFIG_HOME/startup-registered"
# Install or refresh the per-user agent only when needed, then start it unless one is running. The agent is
# started with MUX_AGENT_AUTOSTART=0: it registers itself to start at login on its first run otherwise, and
# login startup is run-at-startup.sh's job.
linux_ensure_user_agent --explicit || die "Installing the tray agent failed."
linux_start_user_agent --explicit || die "Installing the tray agent failed."
