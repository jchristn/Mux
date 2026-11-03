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
TUIKit interactive shell driven headlessly through `HeadlessBackend` (projector, sidebar,
composer/chooser, command surfaces, modals, persistence, frame rendering, and polish), with positive
and negative cases throughout. No ordinary test needs a real terminal or a live LLM; the two opt-in
suites below use Docker and a real model.

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

Pass `--suite <id>` (repeatable) to run only some suites, for example `--suite DatabaseSkills`.

## Docker integration suite (opt-in)

`DockerServices` runs the database and dependency-audit skills against real services in throwaway
containers: PostgreSQL, MySQL, MariaDB, SQL Server, Oracle Database Free, MongoDB, Redis, Neo4j,
Cassandra, and LiteGraph, plus real `npm audit`, `pip-audit`, and `osv-scanner` output. Each case
starts its own labeled container, seeds it, runs the real skill, checks the results and the read-only
guarantees against the server (for example that Postgres rejects a write hidden in a SELECT and that a
SQL Server SELECT INTO is rolled back), checks that the planted password never appears in any output,
and removes the container even when the test fails. The skills reach each server through small shims
that forward their client calls (`psql`, `mysql`, `sqlcmd`, `sqlplus`, `mongosh`, `redis-cli`,
`cypher-shell`, `cqlsh`) into the container, so no database client is needed on the host.

It needs Docker on Linux or macOS and network access to pull images, so it runs only when asked:

```bash
dotnet run --project src/Test.Automated/Test.Automated.csproj --framework net10.0 -- --suite DockerServices --docker
```

`--docker` sets `MUX_TEST_DOCKER=1`; set that variable yourself for the xUnit and NUnit runners. The
SQL Server image is x64 only, so on an ARM64 host that case is skipped unless `MUX_TEST_DOCKER_EMULATION=1`.
Leftover containers, if a run is killed, carry the label `mux-test=1`:
`docker ps -aq --filter label=mux-test=1 | xargs docker rm -f`.

## Live skill-selection suite (opt-in)

`SkillSelectionLive` asks a real model which skill it would use for each built-in evaluation prompt,
with the same skill listing and `skill`/`run_skill` tools the agent sends, and fails when the share of
prompts where it picks an expected skill falls below a floor. It is skipped unless an endpoint and
model are given:

```bash
dotnet run --project src/Test.Automated/Test.Automated.csproj --framework net10.0 -- \
  --suite SkillSelectionLive --llm-endpoint http://localhost:11434 --llm-model qwen3:8b \
  --llm-adapter ollama --llm-cases 40 --llm-floor 0.7 --llm-report live-report.json
```

| Option | Variable | Meaning |
|---|---|---|
| `--llm-endpoint` | `MUX_TEST_LLM_ENDPOINT` | Base URL of the endpoint (required) |
| `--llm-model` | `MUX_TEST_LLM_MODEL` | Model name (required) |
| `--llm-adapter` | `MUX_TEST_LLM_ADAPTER` | `ollama` (default), `openai`, `openai_compatible`, `vllm`, `anthropic`, `gemini`, `azure_openai`, `vertex`, or `bedrock` |
| `--llm-api-key` | `MUX_TEST_LLM_API_KEY` | API key or bearer token, if the endpoint needs one (never printed) |
| `--llm-floor` | `MUX_TEST_LLM_FLOOR` | Required top-1 rate from 0 to 1 (default 0.6) |
| `--llm-cases` | `MUX_TEST_LLM_CASES` | Ask about this many prompts, spread across the set (default all) |
| `--llm-report` | `MUX_TEST_LLM_REPORT` | Write each prompt's expected and picked skill to this JSON file |
| `--llm-timeout` | `MUX_TEST_LLM_TIMEOUT` | Seconds per request (default 180) |

The xUnit and NUnit runners read the same variables. Prefer the variable for the API key so it stays
out of shell history. `mux skill eval --live --endpoint <name>` runs the same check from the CLI using a
configured endpoint.

## Adapter runners

```bash
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj
```

## Development and full validation

For development, build and run the suites you changed on one framework. Repeat `--suite` to
select more than one. Keep the same configuration/framework when using `--no-build`:

```bash
dotnet build src/Test.Automated/Test.Automated.csproj -c Release -f net10.0
dotnet run --project src/Test.Automated/Test.Automated.csproj -c Release -f net10.0 --no-build -- --suite SessionStore --suite SessionService --suite MuxServerRoutes --results focused.json
```

A full pass includes real PowerShell/Python/Node processes, temporary Git repositories, loopback
servers, and script fixtures; it is an integration run. Script tests cache a production-seeded
catalog in memory and copy only the skill they execute into each private fixture. Tests of catalog
seeding, upgrades, discovery, and packaging still seed the full catalog through production code.
No script execution or assertion is skipped by the fixture optimization.

For release validation, run both frameworks. The adapters expose each case once; there is no
additional aggregate test repeating the entire suite. Running all three runners on both frameworks
still executes the shared suite six times, so adapter compatibility checks belong in the weekly/on-demand
job rather than the everyday edit/test cycle. Tests intentionally run serially because some mutate
process-wide environment variables and console streams; do not enable blanket parallelism.

```bash
dotnet build src/Mux.sln -c Release
dotnet run --project src/Test.Automated/Test.Automated.csproj -c Release -f net8.0 --no-build
dotnet run --project src/Test.Automated/Test.Automated.csproj -c Release -f net10.0 --no-build
dotnet test src/Test.Xunit/Test.Xunit.csproj -c Release --no-build
dotnet test src/Test.Nunit/Test.Nunit.csproj -c Release --no-build
```

Use console `--results results.json` for per-case `durationMs` when investigating performance.
Compare the same suites, framework, configuration, and machine load; build/restore time is separate.

### Measured fixture and runner costs

On macOS, Release/net10.0, the same 109 `ToolchainSkills` and `LoopSkills` cases took
162.395 seconds before scoped fixtures and 151.567 seconds after (6.7% less case time).
This is a local comparison, not a cross-machine performance guarantee. A fixture-only probe
wrote 459 files per full seed versus two files for `js-test`; ten full seeds took 459 ms,
while each warm scoped seed took less than 1 ms. The catalog is still seeded once per process.
PowerShell startup and the commands themselves remain real and dominate many cases.

On main before this change, both adapters also exposed a `RunAll` test alongside their individual
cases. Removing that aggregate cuts execution count from two to one per case per framework,
without removing any individual test coverage. This saving is separate from fixture timings.

## Watcher reliability

The macOS investigation reproduced missed notifications in the original SessionStore and server
broadcast tests and in a standalone .NET FileSystemWatcher probe. Waiting 250 ms or 1 second
before writing did not restore notifications, and the probe also failed outside the default temp
directory. No native error event was reported. This establishes silent notification loss on that
host, not its OS-level cause; heavy load and rename handling were not proven explanations.
The original code already subscribed to rename events. The application defect was relying solely
on native notifications with no reconciliation, plus a shared debounce timer that every new event
could postpone indefinitely. [FileSystemWatcher documents that events can be missed](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher).

`SessionStoreWatcher` uses native filesystem events for prompt updates and reconciles directory
metadata once per second to recover persistent creates, updates, renames, and deletes when native
notifications fail. The scan compares file size and last-write timestamp without reading transcripts.
Unchanged files do not generate polling notifications, and existing files form the startup baseline.
Native events are batched with bounded latency, so continuous writes cannot indefinitely postpone a
pending notification. Polling stops when the watcher is disposed.

This is eventual state synchronization, not a filesystem audit log: a create/delete entirely between
scans or a rewrite preserving both size and last-write timestamp requires a native notification.
Normal SessionStore atomic saves update the timestamp. A failed directory scan retains the previous
baseline; removing and recreating the directory is reconciled on subsequent scans.

The SessionStore suite explicitly disables native watching in a regression case to verify fallback
notifications, same-size rewrites, renames, deletion, directory recreation, and disposal. The original
watcher, transcript subscription, and server WebSocket tests retain their notification assertions and
timeouts.

Validation of these changes on macOS (Release, 2026-11-02): the full net10.0 console run
passed 1,871 cases with 23 pre-existing skips in 10m 36s; the full net8.0 xUnit run passed
all 1,871 enabled cases in 9m 14s. NUnit passed all six targeted watcher cases on net8.0,
and all four original notification cases passed ten fresh-process net10.0 repetitions while
the full console suite was running. The solution built for both frameworks with no warnings
or errors. Full-run durations are validation observations, not a controlled before/after comparison.

CI (`.github/workflows/ci.yml`) runs the console runner on every push and pull request as a parallel
matrix of Linux and Windows by `net8.0` and `net10.0`, validates the default skills, and checks that
`BUILTIN_SKILLS.md` is current. The xUnit and NUnit adapters run the same suites, so CI runs them weekly
and on demand rather than on every push. The Docker integration suite runs in its own job on pushes to
`main` and on demand. A newer push to the same branch cancels the run in progress.
