#!/usr/bin/env sh
# Deregister the mux agent from starting at Windows login. WSL 2 counterpart of
# scripts/linux/remove-from-startup.sh: removes the Windows Startup launcher that
# scripts/wsl2/run-at-startup.sh installs, plus any systemd user service or desktop autostart entry
# left by the Linux script. A currently running agent is left alone.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2   # "wsl" in scripts/wsl, "wsl2" in scripts/wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
SERVICE="$HOME/.config/systemd/user/mux-agent.service"
DESKTOP="$HOME/.config/autostart/mux-agent.desktop"
did=0

if wsl_startup_remove mux-agent; then
    did=1
fi

# Also clean up anything scripts/linux/run-at-startup.sh created earlier.
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
fi
echo "A running agent keeps running; stop it now with: pkill -f Mux.Agent"
