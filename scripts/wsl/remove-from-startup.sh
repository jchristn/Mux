#!/usr/bin/env bash
# Deregister the mux agent from starting at Windows login: removes the Windows Startup launcher
# that scripts/wsl/run-at-startup.sh installs, plus any systemd user service or desktop autostart
# entry left by the Linux scripts. A running agent keeps running; stop it with scripts/wsl/kill-mux.sh.
# Usage: ./scripts/wsl/remove-from-startup.sh
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

[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"

# The agent registers itself at login on its first run unless this marker exists. Login startup is
# managed by these scripts, so write the marker: the agent must not re-register itself after it was removed.
MUX_CONFIG_HOME="${MUX_CONFIG_DIR:-$HOME/.mux}"
mkdir -p "$MUX_CONFIG_HOME"
[ -f "$MUX_CONFIG_HOME/startup-registered" ] || date -u +%Y-%m-%dT%H:%M:%SZ > "$MUX_CONFIG_HOME/startup-registered"

SERVICE="$HOME/.config/systemd/user/mux-agent.service"
DESKTOP="$HOME/.config/autostart/mux-agent.desktop"
did=0

if wsl_startup_remove mux-agent; then
    did=1
fi
if [ -f "$SERVICE" ]; then
    systemctl --user disable --now mux-agent >/dev/null 2>&1 || true
    rm -f "$SERVICE"
    systemctl --user daemon-reload >/dev/null 2>&1 || true
    echo "Removed the mux-agent systemd user service."
    did=1
fi
if [ -f "$DESKTOP" ]; then
    rm -f "$DESKTOP"
    echo "Removed the desktop autostart entry."
    did=1
fi

if [ "$did" -eq 0 ]; then
    echo "The agent is not registered to run at login; nothing to do."
else
    echo "If an agent is still running, stop it with scripts/wsl/kill-mux.sh."
fi
