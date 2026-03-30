#!/usr/bin/env bash
set -e
echo "Building mux..."
dotnet pack src/Mux.Cli/Mux.Cli.csproj --configuration Release
echo "Installing mux..."
dotnet tool install -g --add-source src/Mux.Cli/bin/Release Mux.Cli
mux -v
