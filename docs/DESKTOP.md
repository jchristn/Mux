# mux Desktop

_Introduced in mux v0.10.0._

`mux Desktop` is a cross-platform desktop client for the mux agent, built on [Avalonia](https://avaloniaui.net/). It is the third front end over the shared `Mux.Core` engine, alongside the interactive TUI and the `mux serve` web dashboard. It targets parity with the TUI's capability set and the dashboard's monitoring and reporting, plus a first-class conversation/thread experience. The full design and roadmap live in [`DESKTOP_APP.md`](../DESKTOP_APP.md).

The desktop app links `Mux.Core` in-process and drives the agent the same way the TUI does — it does **not** require `mux serve` to be running. It reads and writes the same `~/.mux` configuration and session store as the CLI and tray agent, so conversations, endpoints, MCP servers, skills, and usage telemetry are shared across all three surfaces.

## Running from source

With the .NET 8 or .NET 10 SDK installed, from a clone of the repository:

```bash
# Windows
run-desktop.bat            # defaults to net10.0
run-desktop.bat net8.0

# Linux / macOS
./run-desktop.sh           # defaults to net10.0
./run-desktop.sh net8.0
```

Both scripts run `dotnet run` against `src/Mux.Desktop`. You can also run the project directly:

```bash
dotnet run --project src/Mux.Desktop/Mux.Desktop.csproj --framework net10.0
```

Only one desktop instance runs per config directory (a single-instance lock); launching a second time focuses the existing window.

## The window

The shell has three regions:

- **Sidebar** — your saved conversations, newest first. `+ New` starts a fresh thread; the `⟳` icon refreshes the list; each row has actions (rename, delete). Threads created here are ordinary mux sessions, so the TUI and dashboard see them too. **Bulk delete** clears many at once.
- **Header** — the active endpoint/model selector, the About / Help button, and access to the managers and analytics described below.
- **Workspace** — the transcript and the composer.

### Chatting

Type in the composer and press **Enter** to send (**Shift+Enter** inserts a newline). On launch, focus goes straight to the composer. While a turn is running the send button becomes a red **Stop** button that cancels the turn; a cancelled turn is dropped from the model-facing history so the next prompt is never mis-batched with an unanswered one. The transcript auto-scrolls to the newest content as it streams.

- **Streaming & thinking** — assistant text streams token-by-token. Models that emit reasoning show a distinct thinking section.
- **Markdown & code** — replies render as Markdown with syntax-highlighted code blocks.
- **Copy** — hovering an assistant reply reveals a copy icon in its bottom-right corner; clicking it copies the raw text and briefly flips to a green checkmark.
- **Tool calls** — each tool the model invokes is shown as a card with its name, arguments, status, and elapsed time.
- **Prompt history** — with the caret on the first line of an empty-ish composer, **Up**/**Down** recalls your previous prompts (matching the TUI).

### Tools & approvals

The desktop app runs the same agent loop as the TUI with the **auto-safe** approval posture: read-only tools run automatically, while mutating actions raise an approval dialog before they proceed. Built-in tools, MCP tools, and skills are all available to the model when enabled in settings.

## Managers

Everything you can configure in the TUI is available here as a window, each backed by the same `Mux.Core` stores:

- **Endpoints** — add, edit, validate, and remove LLM endpoints (adapter type, base URL, auth, model).
- **MCP servers** — register stdio/HTTP MCP servers, edit them, and **validate connectivity** (a live probe of the server's tool list).
- **Skills** — browse, scaffold, edit, and **import** skills from a folder.
- **Subagents** — define and edit named subagents.
- **Prompts** — manage system-prompt profiles.
- **Custom commands**, **Hooks**, **Keybindings**, **Search providers**, **Pricing** — full CRUD over each, mirroring the CLI's configuration.
- **Plugins** — view installed plugins.
- **Command palette** — a searchable launcher for actions and managers.

## Analytics

- **Usage dashboard** — durable token/latency/cost analytics over the shared SQLite usage store, with selectable time ranges and per-metric charts.
- **Stats** — a JSON snapshot of the current conversation's statistics, with a copy button.

## Local server

The **Local server** window starts and stops an embedded `mux serve` instance (the REST API + web dashboard) from inside the desktop app, with a port-in-use guard. Use it to expose the dashboard without launching a separate process.

## Configuration

The desktop app reads the same configuration as the rest of mux, under `~/.mux/` by default. Set `MUX_CONFIG_DIR` before launching to point at an isolated config directory — endpoints, settings, sessions, and usage telemetry are all shared with the CLI and the tray agent. See [`CONFIG.md`](CONFIG.md).

Theme (light/dark) and other preferences live in the **Settings** window.

## Architecture

The project is split into two assemblies so the logic is testable without a display:

- **`Mux.Desktop`** — the Avalonia application: windows, controls, the transcript/composer, managers, and app lifecycle. Authored in code (no XAML), matching the tray agent's proven pattern.
- **`Mux.Desktop.Core`** — UI-framework-agnostic logic with no Avalonia dependency: localization (`ILocalizationService`, the locale registry, `LocaleFormatters`), view models, and services over `Mux.Core` (`ThreadService`, `UsageAnalyticsService`, `ConversationService`, and the `AgentLoopTurnRunner`). This library is referenced by the app and by the test project, so its behavior is verified by Touchstone suites in `Test.Shared`.

Logic shared across all three front ends — turn projection, conversation statistics, the system-prompt resolver, the external-tools binder, prompt history — lives in `Mux.Core` so the desktop, TUI, and dashboard behave identically. Both desktop assemblies target `net8.0;net10.0` and are versioned in lockstep with the rest of mux.

## Building the engine on your own

The engine the desktop app uses is published as a NuGet package. `Mux.Core` (and its `Mux.Search` dependency) carry full package metadata and ship a symbol package (`.snupkg`) with SourceLink, so you can build your own experiences on top of mux:

```bash
dotnet pack src/Mux.Core/Mux.Core.csproj -c Release
```

## Packaging

To produce a self-contained, single-file build that runs without a .NET runtime installed on the target machine:

```bash
# Windows (defaults to win-x64 / net10.0)
publish-desktop.bat
publish-desktop.bat win-arm64

# Linux / macOS (RID guessed from the host; override as needed)
./publish-desktop.sh
./publish-desktop.sh osx-arm64
./publish-desktop.sh linux-x64 net8.0
```

Output lands in `dist/desktop/<RID>/`. Distribute the whole folder; the launcher is `Mux.Desktop` (`Mux.Desktop.exe` on Windows).

These artifacts are **unsigned**. Shipping to end users additionally requires platform code-signing, which needs your own certificates and is not scripted here:

- **Windows** — sign `Mux.Desktop.exe` with `signtool` and an Authenticode certificate.
- **macOS** — codesign the `.app` with a Developer ID certificate and notarize it with `notarytool` before stapling.
- **Linux** — no signing is required to run; package as AppImage/`.deb`/`.rpm` as desired.

Signing, notarization, per-OS installers, and auto-update are the remaining items in the packaging phase of [`DESKTOP_APP.md`](../DESKTOP_APP.md).
