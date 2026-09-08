#!/bin/sh
# Deregister the mux agent from starting at login (unloads and removes the launchd
# user agent plist). A currently running agent is left alone; exit it from the
# menu-bar menu to stop it now.
set -eu

LABEL="com.jchristn.mux.agent"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

if [ ! -f "$PLIST" ]; then
    echo "The agent is not registered to run at login; nothing to do."
    exit 0
fi

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || launchctl unload -w "$PLIST" 2>/dev/null || true
rm -f "$PLIST"
echo "Removed $PLIST. A running agent keeps running until you exit it from the menu bar."
