#!/bin/sh
# Register the mux agent (the menu-bar host that runs the local REST server) as a
# launchd user agent so it starts at login. No sudo needed; everything lives under
# ~/Library/LaunchAgents. Refreshes the Finder app and uses its self-contained agent.
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
. "$SCRIPT_DIR/../common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || { echo "Unexpected argument: $1" >&2; exit 1; }

. "$SCRIPT_DIR/app-bundle.sh"
install_macos_app agent
AGENT_BIN="$HOME/Applications/Mux Agent.app/Contents/MacOS/Mux.Agent"

LABEL="com.jchristn.mux.agent"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

if [ ! -x "$AGENT_BIN" ]; then
    echo "Mux.Agent not found. Build it first, e.g.:" >&2
    echo "  dotnet build src/Mux.Agent/Mux.Agent.csproj -c Release -f $TFM -p:TargetFrameworks=$TFM" >&2
    exit 1
fi

# KeepAlive/SuccessfulExit=false restarts the agent if it crashes, but leaves it
# stopped when it exits cleanly (the tray menu's Exit item).
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

# bootstrap is the modern verb; fall back to load -w on older macOS. Boot out any
# previously loaded copy first so re-running is a refresh.
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null || launchctl load -w "$PLIST"

echo "Installed $PLIST and started the agent (it runs the REST server)."
echo "Look for the mux icon in the menu bar."
