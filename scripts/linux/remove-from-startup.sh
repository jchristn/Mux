#!/bin/sh
# Deregister the mux agent from starting at login. Removes both the systemd --user
# service and the desktop autostart entry if present. A currently running agent is
# left alone; exit it from the tray menu to stop it now.
set -eu

SERVICE="$HOME/.config/systemd/user/mux-agent.service"
DESKTOP="$HOME/.config/autostart/mux-agent.desktop"
did=0

if command -v systemctl >/dev/null 2>&1 && [ -f "$SERVICE" ]; then
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
echo "A running agent keeps running until you exit it from the tray menu."
