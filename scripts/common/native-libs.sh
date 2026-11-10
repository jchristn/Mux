#!/bin/sh
# Native-library checks for the Linux scripts. Source it; don't run it. (The WSL scripts get the same
# checks from scripts/wsl*/_wsl-common.sh.) Building never loads these libraries, so without a check a
# missing one only shows when the app starts, as "Unable to load shared library ...".
native_log() { printf '%s\n' "$*" >&2; }

# --- Native libraries -------------------------------------------------------------------------------
# A desktop distro ships these client libraries, but minimal and server images (and WSL, where WSLg
# supplies only the display server) often lack them. Building never loads them, so
# without a check the failure only shows when the app starts, as "Unable to load shared library ...".

# Libraries a .NET GUI app built on Avalonia 12 loads on Linux: the sonames inside Avalonia.X11.dll
# (12.1.x) plus SkiaSharp's fontconfig. libGL, GTK 3 and Vulkan are optional (software rendering and
# managed dialogs are the fallbacks). Each entry is soname:Debian/Ubuntu package:Fedora package.
# shellcheck disable=SC2034 # used by the scripts that source this file
NATIVE_AVALONIA_LIBS="libX11.so.6:libx11-6:libX11 libICE.so.6:libice6:libICE libSM.so.6:libsm6:libSM libXext.so.6:libxext6:libXext libXrandr.so.2:libxrandr2:libXrandr libXi.so.6:libxi6:libXi libXcursor.so.1:libxcursor1:libXcursor libXfixes.so.3:libxfixes3:libXfixes libfontconfig.so.1:libfontconfig1:fontconfig"

# Prints the ldconfig cache (ldconfig -p), or nothing when there is no ldconfig to ask.
# MUX_LDCONFIG overrides the ldconfig command (used by tests).
native_ldconfig_cache() {
    _wl_ldc="${MUX_LDCONFIG:-}"
    if [ -z "$_wl_ldc" ]; then
        for _wl_c in ldconfig /sbin/ldconfig /usr/sbin/ldconfig; do
            if command -v "$_wl_c" >/dev/null 2>&1; then _wl_ldc="$_wl_c"; break; fi
        done
    fi
    [ -n "$_wl_ldc" ] || return 0
    "$_wl_ldc" -p 2>/dev/null || true
}

# True when the dynamic linker has <soname> (exact name, or a prefix ending in a dot such as
# "libicuuc.so.") for this machine's architecture. Usage: native_have_lib <cache> <soname>
native_have_lib() {
    case "$(uname -m)" in
        x86_64|amd64) _wl_arch=x86-64 ;;
        aarch64|arm64) _wl_arch=AArch64 ;;
        *) _wl_arch="" ;;
    esac
    printf '%s\n' "$1" | awk -v so="$2" -v arch="$_wl_arch" '
        { name = $1 }
        (name == so || (substr(so, length(so)) == "." && index(name, so) == 1)) && (arch == "" || index($0, arch)) { found = 1 }
        END { exit !found }'
}

# Prints the install command for the given Debian and Fedora package lists.
# Usage: native_install_hint "<deb packages>" "<rpm packages>"
native_install_hint() {
    if command -v apt-get >/dev/null 2>&1; then
        printf '  sudo apt install %s\n' "$1"
    elif command -v dnf >/dev/null 2>&1; then
        printf '  sudo dnf install %s\n' "$2"
    else
        printf '  Debian/Ubuntu: sudo apt install %s\n  Fedora:        sudo dnf install %s\n' "$1" "$2"
    fi
}

# Reports libraries from <list> that are missing, with the command that installs them. Does nothing
# when they are all present or when there is no ldconfig to ask (it never guesses).
# Usage: native_check_libs error|warning "<app name>" "<soname:deb:rpm list>"
#   error   exits (use before launching the app)
#   warning returns (use after installing it)
native_check_libs() {
    _wl_cache="$(native_ldconfig_cache)"
    [ -n "$_wl_cache" ] || return 0
    _wl_missing=""; _wl_deb=""; _wl_rpm=""
    for _wl_e in $3; do
        _wl_so="${_wl_e%%:*}"
        native_have_lib "$_wl_cache" "$_wl_so" && continue
        _wl_r="${_wl_e#*:}"
        _wl_missing="$_wl_missing $_wl_so"; _wl_deb="$_wl_deb ${_wl_r%%:*}"; _wl_rpm="$_wl_rpm ${_wl_r#*:}"
    done
    [ -n "$_wl_missing" ] || return 0
    if [ "$1" = error ]; then
        native_log "Error: $2 cannot start without these shared libraries:$_wl_missing"
    else
        native_log "Warning: $2 is installed, but it cannot start until these shared libraries are installed:$_wl_missing"
    fi
    native_log "Without them the app fails at startup with"
    native_log "\"Unable to load shared library '...'\". Install them with:"
    native_log "$(native_install_hint "${_wl_deb# }" "${_wl_rpm# }")"
    if [ "$1" = error ]; then exit 1; fi
    return 0
}

# .NET needs ICU (libicu) for globalization: the SDK itself and every app that does not run with
# invariant globalization. dotnet-install.sh and self-contained apps do not bring it. Exits with the
# install command when it is missing; does nothing when DOTNET_SYSTEM_GLOBALIZATION_INVARIANT is set
# to 1/true, or when there is no ldconfig to ask.
native_check_icu() {
    case "${DOTNET_SYSTEM_GLOBALIZATION_INVARIANT:-}" in 1|true|TRUE|True) return 0 ;; esac
    _wl_cache="$(native_ldconfig_cache)"
    [ -n "$_wl_cache" ] || return 0
    native_have_lib "$_wl_cache" "libicuuc.so." && return 0
    _wl_icu=""
    if command -v apt-cache >/dev/null 2>&1; then
        _wl_icu="$(apt-cache pkgnames libicu 2>/dev/null | awk '/^libicu[0-9]+(t64)?$/' | sort -V | tail -n 1 || true)"
    fi
    native_log "Error: .NET needs ICU (libicu), and this system does not have it. Without it dotnet fails with"
    native_log "\"Couldn't find a valid ICU package installed on the system\". Install it with:"
    native_log "$(native_install_hint "${_wl_icu:-libicu-dev}" libicu)"
    exit 1
}

# Warns when no clipboard tool is installed: terminal apps (TUIKit and the apps built on it) copy by
# running wl-copy, xclip or xsel, and minimal installs often have none. Usage: native_check_clipboard "<app name>"
native_check_clipboard() {
    for _wl_t in wl-copy xclip xsel; do
        command -v "$_wl_t" >/dev/null 2>&1 && return 0
    done
    native_log "Note: copy to clipboard will not work in $1 until a clipboard tool is installed:"
    native_log "$(native_install_hint xclip xclip)"
}
