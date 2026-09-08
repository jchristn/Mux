#!/bin/sh
# Register the mux agent (the tray host that runs the local REST server) to start
# at login. Prefers a systemd --user service (works for desktops and headless
# servers, restarts on failure); falls back to a desktop autostart entry when
# systemd --user isn't available. No root needed. Resolves the agent binary from
# dist/ (published layout) first, then the dev build output.
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

AGENT_BIN="$REPO_ROOT/dist/Mux.Agent"
[ -x "$AGENT_BIN" ] || AGENT_BIN="$REPO_ROOT/src/Mux.Agent/bin/Release/net10.0/Mux.Agent"
[ -x "$AGENT_BIN" ] || AGENT_BIN="$REPO_ROOT/src/Mux.Agent/bin/Release/net8.0/Mux.Agent"
[ -x "$AGENT_BIN" ] || AGENT_BIN="$REPO_ROOT/src/Mux.Agent/bin/Debug/net10.0/Mux.Agent"

SERVICE="$HOME/.config/systemd/user/mux-agent.service"
DESKTOP="$HOME/.config/autostart/mux-agent.desktop"

if [ ! -x "$AGENT_BIN" ]; then
    echo "Mux.Agent not found. Build it first, e.g.:" >&2
    echo "  dotnet build src/Mux.Agent/Mux.Agent.csproj -c Release" >&2
    exit 1
fi

if command -v systemctl >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1; then
    mkdir -p "$(dirname "$SERVICE")"
    cat > "$SERVICE" <<EOF
[Unit]
Description=mux tray agent (hosts the local REST server)

[Service]
ExecStart=$AGENT_BIN
Restart=on-failure

[Install]
WantedBy=default.target
EOF
    systemctl --user daemon-reload
    systemctl --user enable --now mux-agent
    echo "Installed and started the mux-agent systemd user service (runs the REST server)."
    echo "Check it with: systemctl --user status mux-agent"
    echo "On a headless server, also run 'loginctl enable-linger \$USER' once so it"
    echo "runs without anyone logged in."
else
    mkdir -p "$(dirname "$DESKTOP")"
    cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=mux Agent
Comment=mux tray agent (hosts the local REST server)
Exec=$AGENT_BIN
X-GNOME-Autostart-enabled=true
EOF
    echo "Installed $DESKTOP; the agent will start at your next desktop login."
    if pgrep -f Mux.Agent >/dev/null 2>&1; then
        echo "The agent is already running."
    else
        "$AGENT_BIN" >/dev/null 2>&1 &
        echo "Started the agent now."
    fi
fi

echo "Note: the tray icon needs StatusNotifierItem support (KDE Plasma has it built in;"
echo "GNOME needs the 'AppIndicator and KStatusNotifierItem' extension). Without a tray,"
echo "the agent still runs the REST server."
