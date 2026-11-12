#!/usr/bin/env bash
# Desktop app and tray agent installs for the Linux, WSL and WSL 2 install, reinstall, remove, run and
# run-at-startup scripts. Source it; don't run it. The caller supplies REPO_ROOT, TFM (for publishing) and
# LINUX_APPS_KILL_MUX (the kill-mux script to name in messages, such as scripts/linux/kill-mux.sh).
#
# Each app (desktop: "Mux", Mux.Desktop; agent: "Mux Agent", Mux.Agent) is published once as a
# self-contained build under artifacts/linux-app/<rid>/<tfm>/<component>, then the whole publish
# folder (with a mux-version.txt marker) is copied to both install locations, with a launcher (.desktop
# entry) and the mux icon:
#   per-user:    ${XDG_DATA_HOME:-~/.local/share}/mux/apps/{desktop,agent}/
#                ${XDG_DATA_HOME:-~/.local/share}/applications/{mux,mux-agent}.desktop
#                ${XDG_DATA_HOME:-~/.local/share}/icons/hicolor/256x256/apps/mux.png
#   system-wide: /opt/mux/{desktop,agent}/
#                /usr/share/applications/{mux,mux-agent}.desktop
#                /usr/share/icons/hicolor/256x256/apps/mux.png
# These paths are fixed: Mux.Core's agent locator searches exactly these. The system-wide copy is
# written directly when the folders are writable (root), otherwise with sudo (which may prompt). When
# sudo is missing or fails, a warning is printed and the per-user install stands. MUX_SYSTEM_INSTALL=0
# skips the system-wide install (and removal) quietly.

# Prints the display name, executable and launcher file name of a component (desktop or agent).
linux_app_name() {
    case "$1" in
        desktop) echo "Mux" ;;
        agent) echo "Mux Agent" ;;
        *) echo "Unknown app: $1" >&2; return 1 ;;
    esac
}
linux_app_exe() {
    case "$1" in
        desktop) echo "Mux.Desktop" ;;
        agent) echo "Mux.Agent" ;;
        *) echo "Unknown app: $1" >&2; return 1 ;;
    esac
}
linux_app_launcher() {
    case "$1" in
        desktop) echo "mux.desktop" ;;
        agent) echo "mux-agent.desktop" ;;
        *) echo "Unknown app: $1" >&2; return 1 ;;
    esac
}

# The install scopes this run touches: user, plus system unless MUX_SYSTEM_INSTALL=0.
linux_app_scopes() {
    echo user
    if [ "${MUX_SYSTEM_INSTALL:-1}" != 0 ]; then
        echo system
    fi
}

# Prints the data folder (holding applications/ and icons/) of a scope.
linux_share_dir() {
    case "$1" in
        user) echo "${XDG_DATA_HOME:-$HOME/.local/share}" ;;
        system) echo "/usr/share" ;;
    esac
}

# Prints the install folder (the whole publish folder) of a component in a scope.
linux_app_dir() {
    case "$2" in
        user) echo "$(linux_share_dir user)/mux/apps/$1" ;;
        system) echo "/opt/mux/$1" ;;
    esac
}

# Every path an install in a scope creates (the app folders, launchers and the icon), for removal.
linux_app_paths() {
    local share component
    share="$(linux_share_dir "$1")"
    for component in desktop agent; do
        linux_app_dir "$component" "$1"
        echo "$share/applications/$(linux_app_launcher "$component")"
    done
    echo "$share/icons/hicolor/256x256/apps/mux.png"
}

# Fails when a copy of a component is running from the install folder of any scope this run touches
# (or only of <scope>, when given), so an app is never replaced (or removed) under a running copy.
# Usage: linux_app_not_running <component> [user|system]
linux_app_not_running() {
    local component="$1" only="${2:-}" name exe scope dir
    name="$(linux_app_name "$component")" || return 1
    exe="$(linux_app_exe "$component")" || return 1
    while IFS= read -r scope; do
        [ -z "$only" ] || [ "$scope" = "$only" ] || continue
        dir="$(linux_app_dir "$component" "$scope")"
        if pgrep -u "$(id -u)" -f "$dir/$exe" >/dev/null 2>&1; then
            echo "Error: $name is running from $dir. Quit it (or run ${LINUX_APPS_KILL_MUX:-kill-mux}) and try again." >&2
            return 1
        fi
    done < <(if [ -n "$only" ]; then echo "$only"; else linux_app_scopes; fi)
}

# True when <path>, or the nearest folder above it that exists, is writable by this user.
linux_writable() {
    local path="$1"
    while [ ! -e "$path" ] && [ "$path" != / ]; do
        path="$(dirname "$path")"
    done
    [ -w "$path" ]
}

# Runs a command for <scope>: directly for user, and for system directly when every system folder is
# writable, otherwise with sudo. Returns 1 (without running it) when sudo is needed but missing.
linux_scope_run() {
    local scope="$1" path
    shift
    if [ "$scope" = user ]; then
        "$@"
        return
    fi
    for path in /opt/mux /usr/share/applications /usr/share/icons/hicolor/256x256/apps; do
        if ! linux_writable "$path"; then
            command -v sudo >/dev/null 2>&1 || return 1
            sudo "$@"
            return
        fi
    done
    "$@"
}

# Escapes a path for an Exec= value of a .desktop entry: quoted, with the argument-level escapes for
# \ " ` $, then the string-level escape for \, and %% for %.
linux_desktop_exec() {
    local arg
    arg="$(printf '%s' "$1" | sed -e 's/[\\"`$]/\\&/g' -e 's/\\/\\\\/g' -e 's/%/%%/g')"
    printf '"%s"' "$arg"
}

# Prints the .NET runtime identifier for this machine.
linux_app_rid() {
    case "$(uname -m)" in
        x86_64|amd64) echo linux-x64 ;;
        aarch64|arm64) echo linux-arm64 ;;
        *) echo "Unsupported architecture for the desktop app and tray agent: $(uname -m)" >&2; return 1 ;;
    esac
}

# Prints the folder linux_publish_app publishes a component to.
linux_app_stage() {
    local rid
    rid="$(linux_app_rid)" || return 1
    echo "$REPO_ROOT/artifacts/linux-app/$rid/$TFM/$1"
}

# Builds one component as a self-contained publish for this machine and writes a launcher for each
# scope next to it (in <stage>-launchers). Replaces nothing that is installed.
linux_publish_app() {
    local component="$1" name exe project version rid stage scope dir launcher
    name="$(linux_app_name "$component")" || return 1
    exe="$(linux_app_exe "$component")" || return 1
    rid="$(linux_app_rid)" || return 1
    project="$REPO_ROOT/src/$exe/$exe.csproj"
    version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)"
    [ -n "$version" ] || { echo "Cannot determine the $exe version." >&2; return 1; }
    stage="$(linux_app_stage "$component")" || return 1
    rm -rf "$stage" "$stage-launchers" || return 1
    mkdir -p "$stage" "$stage-launchers" || return 1
    echo "Publishing $name $version ($rid, $TFM)..."
    dotnet publish "$project" --configuration Release --framework "$TFM" \
        -p:TargetFrameworks="$TFM" --runtime "$rid" --self-contained true \
        --output "$stage" || return
    [ -f "$stage/$exe" ] || { echo "$exe was not found in $stage after publishing." >&2; return 1; }
    chmod +x "$stage/$exe" || return 1
    # The version marker the run scripts compare with the checkout (linux_installed_version).
    printf '%s\n' "$version" > "$stage/mux-version.txt" || return 1
    # The launchers differ only in the absolute path of the installed executable.
    for scope in user system; do
        dir="$(linux_app_dir "$component" "$scope")"
        launcher="$stage-launchers/$scope.desktop"
        {
            echo "[Desktop Entry]"
            echo "Type=Application"
            echo "Version=1.5"
            echo "Name=$name"
            if [ "$component" = agent ]; then
                echo "Comment=Tray host for the mux server (runs the local REST server)"
            else
                echo "Comment=Desktop app for mux"
            fi
            echo "Exec=$(linux_desktop_exec "$dir/$exe")"
            echo "TryExec=$(printf '%s' "$dir/$exe" | sed 's/\\/\\\\/g')"
            echo "Icon=mux"
            echo "Categories=Development;"
            echo "Terminal=false"
        } > "$launcher" || return 1
    done
}

# Copies a published component (from linux_publish_app) into one scope, replacing an older copy only
# once the new one is in place beside it, and writes its launcher and the icon.
linux_copy_app() {
    local component="$1" scope="$2" exe stage dir share apps icons
    exe="$(linux_app_exe "$component")" || return 1
    stage="$(linux_app_stage "$component")" || return 1
    dir="$(linux_app_dir "$component" "$scope")"
    share="$(linux_share_dir "$scope")"
    apps="$share/applications"
    icons="$share/icons/hicolor/256x256/apps"
    [ -f "$stage/$exe" ] || { echo "$component was not published (missing $stage/$exe)." >&2; return 1; }
    linux_scope_run "$scope" mkdir -p "$(dirname "$dir")" "$apps" "$icons" || return 1
    linux_scope_run "$scope" rm -rf "$dir.new" || return 1
    linux_scope_run "$scope" cp -R "$stage" "$dir.new" || return 1
    # A system copy must be readable and runnable by every user.
    linux_scope_run "$scope" chmod -R u+rwX,go+rX,go-w "$dir.new" || return 1
    linux_scope_run "$scope" rm -rf "$dir" || return 1
    linux_scope_run "$scope" mv "$dir.new" "$dir" || return 1
    linux_scope_run "$scope" cp "$stage-launchers/$scope.desktop" "$apps/$(linux_app_launcher "$component")" || return 1
    linux_scope_run "$scope" cp "$REPO_ROOT/assets/icons/icon-green.png" "$icons/mux.png" || return 1
    linux_scope_run "$scope" chmod 644 "$apps/$(linux_app_launcher "$component")" "$icons/mux.png" || return 1
    # Refresh the menu and icon caches when the tools exist; desktops also notice changes without them.
    if command -v update-desktop-database >/dev/null 2>&1; then
        linux_scope_run "$scope" update-desktop-database -q "$apps" >/dev/null 2>&1 || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1 && [ -f "$share/icons/hicolor/index.theme" ]; then
        linux_scope_run "$scope" gtk-update-icon-cache -q -t "$share/icons/hicolor" >/dev/null 2>&1 || true
    fi
    echo "Installed $dir/$exe"
}

# Installs both published components per-user, then system-wide unless MUX_SYSTEM_INSTALL=0. A failed
# per-user install fails; a failed system-wide install only warns (exit status 0).
linux_install_apps() {
    local component
    for component in desktop agent; do
        linux_copy_app "$component" user || return 1
    done
    [ "${MUX_SYSTEM_INSTALL:-1}" != 0 ] || return 0
    if ! linux_writable /opt/mux && command -v sudo >/dev/null 2>&1; then
        echo "Installing system-wide in /opt/mux (sudo may ask for your password; MUX_SYSTEM_INSTALL=0 skips this)..."
    fi
    for component in desktop agent; do
        if ! linux_copy_app "$component" system; then
            echo "Warning: skipped the system-wide install of the desktop app and tray agent in /opt/mux; the per-user install is done." >&2
            echo "Re-run with sudo rights, or set MUX_SYSTEM_INSTALL=0 to skip it quietly." >&2
            return 0
        fi
    done
}

# Publishes one component and installs it per-user only, with its launcher and the icon. It never
# touches the system-wide copy (so it never needs sudo): the run-at-startup scripts use it to install
# or refresh the agent they register, and linux_ensure_user_agent (run-desktop, run-tui) to install or
# refresh the agent a surface starts. Fails before building when the component runs from the per-user
# folder. The installed executable is "$(linux_app_dir <component> user)/$(linux_app_exe <component>)".
# Usage: linux_install_user_app desktop|agent
linux_install_user_app() {
    linux_app_not_running "$1" user || return 1
    linux_publish_app "$1" || return 1
    linux_copy_app "$1" user
}

# Prints this checkout's version of a component (the <Version> in its project file).
linux_app_version() {
    local exe version
    exe="$(linux_app_exe "$1")" || return 1
    version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/src/$exe/$exe.csproj" 2>/dev/null | head -n 1)"
    [ -n "$version" ] || { echo "Cannot determine the $exe version." >&2; return 1; }
    echo "$version"
}

# Prints the version recorded in an installed component (its mux-version.txt), or nothing when it is
# not installed or has no marker (an install from before the marker existed).
# Usage: linux_installed_version <component> [user|system]
linux_installed_version() {
    local marker
    marker="$(linux_app_dir "$1" "${2:-user}")/mux-version.txt"
    [ -f "$marker" ] || return 0
    head -n 1 "$marker" | tr -d '\r'
}

# True unless MUX_AGENT_AUTOSTART turns automatic agent starts off (0, false, no or off, any case), as
# the surfaces themselves read it.
linux_agent_autostart_enabled() {
    case "$(printf '%s' "${MUX_AGENT_AUTOSTART:-}" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')" in
        0|false|no|off) return 1 ;;
    esac
    return 0
}

# Before a user surface starts (run-desktop, run-tui): makes sure the per-user tray agent is installed
# and matches this checkout's version, so the surface (and linux_start_user_agent) start a current
# agent. Installs or refreshes it with linux_install_user_app only when it is missing, its version
# differs, or MUX_REFRESH_AGENT=1, so the common case adds no build. Never replaces a copy running
# from the per-user folder: a current one is left alone, a stale one gets a one-line note. Does nothing
# when MUX_AGENT_AUTOSTART turns automatic starts off. Fails only when the install fails.
linux_ensure_user_agent() {
    local want have dir
    linux_agent_autostart_enabled || return 0
    want="$(linux_app_version agent)" || return 1
    have="$(linux_installed_version agent user)"
    dir="$(linux_app_dir agent user)"
    if pgrep -u "$(id -u)" -f "$dir/Mux.Agent" >/dev/null 2>&1; then
        if [ "$have" != "$want" ]; then
            echo "Note: the running tray agent is an older version (${have:-unknown}, this checkout is $want). Quit it (or run ${LINUX_APPS_KILL_MUX:-kill-mux}) to update it." >&2
        elif [ "${MUX_REFRESH_AGENT:-0}" = 1 ]; then
            echo "Note: the tray agent is running, so MUX_REFRESH_AGENT=1 cannot refresh it. Quit it (or run ${LINUX_APPS_KILL_MUX:-kill-mux}) first." >&2
        fi
        return 0
    fi
    if [ -x "$dir/Mux.Agent" ] && [ "$have" = "$want" ] && [ "${MUX_REFRESH_AGENT:-0}" != 1 ]; then
        return 0
    fi
    if [ ! -x "$dir/Mux.Agent" ]; then
        echo "The tray agent is not installed; installing it per-user ($want)..."
    elif [ "$have" != "$want" ]; then
        echo "Updating the per-user tray agent (${have:-unknown} -> $want)..."
    else
        echo "Refreshing the per-user tray agent (MUX_REFRESH_AGENT=1)..."
    fi
    linux_install_user_app agent
}

# Starts the per-user tray agent detached from this terminal, unless an agent is already running (from
# anywhere), it is not installed, or MUX_AGENT_AUTOSTART turns automatic starts off. It runs with
# MUX_AGENT_AUTOSTART=0 so it never registers itself at login (that is run-at-startup's job).
linux_start_user_agent() {
    local bin
    linux_agent_autostart_enabled || return 0
    pgrep -u "$(id -u)" -x Mux.Agent >/dev/null 2>&1 && return 0
    bin="$(linux_app_dir agent user)/Mux.Agent"
    [ -x "$bin" ] || return 0
    if command -v setsid >/dev/null 2>&1; then
        MUX_AGENT_AUTOSTART=0 setsid nohup "$bin" </dev/null >/dev/null 2>&1 &
    else
        MUX_AGENT_AUTOSTART=0 nohup "$bin" </dev/null >/dev/null 2>&1 &
    fi
    echo "Started the tray agent ($bin)."
}

# Removes everything an install created in one scope (directly, else with sudo). Warns, naming what is
# left, when something could not be removed; never fails.
linux_remove_scope() {
    local scope="$1" path found="" left="" share
    while IFS= read -r path; do
        [ -e "$path" ] && found="$found $path"
    done < <(linux_app_paths "$scope")
    if [ -z "$found" ]; then
        echo "The $scope install of the desktop app and tray agent is not present."
        return 0
    fi
    while IFS= read -r path; do
        [ -e "$path" ] || continue
        linux_scope_run "$scope" rm -rf "$path" >/dev/null 2>&1 || true
        if [ -e "$path" ]; then
            left="$left $path"
        else
            echo "Removed $path"
        fi
    done < <(linux_app_paths "$scope")
    share="$(linux_share_dir "$scope")"
    # Drop the folders the install made when they are now empty.
    if [ "$scope" = system ]; then
        linux_scope_run system rmdir /opt/mux >/dev/null 2>&1 || true
    else
        rmdir "$share/mux/apps" "$share/mux" >/dev/null 2>&1 || true
    fi
    if command -v update-desktop-database >/dev/null 2>&1 && [ -d "$share/applications" ]; then
        linux_scope_run "$scope" update-desktop-database -q "$share/applications" >/dev/null 2>&1 || true
    fi
    if [ -n "$left" ]; then
        echo "Warning: could not remove the $scope install of the desktop app and tray agent. Still installed:$left" >&2
        if [ "$scope" = system ]; then
            echo "Re-run with sudo rights, or remove them with: sudo rm -rf$left" >&2
        fi
    fi
    return 0
}

# Removes the per-user install, and the system-wide one unless MUX_SYSTEM_INSTALL=0 (then it only
# notes that a system-wide copy is left).
linux_remove_apps() {
    local path
    linux_remove_scope user
    if [ "${MUX_SYSTEM_INSTALL:-1}" != 0 ]; then
        linux_remove_scope system
        return 0
    fi
    while IFS= read -r path; do
        if [ -e "$path" ]; then
            echo "Note: MUX_SYSTEM_INSTALL=0, so the system-wide install in /opt/mux was left in place."
            break
        fi
    done < <(linux_app_paths system)
}
