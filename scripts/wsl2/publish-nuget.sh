#!/usr/bin/env bash
# Pack and publish the mux NuGet libraries (and their .snupkg symbol packages) to nuget.org.
# Each project is packed fresh in Release, so the version in its .csproj is what gets published.
# Pushing a .nupkg also uploads the matching .snupkg. Already-published versions are skipped.
# Usage: ./scripts/wsl2/publish-nuget.sh <nuget-api-key> [net8.0|net10.0]
set -euo pipefail

SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

usage() { echo "Usage: ./scripts/wsl2/publish-nuget.sh <nuget-api-key> [net8.0|net10.0]"; }
case "${1:-}" in
    -h|--help) usage; exit 0 ;;
    ''|-*|net*) usage >&2; exit 1 ;;
esac
APIKEY="$1"
shift
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
resolve_framework "$@"
shift "$FRAMEWORK_ARGS"
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
SOURCE="https://api.nuget.org/v3/index.json"
OUTDIR="$REPO_ROOT/artifacts/nuget"
# Publish in dependency order (a package's dependencies should exist first).
PROJECTS="Mux.Search Mux.Core Mux.Server Mux.Desktop.Core"

echo "Cleaning $OUTDIR"
rm -rf "$OUTDIR"
mkdir -p "$OUTDIR"

echo
echo "=== Packing ==="
# Packages ship every target framework the libraries build (src/Directory.Build.props). An explicit
# framework (argument or MUX_TFM) packs only that one; nuget.org versions cannot be replaced later.
FRAMEWORK_PROP=()
if [ "$FRAMEWORK_ARGS" -gt 0 ] || [ -n "${MUX_TFM:-}" ]; then
    FRAMEWORK_PROP=("-p:TargetFrameworks=$TFM")
    echo "Packing for $TFM only (explicit framework)."
fi
for project in $PROJECTS; do
    echo "Packing $project ..."
    dotnet pack "$REPO_ROOT/src/$project/$project.csproj" -c Release ${FRAMEWORK_PROP[@]+"${FRAMEWORK_PROP[@]}"} -o "$OUTDIR" --nologo \
        || die "pack failed for $project"
done

echo
echo "=== Publishing ==="
pushed=0
for package in "$OUTDIR"/*.nupkg; do
    [ -f "$package" ] || continue
    echo "Pushing $(basename "$package") ..."
    dotnet nuget push "$package" --api-key "$APIKEY" --source "$SOURCE" --skip-duplicate \
        || die "push failed for $(basename "$package")"
    pushed=$((pushed + 1))
done
[ "$pushed" -gt 0 ] || die "no .nupkg files were produced in $OUTDIR"

echo
echo "Done. Published $pushed package(s) and their symbol files to $SOURCE."
