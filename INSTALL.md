# Installing mux

*Your AI agent. Your models. Your infra. Anywhere.*

Download the installer for your machine from the
[latest release](https://github.com/jchristn/Mux/releases/latest). Every installer is self-contained (no .NET SDK
needed) and installs three things: the mux desktop app, the `mux` command, and the mux tray agent, which starts when
you log in and keeps mux running in the background (it hosts the local server, the web dashboard, and the Mux Cloud
connection). To build from source instead, see [GETTING_STARTED.md](docs/GETTING_STARTED.md).

> [!IMPORTANT]
> **These builds are not code-signed, so Windows and macOS block the first launch.** This is expected and does not
> mean the download is damaged. mux is free, open-source software, and signing certificates are an ongoing cost we
> choose not to pass on. The steps to allow mux are below. To confirm a file is the one published here, check it
> against `SHA256SUMS`.

## Which file to download

| Platform | File |
| --- | --- |
| Windows (x64) | `mux-<version>-win-x64-setup.exe` |
| Windows (ARM64) | `mux-<version>-win-arm64-setup.exe` |
| macOS (Apple Silicon) | `mux-<version>-osx-arm64.pkg` (recommended) or `mux-<version>-osx-arm64.dmg` |
| macOS (Intel) | `mux-<version>-osx-x64.pkg` (recommended) or `mux-<version>-osx-x64.dmg` |
| Debian / Ubuntu | `mux_<version>_amd64.deb` (or `_arm64.deb`) |
| Fedora / RHEL | `mux-<version>-1.x86_64.rpm` (or `.aarch64.rpm`) |
| Any Linux | `mux-<version>-x86_64.AppImage` (or `-aarch64.AppImage`) |
| Scripts and containers (the `mux` command only) | `mux-<version>-<rid>.tar.gz` / `.zip` |
| Portable desktop app | `mux-desktop-<version>-<rid>.tar.gz` / `.zip` |
| .NET tool | `Mux.Cli.<version>.nupkg` |

Not sure which Mac you have? Apple menu > **About This Mac**: "Chip: Apple M..." is Apple Silicon, "Processor: Intel"
is Intel.

## macOS

The `.pkg` is the recommended installer. It installs `mux.app` into Applications, links the `mux` command at
`/usr/local/bin/mux`, and installs and starts the tray agent's LaunchAgent (`ai.latticetech.mux.agent`). It shows the
MIT license, which you agree to before installing. The `.dmg` is a drag-to-Applications copy; it can't add `mux` to
your PATH, so link it yourself afterwards:

```bash
sudo mkdir -p /usr/local/bin
sudo ln -sf /Applications/mux.app/Contents/MacOS/mux /usr/local/bin/mux
```

### "mux" Not Opened

macOS shows *"mux" Not Opened. Apple could not verify "mux" is free of malware that may harm your Mac or compromise
your privacy.* with **Move to Trash** and **Done** buttons. To open it anyway:

1. Click **Done**. Do not click Move to Trash.
2. Open **System Settings > Privacy & Security** and scroll down to **Security**.
3. Next to *"mux" was blocked to protect your Mac*, click **Open Anyway**. The button only appears for about an hour
   after the blocked attempt; if it is missing, try to open mux again first.
4. Click **Open Anyway** again in the dialog that follows and enter your password. You only do this once.

The `.pkg` gets the same prompt, named after the file. Or skip the prompt in Terminal:

```bash
xattr -dr com.apple.quarantine ~/Downloads/mux-<version>-osx-arm64.pkg   # before opening the .pkg
sudo installer -pkg ~/Downloads/mux-<version>-osx-arm64.pkg -target /    # or install it with no prompt at all
xattr -dr com.apple.quarantine /Applications/mux.app                    # after copying the app from the .dmg
xattr -d com.apple.quarantine ./mux                                     # a bare binary from a .tar.gz
```

On macOS 15 and later, Control-click > **Open** no longer bypasses this prompt. A file downloaded with `curl` never
gets the quarantine flag, so `curl ... | tar xz` installs of the command have no prompt.

### Uninstall

Quit the tray agent from its menu bar icon, then:

```bash
launchctl bootout "gui/$(id -u)/ai.latticetech.mux.agent" 2>/dev/null
rm -f ~/Library/LaunchAgents/ai.latticetech.mux.agent.plist
sudo rm -rf /Applications/mux.app
sudo rm -f /usr/local/bin/mux
sudo pkgutil --forget ai.latticetech.mux
```

Your settings and conversations stay in `~/.mux`; delete that folder to remove them too.

## Windows

Run `mux-<version>-win-x64-setup.exe` (or the ARM64 one). The installer shows the MIT license, which you accept
before installing, installs to `C:\Program Files\mux`, adds that folder to the system PATH (open a new terminal to
pick it up), adds a Start menu shortcut, and starts the tray agent, which registers itself to start when you sign in.
Upgrading from mux 1.x replaces it in place.

### "Windows protected your PC"

Click **More info**, then **Run anyway**. If your browser warns that the file *isn't commonly downloaded*, choose
**Keep** (in Edge: the **...** menu on the download, **Keep**, then **Show more > Keep anyway**). For a `.zip`, run
`Unblock-File` on it (or right-click > Properties > Unblock) before extracting; otherwise every extracted `.exe`
carries the download mark and triggers SmartScreen too:

```powershell
Unblock-File .\mux-<version>-win-x64.zip
```

If Microsoft Defender (not SmartScreen) quarantines a file as malware, that is a false positive that self-extracting
single-file executables sometimes trigger; please open an issue so we can submit it to Microsoft. Don't turn Defender
off.

### Uninstall

Use **Settings > Apps > Installed apps > mux > Uninstall**. It stops the tray agent and the app first. To stop the
agent from starting at sign-in, delete the `Mux Agent` value under
`HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`. Your settings stay in `%USERPROFILE%\.mux`.

## Linux

No prompt. Install a package with apt or dnf, which also install the declared dependencies (ICU, OpenSSL, the X11
client libraries, fontconfig); `dpkg -i` and `rpm -i` do not:

```bash
sudo apt install ./mux_<version>_amd64.deb          # the ./ is required; without it apt searches its repositories
sudo dnf install ./mux-<version>-1.x86_64.rpm
```

The packages install the app under `/opt/mux`, link `mux` into `/usr/bin`, add a menu entry and icons, and add
`/etc/xdg/autostart/ai.latticetech.mux.agent.desktop`, which starts the tray agent when you log in to a desktop
session.

The AppImage runs from wherever you put it: `chmod +x mux-<version>-x86_64.AppImage`, then run it. Older
distributions need `libfuse2` to mount it. It doesn't add `mux` to your PATH.

### Uninstall

```bash
sudo apt remove mux     # or: sudo dnf remove mux
```

Your settings stay in `~/.mux`.

## Verify a download

Each release has one `SHA256SUMS` file listing every asset. Download it into the same folder as the file, then:

```bash
shasum -a 256 -c SHA256SUMS --ignore-missing        # macOS
sha256sum -c SHA256SUMS --ignore-missing            # Linux
```

```powershell
(Get-FileHash .\mux-<version>-win-x64-setup.exe -Algorithm SHA256).Hash   # Windows: compare with SHA256SUMS
```

## The command only

For scripts, CI jobs, and containers, the `.tar.gz` / `.zip` archives hold a single self-contained `mux` binary:

```bash
curl -fsSL https://github.com/jchristn/mux/releases/download/v<version>/mux-<version>-linux-x64.tar.gz | tar xz -C /usr/local/bin mux
```

Or install the .NET tool from the release's `.nupkg`: `dotnet tool install -g Mux.Cli --add-source <folder-with-the-nupkg>`.
