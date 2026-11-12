#!/usr/bin/env bash
# Shared by the macOS install and GUI run scripts. Caller supplies REPO_ROOT and TFM.
# install_macos_app builds one app bundle and installs it in ~/Applications and, unless MUX_SYSTEM_INSTALL=0,
# in /Applications (directly when writable, which it is for admin users, otherwise with sudo; when that is not
# possible it warns and keeps the per-user copy). Pass --user-only to skip /Applications (the run scripts do).
# MUX_SYSTEM_APPLICATIONS_DIR overrides the system folder (tests use it to stay out of the real /Applications).

# The system-wide application folder.
macos_system_apps_dir() {
    echo "${MUX_SYSTEM_APPLICATIONS_DIR:-/Applications}"
}

# Prints the macOS app name for a component (desktop or agent).
macos_app_name() {
    case "$1" in
        desktop) echo "Mux" ;;
        agent) echo "Mux Agent" ;;
        *) return 1 ;;
    esac
}

# The application folders an install writes to: always ~/Applications, plus /Applications unless
# MUX_SYSTEM_INSTALL=0.
macos_app_dirs() {
    echo "$HOME/Applications"
    if [ "${MUX_SYSTEM_INSTALL:-1}" != 0 ]; then
        macos_system_apps_dir
    fi
}

# Fails when a copy of the app is running from any install location, so a bundle is never replaced under it.
macos_app_not_running() {
    local name="$1" dir
    while IFS= read -r dir; do
        if pgrep -u "$(id -u)" -f "$dir/$name.app/Contents/MacOS/" >/dev/null 2>&1; then
            echo "$name is running from $dir. Quit it (or run scripts/macos/kill-mux.sh) and try again." >&2
            return 1
        fi
    done < <(macos_app_dirs)
}

# Runs a command directly when the target folder is writable, otherwise with sudo. Returns 1 (without
# running it) when sudo is unavailable.
macos_privileged() {
    local folder="$1"
    shift
    if [ -w "$folder" ]; then
        "$@"
    elif command -v sudo >/dev/null 2>&1; then
        sudo "$@"
    else
        return 1
    fi
}

# Copies a built bundle into an application folder, replacing any older copy, and registers it.
macos_copy_app() {
    local app="$1" folder="$2" name destination
    name="$(basename "$app")"
    destination="$folder/$name"
    if [ "$folder" = "$HOME/Applications" ]; then
        mkdir -p "$folder"
    fi
    macos_privileged "$folder" rm -rf "$destination" || return 1
    macos_privileged "$folder" cp -R "$app" "$destination" || return 1
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
        -f "$destination" >/dev/null 2>&1 || true
    echo "Installed $destination"
}

# Removes an installed bundle from an application folder (directly, else with sudo). Returns 1 when the bundle
# is still there afterwards.
macos_remove_app() {
    local name="$1" folder="$2" destination
    destination="$folder/$name.app"
    [ -e "$destination" ] || return 0
    macos_privileged "$folder" rm -rf "$destination" || true
    if [ -e "$destination" ]; then
        echo "Warning: could not remove $destination. Remove it with: sudo rm -rf \"$destination\"" >&2
        return 1
    fi
    echo "Removed $destination"
}

install_macos_app() {
    local component="$1" scope="${2:-}" name executable project version rid stage app folder
    case "$component" in
        desktop) executable="Mux.Desktop" ;;
        agent) executable="Mux.Agent" ;;
        *) echo "Unknown macOS app: $component" >&2; return 1 ;;
    esac
    name="$(macos_app_name "$component")"
    case "$(uname -m)" in
        arm64) rid=osx-arm64 ;;
        x86_64) rid=osx-x64 ;;
        *) echo "Unsupported macOS architecture: $(uname -m)" >&2; return 1 ;;
    esac
    project="$REPO_ROOT/src/$executable/$executable.csproj"
    version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)"
    [ -n "$version" ] || { echo "Cannot determine $executable version." >&2; return 1; }
    stage="$REPO_ROOT/artifacts/macos-app/$rid/$TFM/$component"
    app="$stage/$name.app"
    # Never replace a bundle under a running copy: open -a would just bring the old build forward.
    if [ "$scope" = --user-only ]; then
        if pgrep -u "$(id -u)" -f "$HOME/Applications/$name.app/Contents/MacOS/" >/dev/null 2>&1; then
            echo "$name is running. Quit it (or run scripts/macos/kill-mux.sh) and try again." >&2
            return 1
        fi
    else
        macos_app_not_running "$name" || return 1
    fi
    rm -rf "$stage"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    echo "Publishing $name $version ($rid, $TFM)..."
    dotnet publish "$project" --configuration Release --framework "$TFM" \
        -p:TargetFrameworks="$TFM" --runtime "$rid" --self-contained true \
        --output "$app/Contents/MacOS" || return
    cp "$REPO_ROOT/assets/icons/icon-macos.icns" "$app/Contents/Resources/mux.icns"
    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>$name</string>
    <key>CFBundleDisplayName</key><string>$name</string>
    <key>CFBundleIdentifier</key><string>com.jchristn.mux.$component</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleExecutable</key><string>$executable</string>
    <key>CFBundleIconFile</key><string>mux.icns</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
PLIST
    if [ "$component" = agent ]; then
        echo '    <key>LSUIElement</key><true/>' >> "$app/Contents/Info.plist"
    fi
    printf '</dict>\n</plist>\n' >> "$app/Contents/Info.plist"
    # Playwright is data plus a Node driver, not a nested app bundle. Keep it
    # under Resources so codesign does not interpret .playwright as a bundle;
    # the relative link preserves Playwright's lookup beside the managed DLL.
    if [ -d "$app/Contents/MacOS/.playwright" ]; then
        mv "$app/Contents/MacOS/.playwright" "$app/Contents/Resources/playwright"
        ln -s ../Resources/playwright "$app/Contents/MacOS/.playwright"
    fi
    chmod +x "$app/Contents/MacOS/$executable"
    codesign --force --deep --sign - "$app" || return
    # Only replace the installed apps after publishing and signing succeed.
    macos_copy_app "$app" "$HOME/Applications" || return 1
    if [ "$scope" = --user-only ] || [ "${MUX_SYSTEM_INSTALL:-1}" = 0 ]; then
        return 0
    fi
    folder="$(macos_system_apps_dir)"
    if ! macos_copy_app "$app" "$folder"; then
        echo "Warning: skipped the system-wide install of $name in $folder; the per-user install in ~/Applications is done." >&2
        echo "Re-run with administrator rights, or set MUX_SYSTEM_INSTALL=0 to skip it quietly." >&2
    fi
}
