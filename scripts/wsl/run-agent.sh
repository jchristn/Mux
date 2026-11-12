#!/usr/bin/env bash
# Launch the mux agent (the tray host that runs the local REST server). Starts the per-user install in
# ${XDG_DATA_HOME:-~/.local/share}/mux/apps/agent, first installing or refreshing it when it is missing or its
# version differs from this checkout. Set MUX_REFRESH_AGENT=1 to rebuild it anyway (to test agent changes).
# A running agent is never replaced: a current one is reported, a stale one gets a note to quit it first.
# This is an explicit request, so MUX_AGENT_AUTOSTART does not stop it.
# Usage: ./scripts/wsl/run-agent.sh [net8.0|net10.0]
# WSL 1 version: WSL 1 has no WSLg, so this needs an X server on Windows with DISPLAY set.
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
# shellcheck source=../common/linux-apps.sh
. "$REPO_ROOT/scripts/common/linux-apps.sh"
# shellcheck disable=SC2034 # read by linux-apps.sh
LINUX_APPS_KILL_MUX="scripts/wsl/kill-mux.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
if [ -z "${DISPLAY:-}" ]; then
    die "WSL 1 has no WSLg, so it cannot show the mux agent. Run the Windows script (scripts\\windows) instead, move this distro to WSL 2 (wsl --set-version ${WSL_DISTRO_NAME:-<distro>} 2) and use scripts/wsl2, or start an X server on Windows and set DISPLAY."
fi
wsl_check_libs error "The mux agent" "$WSL_AVALONIA_LIBS"
# Running mux from source can start the agent (the TUI and desktop app launch it when none is running),
# and the agent registers itself to start at login on its first run unless this marker exists. Login
# startup is run-at-startup.sh's job, so write the marker to keep source runs from registering a build.
MUX_CONFIG_HOME="${MUX_CONFIG_DIR:-$HOME/.mux}"
mkdir -p "$MUX_CONFIG_HOME"
[ -f "$MUX_CONFIG_HOME/startup-registered" ] || date -u +%Y-%m-%dT%H:%M:%SZ > "$MUX_CONFIG_HOME/startup-registered"
# Install or refresh the per-user agent only when needed (before the Windows PATH entries come back, as in
# run-desktop.sh).
linux_ensure_user_agent --explicit || die "Installing the tray agent failed."
# Give the app the Windows PATH entries back (clip.exe, explorer.exe, wslview) and BROWSER=wslview.
wsl_prepare_app_env
# Start the installed agent (with the app environment above) unless one is running. It is started with
# MUX_AGENT_AUTOSTART=0: it registers itself to start at login on its first run otherwise, and login
# startup is run-at-startup.sh's job.
linux_start_user_agent --explicit || die "Installing the tray agent failed."
