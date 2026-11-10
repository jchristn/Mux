#!/usr/bin/env bash
# Register the mux agent (the host that runs the local REST server) to start at Windows login,
# and start it now. systemd user services and desktop autostart entries do not run at Windows
# login, so this installs a hidden launcher in the Windows Startup folder that runs the agent in
# this distro through wsl.exe (no admin rights needed); the launcher keeps the distro running.
# Builds Mux.Agent (Release) for the selected framework first, so stop a running agent beforehand.
# The agent is a GUI app and needs WSLg. WSLg has no system tray, so no tray icon appears.
# Usage: ./scripts/wsl2/run-at-startup.sh [net8.0|net10.0]
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

wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
if [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
    die "No display found. The mux agent is a GUI app and needs WSLg (Windows 11, or Windows 10 with the Microsoft Store WSL). From Windows run: wsl --update, then wsl --shutdown, and reopen this shell."
fi
wsl_check_libs error "The mux agent" "$WSL_AVALONIA_LIBS"

# Always register a fresh Release build of the agent for the selected framework.
if pgrep -u "$(id -u)" -x Mux.Agent >/dev/null 2>&1; then
    die "The mux agent is running. Stop it (scripts/wsl2/kill-mux.sh) and try again, so the build can replace it."
fi
echo "Building Mux.Agent (Release, $TFM)..."
dotnet build "$REPO_ROOT/src/Mux.Agent/Mux.Agent.csproj" -c Release -f "$TFM" -p:TargetFrameworks="$TFM" -nologo -v quiet
AGENT_BIN="$REPO_ROOT/src/Mux.Agent/bin/Release/$TFM/Mux.Agent"
[ -f "$AGENT_BIN" ] || die "Mux.Agent was not found at $AGENT_BIN after the build."
# A framework-dependent agent started at login needs to find .NET: pass the root of the dotnet in use.
if [ -z "${DOTNET_ROOT:-}" ]; then
    DOTNET_ROOT="$(dirname "$(readlink -f "$(command -v dotnet)")")"
fi
# Only a directory that really holds a .NET install (host/fxr) is useful; snap's /usr/bin is not.
[ -d "$DOTNET_ROOT/host/fxr" ] || die "Could not find the .NET install behind $(command -v dotnet) (no host/fxr under $DOTNET_ROOT). Set DOTNET_ROOT to your .NET directory and try again."

# The agent registers itself at login on its first run unless this marker exists. Login startup is
# managed by these scripts, so write the marker: the agent must not re-register after this script.
MUX_CONFIG_HOME="${MUX_CONFIG_DIR:-$HOME/.mux}"
mkdir -p "$MUX_CONFIG_HOME"
[ -f "$MUX_CONFIG_HOME/startup-registered" ] || date -u +%Y-%m-%dT%H:%M:%SZ > "$MUX_CONFIG_HOME/startup-registered"

# Replace anything scripts/linux/run-at-startup.sh may have created, so the agent does not start twice.
SERVICE="$HOME/.config/systemd/user/mux-agent.service"
DESKTOP="$HOME/.config/autostart/mux-agent.desktop"
if [ -f "$SERVICE" ]; then
    systemctl --user disable --now mux-agent >/dev/null 2>&1 || true
    rm -f "$SERVICE"
    systemctl --user daemon-reload >/dev/null 2>&1 || true
    echo "Removed the old mux-agent systemd user service."
fi
if [ -f "$DESKTOP" ]; then
    rm -f "$DESKTOP"
    echo "Removed the old desktop autostart entry."
fi

# The launcher starts from a fresh wsl.exe login, so pass the environment the agent needs explicitly.
set -- env "DOTNET_ROOT=$DOTNET_ROOT" MUX_AGENT_AUTOSTART=0
wsl_startup_install mux-agent "$@" "$AGENT_BIN"
echo "Registered $AGENT_BIN to start in WSL ($WSL_DISTRO_NAME) at Windows login."

# Start it now through the same launcher, so it keeps running after this terminal closes.
wsl_startup_run mux-agent
echo "Started the agent now (it runs the REST server)."
echo "Windows reaches the REST server on localhost through WSL localhost forwarding."
echo "Stop the running agent with: scripts/wsl2/kill-mux.sh"
