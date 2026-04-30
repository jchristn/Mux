#!/usr/bin/env bash
set -e

resolve_framework() {
    local framework="${1:-}"

    case "$framework" in
        "")
            if dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
                FRAMEWORK="net10.0"
            else
                FRAMEWORK="net8.0"
            fi
            ;;
        -h|--help)
            echo "Usage: $(basename "$0") [net8.0|net10.0]"
            echo "Defaults to net10.0 when a .NET 10 SDK is installed, otherwise net8.0."
            exit 0
            ;;
        net8|net8.0)
            FRAMEWORK="net8.0"
            ;;
        net10|net10.0)
            FRAMEWORK="net10.0"
            ;;
        *)
            echo "Unsupported framework '$framework'."
            echo "Supported frameworks: net8.0, net10.0."
            echo "Use net8.0 on systems without a .NET 10 SDK."
            exit 1
            ;;
    esac
}

resolve_framework "${1:-}"

echo "Removing mux..."
dotnet tool uninstall -g Mux.Cli 2>/dev/null || true
echo "Building mux for $FRAMEWORK..."
dotnet pack src/Mux.Cli/Mux.Cli.csproj --configuration Release -p:TargetFrameworks="$FRAMEWORK"
echo "Installing mux..."
dotnet tool install -g --add-source src/Mux.Cli/bin/Release Mux.Cli
mux -v
