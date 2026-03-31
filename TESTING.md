# Testing mux

## Build

```bash
dotnet build src/Mux.sln
```

## Unit Tests

`Test.Xunit` covers settings, adapters, CLI formatting, command behavior, tools, and agent loop behavior.

```bash
dotnet test test/Test.Xunit/Test.Xunit.csproj
```

## Automated Tests

`Test.Automated` runs the mock-server integration suites, including:
- agent loop lifecycle coverage
- tool-use flows
- `mux print` JSONL contract checks
- `mux probe` JSON output checks

Because the project targets multiple frameworks, specify one when using `dotnet run`:

```bash
dotnet run --project test/Test.Automated/Test.Automated.csproj --framework net10.0
```

## Recommended Full Validation

```bash
dotnet test test/Test.Xunit/Test.Xunit.csproj
dotnet run --project test/Test.Automated/Test.Automated.csproj --framework net10.0
```
