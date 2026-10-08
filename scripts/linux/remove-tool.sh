#!/usr/bin/env bash
set -e
echo "Removing mux..."
dotnet tool uninstall -g Mux.Cli
