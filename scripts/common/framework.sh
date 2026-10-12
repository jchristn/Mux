#!/bin/sh
# Source this helper, then shift by FRAMEWORK_ARGS to preserve application arguments.
# CLI selection takes precedence over MUX_TFM; otherwise prefer an installed .NET 10 SDK.
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
            if dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then TFM=net10.0; else TFM=net8.0; fi ;;
        net8|net8.0) TFM=net8.0 ;;
        net10|net10.0) TFM=net10.0 ;;
        *) echo "Unsupported framework '$TFM'. Supported frameworks: net8.0, net10.0." >&2; return 1 ;;
    esac
}
