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

## Recommended full validation

```bash
dotnet build src/Mux.sln
dotnet run  --project src/Test.Automated/Test.Automated.csproj --framework net8.0
dotnet run  --project src/Test.Automated/Test.Automated.csproj --framework net10.0
dotnet test src/Test.Xunit/Test.Xunit.csproj
dotnet test src/Test.Nunit/Test.Nunit.csproj
```

CI (`.github/workflows/ci.yml`) runs the console runner on every push and pull request as a parallel
matrix of Linux and Windows by `net8.0` and `net10.0`, validates the default skills, and checks that
`BUILTIN_SKILLS.md` is current. The xUnit and NUnit adapters run the same suites, so CI runs them weekly
and on demand rather than on every push. The Docker integration suite runs in its own job on pushes to
`main` and on demand. A newer push to the same branch cancels the run in progress.
