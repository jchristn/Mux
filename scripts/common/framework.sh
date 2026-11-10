#!/bin/sh
# Source this helper, then shift by FRAMEWORK_ARGS to preserve application arguments.
# CLI selection takes precedence over MUX_TFM; otherwise prefer an installed .NET 10 SDK.
# shellcheck disable=SC2034 # FRAMEWORK_ARGS and TFM are read by the scripts that source this file
resolve_framework() {
    FRAMEWORK_ARGS=0
    TFM="${MUX_TFM:-}"
    case "${1:-}" in
        --framework|-f)
            [ -n "${2:-}" ] || { echo "Missing framework value after $1." >&2; return 1; }
            TFM="$2"; FRAMEWORK_ARGS=2 ;;
        net*) TFM="$1"; FRAMEWORK_ARGS=1 ;;
        -h|--help)
            echo "Usage: $(basename "$0") [net8.0|net10.0] [script arguments]"
            echo "       $(basename "$0") --framework <net8.0|net10.0> [script arguments]"
            echo "Defaults to MUX_TFM, then net10.0 with a .NET 10 SDK, otherwise net8.0."
            exit 0 ;;
    esac
    case "$TFM" in
        '')
            # Capture first: under 'set -o pipefail', 'dotnet --list-sdks | grep -q' can fail with SIGPIPE
            # when grep exits early, which would silently pick net8.0 on a .NET 10 machine.
            _mux_sdks="$(dotnet --list-sdks 2>/dev/null || true)"
            case "
$_mux_sdks" in
                *"
10."*) TFM=net10.0 ;;
                *) TFM=net8.0 ;;
            esac ;;
        net8|net8.0) TFM=net8.0 ;;
        net10|net10.0) TFM=net10.0 ;;
        *) echo "Unsupported framework '$TFM'. Supported frameworks: net8.0, net10.0." >&2; return 1 ;;
    esac
}
