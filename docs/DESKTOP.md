# mux Desktop

_Introduced in mux v0.10.0. **Alpha, in active development.**_

`mux Desktop` is a cross-platform desktop client for the mux agent, built on [Avalonia](https://avaloniaui.net/). It is the third front end over the shared `Mux.Core` engine, alongside the interactive TUI and the `mux serve` web dashboard. The goal is full parity with the TUI's capability set and the dashboard's monitoring and reporting, plus a first-class conversation/thread experience and a localized interface. The full design and roadmap live in [`DESKTOP_APP.md`](../DESKTOP_APP.md).

The desktop app links `Mux.Core` in-process and drives the agent the same way the TUI does — it does not require `mux serve` to be running.

## Status

This release ships the foundation and the tested logic layer, not the full app. What works today:

- The application shell — a conversation sidebar, a header, and a workspace — with a startup splash and an About / Help window.
- A single-instance lock, so only one desktop instance runs per config directory.
- Thread management backed by the real mux session store: the sidebar lists your saved sessions, and **New conversation** creates one. Threads created here are ordinary mux sessions, so the TUI sees them too.
- An internationalization scaffold with a twelve-locale registry and locale-aware formatters.

The transcript, composer, tool cards, managers, and analytics views are the subject of the phased work described in `DESKTOP_APP.md`.

## Running it

From a clone of the repository, with the .NET 8 or .NET 10 SDK installed:

```bash
# Windows
run-desktop.bat            # defaults to net10.0
run-desktop.bat net8.0

# Linux / macOS
./run-desktop.sh           # defaults to net10.0
./run-desktop.sh net8.0
```

Both scripts run `dotnet run` against `src/Mux.Desktop`. You can also build and run the project directly:

```bash
dotnet run --project src/Mux.Desktop/Mux.Desktop.csproj --framework net10.0
```

## First run

On launch the splash appears briefly, then the shell opens. The sidebar shows your existing conversations (from `~/.mux/sessions`, or the directory named by `MUX_CONFIG_DIR`). Choose **New conversation** to create a thread. The **About / Help** button in the header opens a window with the version, license, repository link, and a short getting-started note.

## Configuration

The desktop app reads the same configuration as the rest of mux, under `~/.mux/` by default. Set `MUX_CONFIG_DIR` before launching to point at an isolated config directory — endpoints, settings, sessions, and usage telemetry are all shared with the CLI and the tray agent. See [`CONFIG.md`](CONFIG.md).

## Architecture

The project is split into two assemblies so the logic is testable without a display:

- **`Mux.Desktop`** — the Avalonia application: windows, controls, the splash, the About window, and app lifecycle. Authored in code (no XAML) for the current foundation, matching the tray agent's proven pattern.
- **`Mux.Desktop.Core`** — UI-framework-agnostic logic with no Avalonia dependency: localization (`ILocalizationService`, the locale registry, `LocaleFormatters`), view models, and the services over `Mux.Core` (`ThreadService`, `UsageAnalyticsService`, `ConversationService`, and the `TurnProjection` event accumulator). This library is referenced by the app and by the test project, so its behavior is verified by Touchstone suites in `Test.Shared`.

Both target `net8.0;net10.0` and are versioned in lockstep with the rest of mux.

## Building the engine on your own

The engine the desktop app uses is published as a NuGet package. `Mux.Core` (and its `Mux.Search` dependency) carry full package metadata and ship a symbol package (`.snupkg`) with SourceLink, so you can build your own experiences on top of mux:

```bash
dotnet pack src/Mux.Core/Mux.Core.csproj -c Release
```

## Packaging

Signed, installable artifacts for Windows, macOS, and Linux are part of the later packaging phase described in `DESKTOP_APP.md`. Until then, run the app from source with the scripts above.
