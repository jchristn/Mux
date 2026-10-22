#!/usr/bin/env bash
# Shared by the macOS install and GUI run scripts. Caller supplies REPO_ROOT and TFM.
install_macos_app() {
    local component="$1" name executable project version rid stage app destination
    case "$component" in
        desktop) name="Mux"; executable="Mux.Desktop" ;;
        agent) name="Mux Agent"; executable="Mux.Agent" ;;
        *) echo "Unknown macOS app: $component" >&2; return 1 ;;
    esac
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
    destination="$HOME/Applications/$name.app"
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
    # Only replace the installed app after publishing and signing succeed.
    mkdir -p "$HOME/Applications"
    rm -rf "$destination"
    cp -R "$app" "$destination"
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
        -f "$destination" >/dev/null 2>&1 || true
    echo "Installed $destination"
}
