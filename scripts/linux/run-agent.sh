#!/usr/bin/env bash
# Launch the mux system-tray agent (hosts the local REST server in the background).
# Usage: ./scripts/linux/run-agent.sh [net8.0|net10.0]
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
. "$SCRIPT_DIR/../common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || { echo "Unexpected argument: $1" >&2; exit 1; }
dotnet run --project "$REPO_ROOT/src/Mux.Agent/Mux.Agent.csproj" --framework "$TFM" -p:TargetFrameworks="$TFM"
