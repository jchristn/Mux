#!/usr/bin/env bash
# Build the mux linux installers (.deb/.rpm/AppImage) and drop them under installers/<version>/.
# Native installer formats are OS-locked, so this builds only the linux channels. Run
# build-installers on each OS (scripts\windows\build-installers.bat on Windows) for the full set, or
# push a version tag to let the GitHub Actions matrix build all three (.github/workflows/release.yml).
#
# VERSION   defaults to the <Version> in src/Mux.Cli/Mux.Cli.csproj.
# --dry-run prints the plan (files + commands) without building or needing the packagers.
# WSL 2 version: builds the Linux installers with the Linux toolchain inside this distro.
# Usage: ./scripts/wsl2/build-installers.sh [VERSION] [--framework net8.0|net10.0] [--dry-run]
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

wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# shellcheck source=../common/framework.sh
. "$REPO_ROOT/scripts/common/framework.sh"
VERSION=""
DRYRUN=""
FRAMEWORK_SELECTION="${MUX_TFM:-}"
while [ "$#" -gt 0 ]; do
    case "$1" in
        --dry-run) DRYRUN="--dry-run"; shift ;;
        --framework|-f)
            [ -n "${2:-}" ] || die "Missing framework value after $1."
            FRAMEWORK_SELECTION="$2"; shift 2 ;;
        net*) FRAMEWORK_SELECTION="$1"; shift ;;
        -h|--help) echo "Usage: ./scripts/wsl2/build-installers.sh [VERSION] [--framework net8.0|net10.0] [--dry-run]"; exit 0 ;;
        -*) die "Unknown option: $1" ;;
        *) VERSION="$1"; shift ;;
    esac
done
if [ -n "$FRAMEWORK_SELECTION" ]; then resolve_framework --framework "$FRAMEWORK_SELECTION"; else resolve_framework; fi

if [ -z "$VERSION" ]; then
    VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/src/Mux.Cli/Mux.Cli.csproj" | head -n 1)"
fi
[ -n "$VERSION" ] || die "could not determine the version. Pass it explicitly: ./scripts/wsl2/build-installers.sh 0.10.0"

OS_JOB="linux"
OUT="$REPO_ROOT/installers/$VERSION"
STAGING="$REPO_ROOT/dist/staging"
PUBLISHER="$REPO_ROOT/src/Mux.Publisher/Mux.Publisher.csproj"
PUB_DLL="$REPO_ROOT/src/Mux.Publisher/bin/Release/$TFM/Mux.Publisher.dll"

echo "mux installers"
echo "  version : $VERSION"
echo "  host os : $OS_JOB"
echo "  output  : $OUT"
if [ -n "$DRYRUN" ]; then echo "  mode    : DRY RUN (no artifacts produced)"; fi
echo

mkdir -p "$OUT"

echo "Building Mux.Publisher..."
dotnet build "$PUBLISHER" -c Release -f "$TFM" -p:TargetFrameworks="$TFM" -v quiet -nologo || die "failed to build Mux.Publisher."

CHANNELS="$(dotnet "$PUB_DLL" --channels-for "$OS_JOB" --repo "$REPO_ROOT")" || die "Mux.Publisher could not list the $OS_JOB channels."
if [ -z "$CHANNELS" ]; then
    echo "No enabled channels for $OS_JOB in publisher.json."
    exit 0
fi
echo "Channels for $OS_JOB: $CHANNELS"
echo

FAILED=""
for ch in $CHANNELS; do
    echo "==> $ch"
    # shellcheck disable=SC2086 # DRYRUN is empty or a single flag
    if ! dotnet "$PUB_DLL" --framework "$TFM" --channel "$ch" --version "$VERSION" --repo "$REPO_ROOT" \
        --out "$OUT" --staging "$STAGING" $DRYRUN; then
        echo "    (channel '$ch' failed; is its packaging tool installed?)"
        FAILED="$FAILED $ch"
    fi
    echo
done

echo "-------------------------------------------------------------------"
if [ -n "$DRYRUN" ]; then
    echo "Dry run complete for $OS_JOB (nothing built)."
else
    echo "Done. $OS_JOB installers are under: $OUT"
    find "$OUT" -maxdepth 2 -type f ! -path '*/publish/*' | sed "s|$OUT/|  |" | sort
fi
if [ -n "$FAILED" ]; then
    echo
    echo "Channels that failed:$FAILED"
    echo "Install the missing packagers (fpm, rpm, appimagetool, createrepo_c, dpkg-dev) and re-run, or use --dry-run to preview."
    exit 1
fi
echo
echo "Note: this built only the $OS_JOB installers. Run build-installers on the other OSes for the full set,"
echo "or push a 'v$VERSION' tag to build all three via GitHub Actions."
