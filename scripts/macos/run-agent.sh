#!/usr/bin/env bash
# Launch the mux agent (the menu-bar host that runs the local REST server).
# On macOS this starts ~/Applications/Mux Agent.app, first installing or refreshing it when it is missing or its
# version differs from this checkout. Set MUX_REFRESH_AGENT=1 to rebuild it anyway (to test agent changes).
# A running agent is never replaced: a current one is reported (and brought up), a stale one gets a note to quit
# it first. This is an explicit request, so MUX_AGENT_AUTOSTART does not stop it.
# Usage: ./scripts/macos/run-agent.sh [net8.0|net10.0]
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
# Install or refresh the per-user agent only when needed, then start it unless one is running. The agent is
# started with MUX_AGENT_AUTOSTART=0: it registers itself to start at login on its first run otherwise, and
# login startup is run-at-startup.sh's job.
macos_ensure_user_agent --explicit || die "Installing Mux Agent failed."
macos_start_user_agent --explicit || die "Installing Mux Agent failed."
