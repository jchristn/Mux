#!/usr/bin/env bash
# Uninstall the mux global tool (Mux.Cli). Succeeds with a message when mux is not installed.
# WSL 2 version: removes the install in this distro only; use scripts\windows\remove-tool.bat for Windows.
# It also removes the desktop app and tray agent installed by install-tool.sh: the per-user copy in
# ${XDG_DATA_HOME:-~/.local/share}/mux/apps and the system-wide copy in /opt/mux (directly, else with
# sudo, else it warns and names what is left), with their launchers and icon. MUX_SYSTEM_INSTALL=0
# leaves the system-wide copy alone.
# Login registration (run-at-startup.sh) is separate; remove it with remove-from-startup.sh.
# Usage: ./scripts/wsl2/remove-tool.sh
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

[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# shellcheck source=../common/linux-apps.sh
. "$REPO_ROOT/scripts/common/linux-apps.sh"
# shellcheck disable=SC2034 # read by linux-apps.sh
LINUX_APPS_KILL_MUX="scripts/wsl2/kill-mux.sh"

# True when a process named mux (the installed global tool) is running.
mux_running() { pgrep -u "$(id -u)" -x mux >/dev/null 2>&1; }
# True when the Mux.Cli global tool is installed.
tool_installed() {
    dotnet tool list -g 2>/dev/null | awk 'tolower($1) == "mux.cli" { found = 1 } END { exit !found }'
}

echo "mux tool removal (WSL 2)"
echo "[1/3] Checking for running mux processes..."
if mux_running; then
    die "A running mux process is using the global tool. Exit all mux sessions (or run scripts/wsl2/kill-mux.sh) and try again."
fi
# The apps are removed in the last step; check both install locations now so a running app cannot
# leave a half-done removal.
for component in desktop agent; do
    linux_app_not_running "$component" || exit 1
done
echo "[2/3] Removing the mux global tool..."
if tool_installed; then
    dotnet tool uninstall -g Mux.Cli
else
    echo "mux is not installed; nothing to remove."
fi
echo "[3/3] Removing the desktop app and tray agent..."
linux_remove_apps
if wsl_startup_exists mux-agent; then
    echo "Note: the agent is still registered to start at Windows login. Run scripts/wsl2/remove-from-startup.sh to remove that."
fi
echo "Done."
