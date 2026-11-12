#!/usr/bin/env bash
# Deregister the mux agent from starting at login: unloads (which also stops) the launchd-managed
# agent and removes its plist. Any other running copy keeps running; quit it from the menu bar or
# run scripts/macos/kill-mux.sh. ~/Applications/Mux Agent.app stays in place; remove-tool.sh removes it.
# Usage: ./scripts/macos/remove-from-startup.sh
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"

# The agent registers itself at login on its first run unless this marker exists. Login startup is
# managed by these scripts, so write the marker: the agent must not re-register itself after it was removed.
MUX_CONFIG_HOME="${MUX_CONFIG_DIR:-$HOME/.mux}"
mkdir -p "$MUX_CONFIG_HOME"
[ -f "$MUX_CONFIG_HOME/startup-registered" ] || date -u +%Y-%m-%dT%H:%M:%SZ > "$MUX_CONFIG_HOME/startup-registered"

LABEL="com.jchristn.mux.agent"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

if [ ! -f "$PLIST" ]; then
    echo "The agent is not registered to run at login; nothing to do."
    exit 0
fi
# bootout also stops the launchd-managed copy; that is what deregistering means for launchd.
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || launchctl unload -w "$PLIST" 2>/dev/null || true
rm -f "$PLIST"
echo "Removed $PLIST."
echo "If an agent is still running, quit it from the menu bar or run scripts/macos/kill-mux.sh."
