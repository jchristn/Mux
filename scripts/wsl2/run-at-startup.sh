#!/usr/bin/env sh
# Register the mux agent (the tray host that runs the local REST server) to start at Windows login.
# WSL 2 counterpart of scripts/linux/run-at-startup.sh. systemd user services and desktop autostart
# entries do nothing at Windows login, so this installs a hidden launcher in the Windows Startup
# folder that runs the agent in this distro through wsl.exe. No admin rights needed.
# Usage: ./scripts/wsl2/run-at-startup.sh [net8.0|net10.0]
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
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || wsl_die "Unexpected argument: $1"

# Same lookup as scripts/linux/run-at-startup.sh: dist/ first unless a framework was chosen, then
# the Release and Debug builds. -f rather than -x, since DrvFs checkouts may lack exec bits.
AGENT_BIN="$REPO_ROOT/src/Mux.Agent/bin/Release/$TFM/Mux.Agent"
[ -f "$AGENT_BIN" ] || AGENT_BIN="$REPO_ROOT/src/Mux.Agent/bin/Debug/$TFM/Mux.Agent"
if [ "$FRAMEWORK_ARGS" -eq 0 ] && [ -z "${MUX_TFM:-}" ] && [ -f "$REPO_ROOT/dist/Mux.Agent" ]; then
    AGENT_BIN="$REPO_ROOT/dist/Mux.Agent"
fi
if [ ! -f "$AGENT_BIN" ]; then
    wsl_log "Mux.Agent not found. Build it first, e.g.:"
    wsl_log "  dotnet build src/Mux.Agent/Mux.Agent.csproj -c Release -f $TFM -p:TargetFrameworks=$TFM"
    exit 1
fi

if [ -z "${WAYLAND_DISPLAY:-}" ] && [ -z "${DISPLAY:-}" ]; then
    wsl_log "Warning: no display right now. The agent is a GUI app and needs WSLg (Windows 11, or"
    wsl_log "Windows 10 with the Microsoft Store WSL; run wsl --update). Registering anyway."
fi

# systemd user services and desktop autostart entries do not run at Windows login. Replace any that
# scripts/linux/run-at-startup.sh created, so the agent does not start twice.
SERVICE="$HOME/.config/systemd/user/mux-agent.service"
DESKTOP="$HOME/.config/autostart/mux-agent.desktop"
if [ -f "$SERVICE" ]; then
    systemctl --user disable --now mux-agent >/dev/null 2>&1 || true
    rm -f "$SERVICE"
    systemctl --user daemon-reload >/dev/null 2>&1 || true
    echo "Removed the mux-agent systemd user service left by scripts/linux/run-at-startup.sh."
fi
if [ -f "$DESKTOP" ]; then
    rm -f "$DESKTOP"
    echo "Removed the desktop autostart entry left by scripts/linux/run-at-startup.sh."
fi

if [ -n "${DOTNET_ROOT:-}" ]; then
    wsl_startup_install mux-agent env "DOTNET_ROOT=$DOTNET_ROOT" "$AGENT_BIN"
else
    wsl_startup_install mux-agent "$AGENT_BIN"
fi
echo "Registered $AGENT_BIN to start in WSL ($WSL_DISTRO_NAME) at Windows login."

if pgrep -f Mux.Agent >/dev/null 2>&1; then
    echo "The agent is already running."
else
    nohup "$AGENT_BIN" >/dev/null 2>&1 &
    echo "Started the agent now (it runs the REST server)."
fi
echo "Note: WSLg has no system tray, so no tray icon appears; the REST server still runs, and Windows"
echo "reaches it on localhost through WSL localhost forwarding. The distro stays up while it runs."
echo "For the tray icon, use scripts\\windows\\run-at-startup.bat from Windows instead."
