# Testing mux

## Unit Tests (Test.Xunit)

Fast, deterministic, no external dependencies. Covers settings, adapters, tools, approval handler, and agent loop.

```bash
dotnet test test/Test.Xunit/Test.Xunit.csproj
```

## Automated Tests (Test.Automated)

Integration tests that exercise the agent loop with a mock HTTP server. No Ollama or external LLM required.

```bash
dotnet run --project test/Test.Automated/Test.Automated.csproj
```

## Run Everything

```bash
dotnet test test/Test.Xunit/Test.Xunit.csproj && dotnet run --project test/Test.Automated/Test.Automated.csproj
```

## Build Only (No Tests)

```bash
dotnet build src/Mux.sln
```
