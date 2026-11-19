# Release plan: installers on GitHub Releases

Plan for building unsigned installers for macOS, Windows, and Linux and publishing them to
[GitHub Releases](https://github.com/jchristn/Mux/releases), written 2026-11-19 for the 2.0.0 release (Mux Cloud).

## 1. What already exists

The pipeline is built and has shipped five releases (v1.3.0 to v1.5.0).

| Piece | Where | State |
| --- | --- | --- |
| Channel definitions | `publisher.json` | 16 channels; 12 enabled |
| Packager | `src/Mux.Publisher` | Builds every channel; skips a channel (with a note) when its secret is missing |
| Release workflow | `.github/workflows/release.yml` | Manual (`workflow_dispatch`, `version`, `publish`); Windows, macOS, and Linux runners in parallel; uploads to the `v<version>` release with `SHA256SUMS-*.txt` |
| Release check | `.github/workflows/release-check.yml` | Packages without publishing on pushes to `main` that touch the publisher |
| Local builds | `scripts/<os>/build-installers` | Builds the installers for the current OS into `installers/<version>/` |
| Signing | `publisher.json` `signing` | No paid certificates by design: Windows and macOS ship unsigned (macOS apps are ad-hoc signed, which Apple silicon requires); Linux repo metadata can be GPG-signed |

v1.5.0 shipped 22 assets: macOS `.dmg` and `.tar.gz` (arm64, x64); Windows Inno Setup `setup.exe` (x64, arm64)
and `.zip`; Linux `.deb`, `.rpm`, `.AppImage`, `.tar.gz` (x64, arm64); desktop archives per OS; `Mux.Cli.<v>.nupkg`;
checksums.

## 2. Gaps before publishing 2.0.0

1. **Version.** Mux reports 1.8.0; MuxCloud and `MuxCloud.Protocol` are 2.0.0, and the Mux work lives on
   `feature/v2.0.0` (CHANGELOG "Unreleased"). Decision: bump Mux to 2.0.0 (VERSIONING.md requires explicit approval).
   Versions 1.6.0 to 1.8.0 were never released; skip straight to 2.0.0.
2. **Merge.** `feature/v2.0.0` is 17 or more commits ahead of `main`. Merge after validation; the release workflow
   checks out the branch it runs on, so run it from `main` (or pass `--ref`).
3. **Tray agent in the installers.** The publisher packages the desktop app ("Mux") and the CLI, but not the tray
   agent ("Mux Agent"). The agent hosts the hub and keeps the Mux Cloud connection open when no window is open, so
   it should ship in the `.dmg`, the Windows setup, and the Linux packages, with an optional "start at login" step.
   Work: add the agent as a publisher artifact, include it in the dmg, Inno, and deb/rpm/AppImage layouts, and
   register login startup (the `run-at-startup` scripts already do this per OS).
4. **Package managers (optional).** No repository secrets are set, so winget, Chocolatey, Homebrew, Scoop, NuGet, and
   the apt/yum repositories are skipped; only the GitHub Release upload runs (it uses the workflow token). To turn
   them on later, add `WINGET_TOKEN`, `CHOCO_API_KEY`, `NUGET_API_KEY`, `GPG_PRIVATE_KEY` / `GPG_KEYID`, and the
   tap/bucket repositories. Homebrew casks for unsigned apps need `--no-quarantine` in the install instructions.
5. **Install instructions.** README now has "Download an installer" with the unsigned-app steps (done in this
   change). The release notes template should repeat them.
6. **Asset size.** Self-contained builds are 80 to 450 MB each (about 6 GB per release). That is within GitHub's
   2 GB per-file limit; keep it, or trim later by dropping the per-OS desktop archives that duplicate the installers.
7. **Smoke test.** No job installs the artifacts. Add a `verify` job per OS that installs the package (`installer.exe
   /VERYSILENT`, `hdiutil attach` plus copy, `apt install ./*.deb`) and runs `mux --version` and `mux probe --help`.

## 3. Unsigned first-run guidance (for README and release notes)

| OS | What the user sees | What to do |
| --- | --- | --- |
| macOS 15 and later | "Mux cannot be opened" or "is damaged" | System Settings, Privacy & Security, **Open Anyway**; or `xattr -dr com.apple.quarantine /Applications/Mux.app` |
| Windows 10/11 | SmartScreen "Windows protected your PC" | **More info**, then **Run anyway** |
| Linux | Nothing for `.deb`/`.rpm`; AppImage will not start until executable | `chmod +x mux-*.AppImage` |

Right-click, Open no longer bypasses Gatekeeper on macOS 15, so the instructions use Open Anyway.

## 4. Steps to release 2.0.0

1. Approve the 2.0.0 version bump, then bump every `<Version>` and the CHANGELOG heading on `feature/v2.0.0`.
2. Ship the tray agent in the installers (gap 3) and add the smoke-test job (gap 7). Test locally with
   `scripts/<os>/build-installers.sh 2.0.0 --dry-run` and a full build on this Mac.
3. Validate: full test suite, MuxCloud end-to-end (`scripts/e2e-real.sh`), and one install per OS from a dry run:
   `gh workflow run release.yml -f version=2.0.0 -f publish=false`, then download the artifacts from the run.
4. Merge `feature/v2.0.0` into `main`.
5. Publish: `gh workflow run release.yml --ref main -f version=2.0.0 -f publish=true`. The workflow creates the
   `v2.0.0` tag and release and uploads every asset and checksum.
6. Edit the release notes: highlights from the CHANGELOG, Mux Cloud links (app.usemux.ai, the MuxCloud repo), and the
   unsigned-app steps from section 3.
7. Update usemux.ai's download links if they point at a fixed version.

## 5. Mux Cloud release (optional, separate repository)

MuxCloud has no installers; it ships the `jchristn77/muxcloud-server` Docker image and the `MuxCloud.Protocol` NuGet
package. A `v2.0.0` GitHub release in `jchristn/MuxCloud` can carry the release notes, the Docker and NuGet links, and
(optionally) the Android APK for sideloading until the Play listing is live.

## 6. Decisions needed

- Approve bumping Mux to 2.0.0.
- Ship the tray agent in the installers (recommended) or keep it source-install only.
- Turn on package managers now or after 2.0.0.
- Attach the Android APK to the MuxCloud release or not.
