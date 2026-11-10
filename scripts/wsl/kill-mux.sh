#!/usr/bin/env bash
# Stop every running mux process: the agent, desktop app, CLI/TUI, server, publisher and the
# installed mux tool, including copies started with dotnet run or dotnet <dll>. Asks them to exit,
# waits up to 5 seconds, then force-kills what is left. Succeeds when nothing is running.
# WSL 1 version: stops processes in this distro only; use scripts\windows\kill-mux.bat for Windows ones.
# Usage: ./scripts/wsl/kill-mux.sh
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

# Process names (apphost launches) and the managed-dll command lines (dotnet <dll>, dotnet run).
NAMES="Mux.Agent Mux.Desktop Mux.Cli Mux.Server Mux.Publisher mux"
DLL_PATTERN='Mux\.(Agent|Desktop|Cli|Server|Publisher)\.dll'

running() {
    for name in $NAMES; do
        pgrep -u "$(id -u)" -x "$name" >/dev/null 2>&1 && return 0
    done
    pgrep -u "$(id -u)" -f "$DLL_PATTERN" >/dev/null 2>&1
}

signal_all() {
    for name in $NAMES; do
        pkill "$1" -u "$(id -u)" -x "$name" >/dev/null 2>&1 || true
    done
    pkill "$1" -u "$(id -u)" -f "$DLL_PATTERN" >/dev/null 2>&1 || true
}

echo "Stopping mux processes..."
# A systemd-managed agent restarts after a failure; stop it through systemd first. It stays enabled,
# so it still starts at the next login.
if command -v systemctl >/dev/null 2>&1 && systemctl --user is-active --quiet mux-agent 2>/dev/null; then
    systemctl --user stop mux-agent
    echo "Stopped the mux-agent systemd user service (it starts again at the next login)."
fi
if ! running; then
    echo "No mux processes are running."
    exit 0
fi
signal_all -TERM
for _ in 1 2 3 4 5; do
    running || break
    sleep 1
done
if running; then
    echo "Some mux processes did not exit; force-killing them."
    signal_all -KILL
    sleep 1
fi
running && die "Some mux processes could not be stopped. Check: pgrep -fl Mux"
echo "Done."
