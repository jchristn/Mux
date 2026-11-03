# Investigate 3 watcher/broadcast test failures in Mux (macOS)

## Task
Find the root cause of 3 failing tests and either fix the underlying bug or durably harden the tests. Do NOT skip/silence them, and do not treat "flaky" as a diagnosis — produce evidence.

## Environment & repo
- Repo: `/Users/joelchristner/Code/Mux` (mux — TUI/agent CLI, .NET, targets net8.0 + net10.0)
- OS: macOS 26.6.0 (Apple Silicon). During the referencing session, load average was 12–16 and hundreds of test processes had run back-to-back all day.
- Working tree is clean; branch `feature/usage-heatmap-groups` is pushed. The failures reproduce on UNMODIFIED `main` too (proven by full stash → rebuild → run → restore cycle), so do not chase the changes on that branch — they are exonerated.

## The failures
1. `Mux.Core.Tests.Sessions.SessionStore.WatcherFiresChangedOnWriteAndRemovedOnDelete`
   - "Timeout after 10s waiting for changed event" (and/or "removed event").
   - Fails even run ISOLATED, ~3/3 times. Isolated runtime ~20s (2 internal 10s timeouts).
2. `Mux.Core.Tests.Server.MuxServerRoutes.StoreWriteBroadcastsToSessionSubscribers`
   - "no output event received" within its timeout. PASSES isolated (~3/3, 3s); fails under full-suite runs.
3. `Mux.Core.Tests.Server.MuxServerRoutes.StoreWriteDeletedRemovesSessionAndBroadcasts`
   - "no delete event received". Same isolation behavior as #2.

## Leading hypothesis (verify, don't assume)
All three share one root: FileSystemWatcher (FSEvents) event delivery. SessionStore watches its store directory; the two server tests assert WebSocket broadcasts whose feed originates from store writes/watcher notifications. On macOS, FSEvents under heavy filesystem churn coalesces/delays/drops events, and atomic temp+rename writes may surface as Renamed rather than Changed. Fixed 10s wall-clock timeouts then fail nondeterministically.

## Where to look
- Tests: `src/Mux.Core.Tests/Sessions/SessionStore.cs`, `src/Mux.Core.Tests/Server/MuxServerRoutes.cs`
- Implementation: `grep -rn "FileSystemWatcher" src/` — SessionStore watcher wiring (NotifyFilters, event handlers, write path: buffered write vs temp-file+rename), and the server route that turns store events into WS broadcasts (`src/Mux.Core/Server/...`).
- Test infra: Touchstone. Console runner `src/Test.Automated` supports `--suite <id>`, `--group <name>`, `--list-groups`; group definitions live in the MuxSuites.Groups registry (see `src/Mux.Core.Tests.Registry.SuiteRegistrySuite` for the full ID list); docs in `docs/TESTING.md`.

## Key questions to answer with evidence
1. Does SessionStore write atomically (temp + rename)? If the test and/or the broadcast feed only handles `Changed`, that's a latent defect that timing exposes. Check which NotifyFilters and handler branches exist.
2. Do `StoreWriteBroadcasts*` depend on the same watcher feed as SessionStore? Trace store write → event → broadcast. If yes, #2/#3 are downstream symptoms of #1's degradation.
3. Are the 10s timeouts compatible with FSEvents coalescing latency on a loaded machine? What timeout/backoff do comparable watcher tests elsewhere use?
4. xUnit parallelizes by collection — in a full run, which collections do these three land in, and can neighbors starve their timers/thread-pool callbacks?
5. Cheap discriminator, do this FIRST: on an idle (ideally freshly rebooted) machine, run all 3 on clean `main`. If green → environmental degradation; then still decide whether the tests are too wall-clock fragile to leave as-is.

## Repro commands
```sh
# Isolated (xUnit):
dotnet test src/Mux.Core.Tests -c Release -f net10.0 --filter "FullyQualifiedName~Sessions.SessionStore.WatcherFires"
dotnet test src/Mux.Core.Tests -c Release -f net10.0 --filter "FullyQualifiedName~Server.MuxServerRoutes.StoreWrite"
# Touchstone console:
cd src/Test.Automated && dotnet run -c Release -- --suite Mux.Core.Tests.Sessions.SessionStore
cd src/Test.Automated && dotnet run -c Release -- --group server
# Loop for stability proof:
for i in 1 2 3 4 5 6 7 8 9 10; do
  dotnet test src/Mux.Core.Tests -c Release -f net10.0 \
    --filter "FullyQualifiedName~Sessions.SessionStore|FullyQualifiedName~Server.MuxServerRoutes" | tail -2
done
```

## Acceptance criteria
- Root cause stated with code/evidence (e.g., rename-vs-changed semantics, observer latency measurement, scheduler contention).
- Fix landed: implementation fix if a real bug (preferred), or test hardening with justification (poll observable state instead of fixed event waits; handle Renamed; relax wall-clock asserts).
- The three pass 10/10 isolated AND in at least one full `dotnet test` run on an idle machine.
- If any behavior is genuinely unobservable-by-design on macOS, an OS-gated skip with a clear reason is acceptable only with that justification documented in the test.

## Out of scope
- The `feature/usage-heatmap-groups` branch contents (heatmap UI, test grouping) — verified unrelated. Visual verification of the new heatmap is a separate task.
- Rebuilding the test-grouping infrastructure — use it, don't modify it.

## Useful clues
- `WatcherFires` fails even in isolation while the two server tests only fail under load — that asymmetry points at a SessionStore-local defect (or macOS watcher semantics) underneath a separate load-sensitivity problem.
- For timing analysis, Touchstone results files record `durationMs` per case — `--results-file` on the console runner provides data with no new instrumentation.
