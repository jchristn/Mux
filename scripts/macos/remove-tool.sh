#!/usr/bin/env bash
# Uninstall the mux global tool (Mux.Cli). Succeeds with a message when mux is not installed.
# On macOS this also removes Mux.app and Mux Agent.app from ~/Applications and /Applications (with sudo when
# /Applications is not writable).
# Login registration (run-at-startup.sh) is separate; remove it with remove-from-startup.sh.
# Usage: ./scripts/macos/remove-tool.sh
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"

# True when a process named mux (the installed global tool) is running.
mux_running() { pgrep -u "$(id -u)" -x mux >/dev/null 2>&1; }
# True when the Mux.Cli global tool is installed.
tool_installed() {
    dotnet tool list -g 2>/dev/null | awk 'tolower($1) == "mux.cli" { found = 1 } END { exit !found }'
}

echo "mux tool removal (macOS)"
echo "[1/3] Checking for running mux processes..."
if mux_running; then
    die "A running mux process is using the global tool. Exit all mux sessions (or run scripts/macos/kill-mux.sh) and try again."
fi
echo "[2/3] Removing the mux global tool..."
if tool_installed; then
    dotnet tool uninstall -g Mux.Cli
else
    echo "mux is not installed; nothing to remove."
fi
echo "[3/3] Removing the macOS apps (~/Applications and /Applications)..."
# shellcheck source=app-bundle.sh
. "$SCRIPT_DIR/app-bundle.sh"
for app in "Mux" "Mux Agent"; do
    macos_app_not_running "$app" || exit 1
done
for app in "Mux" "Mux Agent"; do
    for folder in "$HOME/Applications" "$(macos_system_apps_dir)"; do
        if [ -d "$folder/$app.app" ]; then
            macos_remove_app "$app" "$folder" || true
        else
            echo "$folder/$app.app is not installed."
        fi
    done
done
if [ -f "$HOME/Library/LaunchAgents/com.jchristn.mux.agent.plist" ]; then
    echo "Note: the agent is still registered to start at login. Run scripts/macos/remove-from-startup.sh to remove that."
fi
echo "Done."
