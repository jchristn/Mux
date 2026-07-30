# Testing mux

The test suite is written once as runner-agnostic
[Touchstone](https://www.nuget.org/packages/Touchstone.Core) descriptors in `src/Test.Shared`
(`MuxSuites.All`) and executed through three runners, all targeting `net8.0` and `net10.0`:

| Project            | Runner            | How it runs the shared suites            |
| ------------------ | ----------------- | ---------------------------------------- |
| `Test.Automated`   | Touchstone console | `dotnet run` (exits non-zero on failure) |
| `Test.Xunit`       | xUnit adapter     | `dotnet test`                            |
| `Test.Nunit`       | NUnit adapter     | `dotnet test`                            |

Suites cover the engine (jobs, write-lease, approvals, sessions, adapters, tools) and the entire
TUIKit interactive shell driven headlessly through `HeadlessBackend` — projector, sidebar,
composer/chooser, command surfaces, modals, persistence, frame rendering, and polish — with positive
and negative cases throughout. No test needs a real terminal or a live LLM.

## Build

```bash
dotnet build src/Mux.sln
```

## Console runner (both frameworks)

The solution multi-targets, so pick a framework with `--framework`:

```bash
dotnet run --project src/Test.Automated/Test.Automated.csproj --framework net8.0
dotnet run --project src/Test.Automated/Test.Automated.csproj --framework net10.0
```

Pass `--results <path>` to export machine-readable results:

```bash
dotnet run --project src/Test.Automated/Test.Automated.csproj --framework net10.0 -- --results results.json
```

## Adapter runners

```bash
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj
```

## Recommended full validation

```bash
dotnet build src/Mux.sln
dotnet run  --project src/Test.Automated/Test.Automated.csproj --framework net8.0
dotnet run  --project src/Test.Automated/Test.Automated.csproj --framework net10.0
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj
```

CI (`.github/workflows/ci.yml`) runs exactly this matrix on Linux and Windows for every push and pull
request.
