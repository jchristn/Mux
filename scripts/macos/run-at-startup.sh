#!/usr/bin/env bash
# Register the mux agent (the menu-bar host that runs the local REST server) as a launchd user
# agent so it starts at login, and start it now. No sudo needed; everything lives under
# ~/Library/LaunchAgents. Stops a launchd-managed agent, then builds and installs
# ~/Applications/Mux Agent.app and runs that copy (quit any other running agent first).
# launchd restarts the agent if it crashes, and leaves it stopped after a clean Exit.
# Usage: ./scripts/macos/run-at-startup.sh [net8.0|net10.0]
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
LABEL="com.jchristn.mux.agent"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

# Stop a launchd-managed copy first (so the app can be replaced), and wait until launchd lets it go;
# bootstrapping again too early fails with "Bootstrap failed: 5: Input/output error".
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
for _ in 1 2 3 4 5 6 7 8 9 10; do
    launchctl print "gui/$(id -u)/$LABEL" >/dev/null 2>&1 || break
    sleep 0.5
done

if pgrep -u "$(id -u)" -x Mux.Agent >/dev/null 2>&1; then
    die "Another mux agent is running (for example from /Applications or a source build). Quit it (or run scripts/macos/kill-mux.sh) and try again."
fi

# shellcheck source=app-bundle.sh
. "$SCRIPT_DIR/app-bundle.sh"
install_macos_app agent
AGENT_BIN="$HOME/Applications/Mux Agent.app/Contents/MacOS/Mux.Agent"
[ -x "$AGENT_BIN" ] || die "Mux.Agent was not found at $AGENT_BIN after the build."
# The agent registers itself at login on its first run unless this marker exists. Login startup is
# managed by these scripts, so write the marker: the agent must not re-register after this script.
MUX_CONFIG_HOME="${MUX_CONFIG_DIR:-$HOME/.mux}"
mkdir -p "$MUX_CONFIG_HOME"
[ -f "$MUX_CONFIG_HOME/startup-registered" ] || date -u +%Y-%m-%dT%H:%M:%SZ > "$MUX_CONFIG_HOME/startup-registered"

mkdir -p "$HOME/Library/LaunchAgents"
cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$LABEL</string>
    <key>ProgramArguments</key>
    <array>
        <string>$AGENT_BIN</string>
    </array>
    <key>EnvironmentVariables</key>
    <dict>
        <key>MUX_AGENT_AUTOSTART</key>
        <string>0</string>
    </dict>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <dict>
        <key>SuccessfulExit</key>
        <false/>
    </dict>
</dict>
</plist>
EOF

# bootstrap is the modern verb; fall back to load -w on older macOS.
launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null || launchctl load -w "$PLIST"

echo "Installed $PLIST and started the agent (it runs the REST server)."
echo "Look for the mux icon in the menu bar."
