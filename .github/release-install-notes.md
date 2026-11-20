## Installing

> [!IMPORTANT]
> **These builds are not code-signed, so Windows and macOS block the first launch.** This is expected and does not
> mean the download is damaged. mux is free, open-source software, and signing certificates are an ongoing cost we
> choose not to pass on. The steps to allow mux are below. To confirm a file is the one published here, check it
> against `SHA256SUMS`.

### macOS: "mux" Not Opened

macOS shows *"mux" Not Opened. Apple could not verify "mux" is free of malware that may harm your Mac or compromise
your privacy.* with **Move to Trash** and **Done** buttons. To open it anyway:

1. Click **Done**. Do not click Move to Trash.
2. Open **System Settings > Privacy & Security** and scroll down to **Security**.
3. Next to *"mux" was blocked to protect your Mac*, click **Open Anyway**. The button only appears for about an hour
   after the blocked attempt; if it is missing, try to open mux again first.
4. Click **Open Anyway** again in the dialog that follows and enter your password. You only do this once.

The same steps apply to the `.pkg` installer, which macOS names after the file. Or skip the prompt in Terminal:

```bash
xattr -dr com.apple.quarantine ~/Downloads/mux-{version}-osx-arm64.pkg   # before opening the .pkg
sudo installer -pkg ~/Downloads/mux-{version}-osx-arm64.pkg -target /    # or install it with no prompt at all
xattr -dr com.apple.quarantine /Applications/mux.app                    # after copying the app from the .dmg
```

On macOS 15 and later, Control-click > **Open** no longer bypasses this prompt.

### Windows: "Windows protected your PC"

Click **More info**, then **Run anyway**. If your browser warns that the file *isn't commonly downloaded*, choose
**Keep** (in Edge: the **...** menu on the download, **Keep**, then **Show more > Keep anyway**). For a `.zip`, run
`Unblock-File` on it (or right-click > Properties > Unblock) before extracting.

### Linux

No prompt. `sudo apt install ./mux_{version}_amd64.deb`, `sudo dnf install ./mux-{version}-1.x86_64.rpm`, or
`chmod +x` the AppImage and run it.

### Which file to download

| Platform | File |
| --- | --- |
| Windows (x64) | `mux-{version}-win-x64-setup.exe` |
| Windows (ARM64) | `mux-{version}-win-arm64-setup.exe` |
| macOS (Apple Silicon) | `mux-{version}-osx-arm64.pkg` (recommended) or `mux-{version}-osx-arm64.dmg` |
| macOS (Intel) | `mux-{version}-osx-x64.pkg` (recommended) or `mux-{version}-osx-x64.dmg` |
| Debian / Ubuntu | `mux_{version}_amd64.deb` (or `_arm64.deb`) |
| Fedora / RHEL | `mux-{version}-1.x86_64.rpm` (or `.aarch64.rpm`) |
| Any Linux | `mux-{version}-x86_64.AppImage` (or `-aarch64.AppImage`) |
| Scripts and containers (the `mux` command only) | `mux-{version}-<rid>.tar.gz` / `.zip` |
| Portable desktop app | `mux-desktop-{version}-<rid>.tar.gz` / `.zip` |
| .NET tool | `Mux.Cli.{version}.nupkg` (`dotnet tool install -g Mux.Cli --add-source <folder>`) |

Every installer includes the desktop app, the `mux` command, and the tray agent, which starts when you log in (set
`MUX_AGENT_AUTOSTART=0` to keep it from registering). The `.pkg`, the Windows installer, and the `.deb`/`.rpm` put
`mux` on your PATH; with the `.dmg`, link it yourself (the note inside the disk image shows how).

Not sure which Mac you have? Apple menu > **About This Mac**: "Chip: Apple M..." is Apple Silicon, "Processor: Intel"
is Intel.

Full instructions: [INSTALL.md](https://github.com/jchristn/Mux/blob/main/INSTALL.md). To build from source, use the
**Source code** archives below and follow [GETTING_STARTED.md](https://github.com/jchristn/Mux/blob/main/docs/GETTING_STARTED.md).
