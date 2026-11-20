#!/usr/bin/env bash
# Builds the GitHub Release body for a version, or checks that it can be built.
#
#   .github/scripts/release-notes.sh <version>           print the notes
#   .github/scripts/release-notes.sh <version> --check   exit non-zero if CHANGELOG.md lacks the section
#
# The body is .github/release-install-notes.md (the Installing section, with {version} substituted), then
# .github/release-highlights/<version>.md when it exists, then "## What's changed" and the CHANGELOG.md section
# whose heading starts with "## [<version>]".
set -euo pipefail

v="${1:-}"
[ -n "$v" ] || { echo "usage: $0 <version> [--check]" >&2; exit 2; }
root="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"

section="$(awk -v h="## [$v]" '
    index($0, h) == 1 { on = 1; next }
    on && /^## \[/ { exit }
    on { print }
' "$root/CHANGELOG.md")"

if [ -z "$(printf '%s' "$section" | tr -d '[:space:]')" ]; then
    echo "CHANGELOG.md has no non-empty section whose heading starts with '## [$v]'." >&2
    exit 1
fi
[ "${2:-}" = "--check" ] && exit 0

sed "s/{version}/$v/g" "$root/.github/release-install-notes.md"
highlights="$root/.github/release-highlights/$v.md"
if [ -f "$highlights" ]; then
    echo
    cat "$highlights"
fi
echo
echo "## What's changed"
printf '%s\n' "$section"
