#!/usr/bin/env bash
# Uninstall the mux global tool (Mux.Cli). Succeeds with a message when mux is not installed.
# Login registration (run-at-startup.sh) is separate; remove it with remove-from-startup.sh.
# Usage: ./scripts/linux/remove-tool.sh
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

[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://learn.microsoft.com/dotnet/core/install/linux"

# True when a process named mux (the installed global tool) is running.
mux_running() { pgrep -u "$(id -u)" -x mux >/dev/null 2>&1; }
# True when the Mux.Cli global tool is installed.
tool_installed() {
    dotnet tool list -g 2>/dev/null | awk 'tolower($1) == "mux.cli" { found = 1 } END { exit !found }'
}

echo "mux tool removal (Linux)"
echo "[1/2] Checking for running mux processes..."
if mux_running; then
    die "A running mux process is using the global tool. Exit all mux sessions (or run scripts/linux/kill-mux.sh) and try again."
fi
echo "[2/2] Removing the mux global tool..."
if tool_installed; then
    dotnet tool uninstall -g Mux.Cli
else
    echo "mux is not installed; nothing to remove."
fi
if [ -f "$HOME/.config/systemd/user/mux-agent.service" ] || [ -f "$HOME/.config/autostart/mux-agent.desktop" ]; then
    echo "Note: the agent is still registered to start at login. Run scripts/linux/remove-from-startup.sh to remove that."
fi
echo "Done."
