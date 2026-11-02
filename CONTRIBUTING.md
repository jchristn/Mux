# Contributing to mux

Thanks for your interest in contributing! mux is beta software and feedback, bug reports, and pull
requests are all welcome.

## Ways to contribute

- **Bug reports and feature requests** — open an issue using the templates under
  [Issues](https://github.com/jchristn/Mux/issues). Search first; your issue may already exist.
- **Questions and ideas** — use [Discussions](https://github.com/jchristn/Mux/discussions) rather
  than issues for open-ended topics.
- **Pull requests** — small, focused PRs are easiest to review. For anything large (new surfaces,
  new adapters, architectural changes), open an issue to discuss the approach first.
- **Skills and documentation** — bundled skills live in `src/Mux.Core/Skills/Bundled`, guides in
  `docs/`. Improvements to either are as valuable as code.

## Development setup

You need the **.NET 8 and .NET 10 SDKs** (the solution multi-targets `net8.0;net10.0`). Everything
else restores from NuGet.

```bash
git clone https://github.com/jchristn/Mux.git
cd Mux
dotnet build src/Mux.sln
```

Optional tooling: Docker (integration suite), Node.js (TypeScript SDK and VS Code extension),
Python 3.8+ (Python SDK).

## Repository layout

| Path | What lives there |
| ---- | ---------------- |
| `src/Mux.Core` | The engine: agent loop, adapters, tools, MCP client, skills, sessions, telemetry |
| `src/Mux.Search` | Search providers (Tavily, Google, Brave, Bing, DuckDuckGo, Kagi, Wikipedia) |
| `src/Mux.Cli` | Interactive TUI and the `mux print` headless surface |
| `src/Mux.Server` | `mux serve`: loopback REST + WebSocket API and the web dashboard |
| `src/Mux.Desktop`, `src/Mux.Desktop.Core` | Avalonia desktop app and its service layer |
| `src/Mux.VSCode` | VS Code extension (TypeScript) |
| `src/Mux.Agent` | Cross-platform tray agent that hosts `mux serve` |
| `src/Test.Shared`, `src/Test.Automated`, `src/Test.Xunit`, `src/Test.Nunit` | Test suites and the three runners |
| `sdk/python`, `sdk/typescript` | Python and TypeScript drivers for `mux print` |
| `docs/` | Topic guides |
| `docker/` | Observability stack (Prometheus, Tempo, Loki, Grafana) |

## Building and testing

Build the whole solution:

```bash
dotnet build src/Mux.sln --configuration Release
```

Run the test suite the way CI does (the Touchstone console runner executes every suite):

```bash
dotnet run --project src/Test.Automated/Test.Automated.csproj --configuration Release --framework net8.0
dotnet run --project src/Test.Automated/Test.Automated.csproj --configuration Release --framework net10.0
```

Run a single suite while iterating: add `-- --suite <id>` (for example `--suite AgentLoop`).

The xUnit and NUnit adapters run the same suites through `dotnet test`:

```bash
dotnet test src/Test.Xunit/Test.Xunit.csproj --configuration Release
dotnet test src/Test.Nunit/Test.Nunit.csproj --configuration Release
```

See [docs/TESTING.md](docs/TESTING.md) for the opt-in Docker integration suite and the live
skill-selection suite, and [docs/SKILLS_AUTHORING.md](docs/SKILLS_AUTHORING.md) when writing skills.

## Style and conventions

- **C# conventions.** The first code line of every file is the `namespace` declaration, using
  block-scoped style with `using` directives inside the namespace body (`namespace Foo { using ...; }`).
  Prefer explicit types over `var`, end `async` paths in `ConfigureAwait(false)` in library code, and
  implement `IDisposable`/`IAsyncDisposable` for owned resources.
- **Nullable reference types are enabled.** Warnings are errors in spirit: do not commit code that
  produces compiler warnings, and never suppress them in `.csproj`/`Directory.Build.props`. Fix the
  null flow (or document intent with `!` only where a contract guarantees non-null).
- **XML documentation on every publicly visible type and member** (summary plus parameters and
  returns when not obvious). `GenerateDocumentationFile` is on and CS1591 is not suppressed.
- **Obsolete APIs** are annotated with `[Obsolete("...")]` and a migration hint; new code must not
  use obsolete members (CS0618 is an error-level fix, not a suppression).
- **Tests.** New behavior ships with a Touchstone suite in `src/Test.Shared/Suites` so it runs
  through all three runners. Prefer headless, deterministic tests — no real terminals, live LLMs,
  or network in the default suite.
- **Keep changes focused.** One concern per pull request; avoid drive-by reformatting.

## Pull request process

1. Fork, branch from `main` (`feature/<short-name>` works with CI), and keep commits readable.
2. Update `CHANGELOG.md` under `## Unreleased` when the change is user-visible.
3. Touch a bundled skill? Run `python3 scripts/common/generate-builtin-skills.py` and commit the
   regenerated `BUILTIN_SKILLS.md` — CI verifies it is current.
4. Make sure `dotnet build` has **zero warnings** and the console runner passes on both frameworks.
5. Fill in the pull request template and link the issue it closes.

CI runs the full test matrix (Ubuntu + Windows, `net8.0` + `net10.0`), skill validation, and the
`BUILTIN_SKILLS.md` freshness check on every PR; the Docker integration suite runs on `main`.

## Versioning and SDK compatibility

All shipped artifacts **version in lockstep with the CLI**: `Mux.Core`/`Mux.Search` (NuGet), the
Python SDK (`sdk/python`), the TypeScript SDK (`sdk/typescript`), and the VS Code extension
(`src/Mux.VSCode`) share one version number (currently **1.5.0**). When bumping the release:

1. Set `<Version>` in each production `.csproj` under `src/` (not the `Test.*` projects, which are
   internal tools and stay on their own scheme).
2. Bump `version` in `sdk/python/pyproject.toml`, `__version__` in `sdk/python/mux_sdk/__init__.py`,
   `version` in `sdk/typescript/package.json` **and** the two root spots in its `package-lock.json`,
   and `version` in `src/Mux.VSCode/package.json` **and** the two root spots in its
   `package-lock.json` (or run `npm version <version> --no-git-tag-version` in each).
3. Update the version badge and the release download example in `README.md`.
4. Note the bump in `CHANGELOG.md`.

SDK compatibility is pinned to the CLI's `contractVersion`-versioned JSONL contract, not the package
version, so a lockstep bump never breaks an older CLI; the shared version just makes "which mux goes
with which SDK" answerable at a glance. See each SDK's README for its contract guarantees.



## Contributor License Agreement

No CLA. By submitting a contribution you agree it is licensed under the project's
[MIT license](LICENSE.md).

## Code of conduct

Be respectful and constructive. Maintainer decisions on scope and direction are final; the goal is a
coherent, dependable tool.
