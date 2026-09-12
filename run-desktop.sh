#!/usr/bin/env bash
# Launch the mux desktop application (Avalonia).
# Usage: ./run-desktop.sh [net8.0|net10.0]
# Classic logo, kept for easy revert:
#    _____ _ _ _ _
#   |     | | |_'_|
#   |_|_|_|___|_,_|
set -e
TFM="${1:-net10.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

cat <<'EOF'

▄▄   ▄▄ ▄▄ ▄▄ ▄▄ ▄▄
██▀▄▀██ ██ ██ ▀█▄█▀
██   ██ ▀███▀ ██ ██

 mux desktop

EOF

dotnet run --project "$SCRIPT_DIR/src/Mux.Desktop/Mux.Desktop.csproj" --framework "$TFM"
