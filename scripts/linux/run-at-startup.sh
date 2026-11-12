#!/usr/bin/env bash
# Register the mux agent (the tray host that runs the local REST server) to start at login, and
# start it now. Uses a systemd --user service tied to graphical-session.target when the desktop
# provides one (restarts on failure); otherwise a desktop autostart entry. No root needed.
# Stops a systemd-managed agent, then builds and installs (or refreshes) the per-user agent in
# ${XDG_DATA_HOME:-~/.local/share}/mux/apps/agent, the same copy install-tool.sh installs, and
# registers that copy (quit any other running agent first). Never touches the system-wide copy.
# Usage: ./scripts/linux/run-at-startup.sh [net8.0|net10.0]
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

# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
# shellcheck source=../common/linux-apps.sh
. "$REPO_ROOT/scripts/common/linux-apps.sh"
# shellcheck disable=SC2034 # read by linux-apps.sh
LINUX_APPS_KILL_MUX="scripts/linux/kill-mux.sh"
# Print the help before touching the system.
case "${1:-}" in -h|--help) resolve_framework "$1" ;; esac
# Install the system packages mux needs when they are missing (INSTALL_SYSTEM_PACKAGES=0 to skip).
native_install_deps "mux" icu gui
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://learn.microsoft.com/dotnet/core/install/linux"
native_check_icu
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"

# Stop a systemd-managed agent first so the install can replace it (re-running is a refresh).
if command -v systemctl >/dev/null 2>&1 && systemctl --user is-active --quiet mux-agent 2>/dev/null; then
    systemctl --user stop mux-agent
fi
native_check_libs error "The mux agent" "$NATIVE_AVALONIA_LIBS"
if pgrep -u "$(id -u)" -x Mux.Agent >/dev/null 2>&1; then
    die "Another mux agent is running (for example from /opt/mux or a source build). Quit it (or run scripts/linux/kill-mux.sh) and try again."
fi
# Install or refresh the per-user agent (a self-contained publish, so it needs no DOTNET_ROOT at login).
linux_install_user_app agent || die "Installing the tray agent failed."
AGENT_BIN="$(linux_app_dir agent user)/Mux.Agent"
[ -x "$AGENT_BIN" ] || die "Mux.Agent was not found at $AGENT_BIN after the install."

# The agent registers itself at login on its first run unless this marker exists. Login startup is
# managed by these scripts, so write the marker: the agent must not re-register after this script.
MUX_CONFIG_HOME="${MUX_CONFIG_DIR:-$HOME/.mux}"
mkdir -p "$MUX_CONFIG_HOME"
[ -f "$MUX_CONFIG_HOME/startup-registered" ] || date -u +%Y-%m-%dT%H:%M:%SZ > "$MUX_CONFIG_HOME/startup-registered"

SERVICE="$HOME/.config/systemd/user/mux-agent.service"
DESKTOP="$HOME/.config/autostart/mux-agent.desktop"

# systemd only when this desktop actually starts graphical-session.target (GNOME and KDE do; XFCE,
# MATE, Cinnamon, LXQt and bare window managers usually don't). Otherwise use a desktop autostart entry.
if command -v systemctl >/dev/null 2>&1 && systemctl --user is-active --quiet graphical-session.target 2>/dev/null; then
    # systemd expands % specifiers and $VARIABLES in ExecStart; double them to keep the path literal.
    SYSTEMD_BIN="${AGENT_BIN//%/%%}"
    SYSTEMD_BIN="${SYSTEMD_BIN//\$/\$\$}"
    # Drop links from an older unit (WantedBy=default.target) before rewriting it.
    systemctl --user disable mux-agent >/dev/null 2>&1 || true
    rm -f "$DESKTOP"
    mkdir -p "$(dirname "$SERVICE")"
    {
        echo "[Unit]"
        echo "Description=mux tray agent (hosts the local REST server)"
        echo "# A GUI/tray app: start with the graphical session (which provides DISPLAY), stop with it."
        echo "After=graphical-session.target"
        echo "PartOf=graphical-session.target"
        echo
        echo "[Service]"
        echo "ExecStart=\"$SYSTEMD_BIN\""
        echo "Environment=MUX_AGENT_AUTOSTART=0"
        echo "Restart=on-failure"
        echo "RestartSec=5"
        echo
        echo "[Install]"
        echo "WantedBy=graphical-session.target"
    } > "$SERVICE"
    systemctl --user daemon-reload
    systemctl --user enable mux-agent
    systemctl --user restart mux-agent
    echo "Installed and started the mux-agent systemd user service (it runs the REST server)."
    echo "Check it with: systemctl --user status mux-agent"
    echo "It starts with your desktop session (graphical-session.target); the agent needs a display."
else
    # Replace a systemd unit from an earlier run, so the agent does not start twice.
    if [ -f "$SERVICE" ]; then
        systemctl --user disable --now mux-agent >/dev/null 2>&1 || true
        rm -f "$SERVICE"
        systemctl --user daemon-reload >/dev/null 2>&1 || true
    fi
    mkdir -p "$(dirname "$DESKTOP")"
    cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=mux Agent
Comment=mux tray agent (hosts the local REST server)
Exec=env MUX_AGENT_AUTOSTART=0 $(linux_desktop_exec "$AGENT_BIN")
X-GNOME-Autostart-enabled=true
EOF
    echo "Installed $DESKTOP; the agent starts at each desktop login."
    if pgrep -u "$(id -u)" -x Mux.Agent >/dev/null 2>&1; then
        echo "The agent is already running."
    else
        MUX_AGENT_AUTOSTART=0 nohup "$AGENT_BIN" >/dev/null 2>&1 &
        echo "Started the agent now (it runs the REST server)."
    fi
fi

echo "Note: the tray icon needs StatusNotifierItem support (KDE Plasma has it built in; GNOME needs the"
echo "'AppIndicator and KStatusNotifierItem' extension). Without a tray, the agent still runs the REST server."
