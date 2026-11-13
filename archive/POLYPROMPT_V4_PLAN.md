# Mux: Adopting PolyPrompt 4.0

> **Status:** implemented on branch `polyprompt-v4` (2026-11-12) against PolyPrompt 4.0.0 from nuget.org. See section 13 for the as-built record. Not yet committed, pushed, or published.
> **Baseline:** Mux `main` @ `24bef09` (1.7.0), PolyPrompt 3.7.0.
> **Target:** Mux **1.8.0**.
> **Companion plan:** `~/Code/Polyprompt/V4_PLAN.md` (PolyPrompt 4.0). Section 12 there is the summary of this plan. Section numbers below that start with "PP" refer to it, for example PP 5.3.
> **Before starting:** read the as-built section of the PolyPrompt plan and its CHANGELOG. Where shipped 4.0 names differ from the names used here, the shipped names win.

---

## 1. Goal

Mux supports these providers **with API keys only**:

| Provider | Mux adapter | Before this plan | After |
|---|---|---|---|
| Anthropic (Claude) | `anthropic` | works | works; model listing added |
| OpenAI, including the codex-tuned models | `openai` | Chat Completions only. Codex-tuned and newest models are unavailable, and reasoning is discarded between tool calls. | **Responses API**: every current OpenAI model, with encrypted reasoning carried across tool calls and session resume |
| Google Gemini (the models behind Antigravity) | `gemini` | works | works; model listing added; docs explain Antigravity |
| xAI / Grok / SpaceX | `xai` (aliases `grok`, `x-ai`, `spacex`) | works (Responses) | works; same engine as OpenAI |

**Out of scope, deliberately:** OAuth and subscription logins (Claude Pro/Max, ChatGPT through `codex login`, Antigravity's Google-account quota). They were dropped on 2026-11-12 in favor of API keys only. Section 11 records the sanctioned route if that changes.

## 2. What PolyPrompt 4.0 changes, from Mux's side

| PolyPrompt 4.0 change | Effect on Mux |
|---|---|
| One `{Provider}Client` per provider replaces the per-capability clients (PP 4.1, 5.1) | `LlmClient.CreateClient` builds provider clients. The type `CompletionClientBase` disappears. |
| `OpenAiClient` speaks Responses; `OpenAiCompatibleClient` speaks Chat Completions (PP 6.1) | The `openai` adapter gets Responses with no new setting. `vllm` and `openai-compatible` keep Chat Completions. |
| `OpenAiCompatibleClient` has `AuthPlacement` (bearer, header, or query) (PP 4.9) | Mux's `QueryStringAuthHandler` and its header placement code can be deleted |
| `AzureOpenAiClient` moves to the v1 API: deployment sent as `model`, no `api-version` (PP 6.6) | `EndpointConfig.ApiVersion` becomes unused |
| `Defaults.<Section>` replaces `Defaults`/`Model`; some clients have no default model (PP 4.5, 5.3) | Mux always sends `Model` per call, so default changes matter only when an endpoint's `model` is blank |
| `ClientOptions` replaces the trailing constructor parameters (PP 4.9) | Constructor calls change |
| Continuation state: 4.0 envelope written, every 3.x shape still read (PP 4.8) | Saved sessions keep working with no migration. Mux's `ProviderState` plumbing is unchanged. |
| `ReasoningEffortLevel` gains `None`, `XHigh`, `Max`; `AppliedReasoningEffort` on results (PP 4.7) | New effort levels on every surface; applied effort recorded in telemetry |
| `Capabilities` / `Supports` on every client (PP 4.3) | Model listing for any adapter whose client supports `Models` |
| `RawResponse`, `Citations` on results (PP 4.8) | Not used by Mux in 1.8.0 (section 10) |
| `CacheCreationTokens` reported for OpenAI Responses (PP 4.8) | Check cost math (section 4.7) |

## 3. Inventory: where Mux touches PolyPrompt today

Found by search on 2026-11-12:

| File | Usage |
|---|---|
| `src/Mux.Core/Mux.Core.csproj` | `PolyPrompt` 3.7.0 package reference |
| `src/Mux.Core/Llm/LlmClient.cs` | All client construction (`CreateClient`), the request mapping (`BuildRequest`, `MapMessage`, `MapReasoningEffort`, `MapReasoningLevel`), streaming and non-streaming tool chat, usage mapping (including the `cachedIsAdditional` rule for Anthropic and Bedrock), `ProviderState` capture, malformed-tool-call recovery for Ollama, `OpenAiFamilyBearerKey`, `BuildQueryStringAuthHandler`, and silent logging |
| `src/Mux.Core/Utility/QueryStringAuthHandler.cs` | Query-string key placement for OpenAI-family adapters |
| `src/Mux.Core/Utility/EndpointModelLister.cs` | Model listing over its own HTTP calls (Ollama `/api/tags`, OpenAI-compatible `/v1/models`, xAI); no PolyPrompt use today |
| `src/Mux.Core/Models/EndpointConfig.cs` | `ApiVersion`, `AuthPlacement`, `ApiKey` docs that name PolyPrompt behavior |
| `src/Mux.Core/Settings/SettingsLoader.cs` | Adapter-specific normalization, including Ollama Cloud's fixed root |
| `src/Mux.Core/Telemetry/PricingTable.cs`, `DefaultPricing.cs` | Cost math and the default price list |
| `src/Mux.Core/Enums/ReasoningLevelEnum.cs` (+ converter) | `Minimal`, `Low`, `Medium`, `High` |
| `src/Mux.Core/PACKAGE.md`, `docs/CONFIG.md`, `docs/USAGE.md`, `docs/PROVIDERS.md` | Docs that name PolyPrompt or provider behavior |
| `src/Test.Shared/Suites/LlmBridgeSuite.cs`, `src/Test.Shared/Llm/LocalLlmTestServer.cs`, `MuxSuites.cs` | Bridge tests against per-provider mock servers |

## 4. Changes in Mux.Core

### 4.1 Package

Set `PolyPrompt` to `4.0.0` in `src/Mux.Core/Mux.Core.csproj`. Mux.Core is published to NuGet, so its package dependency moves to 4.0.0 too. Note this in Mux.Core's `PACKAGE.md`.

### 4.2 Client construction (`LlmClient.CreateClient`)

`_Client` changes type from `CompletionClientBase` to `ProviderClientBase`.

| Adapter | 3.x client | 4.0 client | Notes |
|---|---|---|---|
| `openai` | `OpenAiCompletionClient` | `OpenAiClient(apiKey, baseUrl, options)` | Responses. Blank `baseUrl` means OpenAI's default. |
| `openai-compatible`, `vllm` | `OpenAiCompletionClient` | `OpenAiCompatibleClient(baseUrl, apiKey, new OpenAiCompatibleClientOptions { AuthPlacement = ... })` | Map Mux's `AuthPlacement` (bearer, header, query) onto PolyPrompt's. Delete `QueryStringAuthHandler` and the header placement in `ApplyEndpointHeaders`. |
| `azure-openai` | `AzureOpenAiCompletionClient(baseUrl, model, key, apiVersion, ...)` | `AzureOpenAiClient(baseUrl, apiKey, options)` | The deployment comes from `endpoint.Model`, sent as `model`. `ApiVersion` is ignored (4.5). |
| `xai` | `XAiCompletionClient` | `XAiClient(apiKey, baseUrl, options)` | Keep the `XAI_API_KEY` fallback |
| `anthropic` | `AnthropicCompletionClient` | `AnthropicClient(apiKey, baseUrl, options)` | |
| `gemini` | `GeminiCompletionClient` | `GeminiClient(apiKey, baseUrl, options)` | |
| `vertex` | `VertexAiCompletionClient(project, region, new AdcCredential(), ...)` | `VertexAiClient(project, region, new AdcCredential(), baseUrl, options)` | Keep the project and region validation messages |
| `bedrock` | `BedrockCompletionClient(new EnvironmentAwsCredential(region), ...)` | `BedrockClient(region, new EnvironmentAwsCredential(region), baseUrl, options)` | Keep the region validation message |
| `ollama` | `OllamaCompletionClient(NormalizeOllamaBaseUrl(...))` | `OllamaClient(NormalizeOllamaBaseUrl(...), apiKey, options)` | Keep the `/v1` stripping and bearer pass-through |
| `ollama-cloud` | `OllamaCloudCompletionClient` | `OllamaCloudClient(apiKey, options)` | Keep the `OLLAMA_API_KEY` fallback |

**Rules:**
- **`ClientOptions`:** pass Mux's own `HttpClient` (certificate policy, per-endpoint headers), the silent logging module, and `TimeoutMs`. The client must not dispose the injected `HttpClient`; `LlmClient` keeps disposing it, as today.
- **API-key fallbacks:** add `ANTHROPIC_API_KEY` (anthropic), `OPENAI_API_KEY` (openai), and `GEMINI_API_KEY`, then `GOOGLE_API_KEY` (gemini), alongside the existing `XAI_API_KEY` and `OLLAMA_API_KEY`. An endpoint's own `apiKey` always takes precedence. Not added for `openai-compatible` or `azure-openai`, because their keys are server-specific.
- **Construction errors:** PolyPrompt 4.0 validates keys and endpoints at construction, for example *"apiKey looks like a URL"*. Let these surface unchanged through Mux's existing endpoint-error path, and in `mux probe`.

### 4.3 Requests and responses

- **Request mapping:** `BuildRequest` keeps building `ToolChatRequest` and `CompletionOptions` (both unchanged in 4.0). Per-call options stay the base `CompletionOptions`, which every 4.0 client accepts, so the wrong-options-type rule (PP 4.5) never triggers.
- **Model:** Mux keeps sending `Model` on every call from `endpoint.Model`. When `endpoint.Model` is blank, the provider default applies. Some 4.0 clients have none (`OpenAiCompatibleClient`, `BedrockClient` completion, `OllamaCloudClient`) and throw `InvalidOperationException` before sending. Surface that message as a configuration error, not a model error. The endpoint forms already require a model; check that the CLI `--model` path and hand-edited `endpoints.json` give the same clear error.
- **Responses for `openai`:** set `Store = false` explicitly on `OpenAiCompletionOptions` for the `openai` adapter. It is PolyPrompt's default, but setting it records Mux's intent: no server-side storage, with reasoning carried in `ProviderState`.
- **`ProviderState`:** no change. `LlmClient` → `AgentLoop` → `ConversationMessage.ProviderState` → `SessionMergePolicy` is provider-agnostic. Confirm by test (section 7) that an OpenAI Responses turn's encrypted reasoning survives save, resume, and compaction.
- **Usage mapping:**
  - `cachedIsAdditional` stays true only for Anthropic and Bedrock.
  - OpenAI Responses reports cached tokens as a subset of input (like xAI), and now also `CacheCreationTokens`.
  - Map `CacheCreationTokens` into `LlmUsage`, adding the field if it is missing, and into the usage record.
- **Malformed-tool-call recovery:** the Ollama-only recovery path is unchanged. Re-check its error-text matching against 4.0's Ollama error messages.

### 4.4 Model listing (`EndpointModelLister`)

Replace the hand-rolled HTTP listing with the provider client's `ListModelsAsync` for every adapter where `client.Supports(Capability.Models)`:

| Adapter | Listing |
|---|---|
| openai, openai-compatible, vllm | yes |
| azure-openai | per PolyPrompt's P0 outcome (PP 6.6) |
| xai | yes |
| anthropic | yes (**new**) |
| gemini | yes (**new**) |
| vertex | no |
| bedrock | yes (**new**) |
| ollama, ollama-cloud | yes |

Keep `EndpointModelListResult`'s failure classification: map `CapabilityNotSupportedException` to "this provider does not support model listing" and HTTP failures as today. This updates `mux endpoint models`, the TUI and desktop "import models" actions, the web dashboard, and VS Code, because they all call `EndpointModelLister`. Ollama's richer metadata (`SupportsTools`) can flow into the import UI later. Not required for 1.8.0.

### 4.5 Configuration (`EndpointConfig`, `SettingsLoader`)

- **`ApiVersion`:** keep the property so existing `endpoints.json` files load, but mark it obsolete in its XML doc and ignore it. When it is set on an `azure-openai` endpoint, log one warning per process: *"apiVersion is no longer used; Azure OpenAI now uses the v1 API (deployment name in `model`)."*
- **`AuthPlacement` docs:** now implemented by PolyPrompt for `openai-compatible` and `vllm`. For `openai`, `OpenAiClient` always uses bearer auth; if `authPlacement` is set to header or query on an `openai` endpoint, warn and suggest `openai-compatible`.
- **The `openai`-pointed-at-a-gateway case (the one behavior change users can hit):** an `openai` endpoint whose `baseUrl` host is not `api.openai.com` now speaks Responses. Many gateways only implement Chat Completions. Mux does **not** silently rewrite the adapter, because some gateways implement `/responses`. Instead:
  - `SettingsLoader` logs a one-time notice for such endpoints: *"Endpoint 'X' uses adapter openai with a non-OpenAI baseUrl. Since Mux 1.8 this adapter uses the OpenAI Responses API. If the server only supports Chat Completions, change the adapter to openai-compatible."*
  - PolyPrompt's 404/405 hint (PP 6.1) reaches the user as the turn error.
  - `mux probe` reports it explicitly (4.6).

### 4.6 `mux probe`

- **Construction errors:** report PolyPrompt 4.0 construction errors as configuration failures.
- **`openai` endpoints:** make one minimal Responses call. On 404/405, report *"server does not implement the Responses API; use adapter openai-compatible"*.
- **Model listing:** report whether the provider supports it, using `Supports(Capability.Models)`.

### 4.7 Usage, cost, and telemetry

- **Applied reasoning effort:** record `AppliedReasoningEffort` per call:
  - a new nullable `applied_effort` column in `usage.db` (additive schema migration, multi-process safe like the existing ones);
  - a field on `LlmCallMetrics`;
  - the `polyprompt.reasoning.effort` attribute already emitted by PolyPrompt, plus Mux's own `Mux` meter tag on LLM spans.
- **Usage views:** show applied effort in `/usage` call detail, the desktop and web usage views, and the calls modal.
- **Cache writes:** `PricingTable` cost math must count `CacheCreationTokens` for OpenAI at the cache-write rate where the price table has one, and not double-count. Add a test.
- **Default prices:** add `DefaultPricing` entries for the new default and recommended models listed in PP 5.3:
  - OpenAI: `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-luna`;
  - Anthropic: `claude-sonnet-5-5`, `claude-opus-5-5`, `claude-haiku-5-5`;
  - Gemini: the chosen Flash model;
  - Cohere: `command-a-plus-05-2026`, if it was adopted.

  Take every price from the provider's pricing page on the day of the change and note the date in a code comment. Do not copy prices from memory or from this plan.

### 4.8 Reasoning effort levels

- **`ReasoningLevelEnum`:** add `None` (`"none"`), `XHigh` (`"xhigh"`), and `Max` (`"max"`), and map them in `LlmClient.MapReasoningLevel` to PolyPrompt's new levels.
- **The existing override fields:** `ReasoningEffortConfig.OpenAiValue`, `GeminiThinkingBudget`, and `OllamaThink` map to whatever 4.0 shipped for exact per-provider values (`Pp.ReasoningEffort` members, or `ResponsesApiCompletionOptions.ReasoningEffortValue` for the Responses clients). Check the as-built notes; keep config compatibility either way.
- **Surfaces:** update every effort picker:
  - TUI `EffortSelectModal` and `/effort` completion;
  - desktop `ReasoningEffortDialog`, with `DesktopStrings` for all **11** languages;
  - the web dashboard effort control;
  - VS Code (`src/api/types.ts` and the effort command);
  - the REST API docs (`Mux.Server/Documentation/ApiDoc.cs`, `docs/REST_API.md`) and the Postman collection (`assets/postman/mux.postman_collection.json`).
- **Help text:** say that not every model supports every level, and that the usage view shows what was actually sent.

## 5. Surfaces

| Surface | Change |
|---|---|
| TUI (`EndpointFormModal`) | Hide the `apiVersion` field for `azure-openai` (show a note instead). Field help for `openai` says "OpenAI Responses API; use openai-compatible for other servers". |
| Desktop (`EndpointFormDialog`) | Same changes, with i18n for all 11 languages |
| Web dashboard (`DashboardPage`, endpoint editor) | Same changes |
| VS Code (`manage/actions.ts`, `api/types.ts`) | Same changes; keep `apiVersion` optional in the type for older servers |
| REST API (`ChatRoutes`, `ChatDtos`, `ApiDoc`) | `apiVersion` documented as ignored; new effort values documented |
| All | Model import now works for Anthropic, Gemini, and Bedrock (4.4); effort pickers (4.8); applied effort in usage views (4.7) |

## 6. Documentation

- **`docs/PROVIDERS.md`:**
  - **OpenAI:** now the Responses API. Add a "Which adapter?" note (`openai` for OpenAI or servers that implement `/responses`; `openai-compatible` for vLLM, LM Studio, OpenRouter, gateways), and mention the `OPENAI_API_KEY` fallback and `store` being off.
  - **Anthropic and Gemini:** the env-var fallbacks and model listing.
  - **Antigravity:** a short subsection: Antigravity is Google's agent IDE, not an API; its Gemini models are reached through the `gemini` adapter with an AI Studio key. Antigravity account sign-in is not supported.
  - **Logins:** a "Credentials" note that Mux supports API keys only, with no OAuth or subscription logins.
  - **Azure OpenAI:** the v1 API, `model` as the deployment, `apiVersion` ignored.
  - **Verify the connection:** model listing coverage updated.
- **`docs/CONFIG.md`:** `apiVersion` obsolete; `authPlacement` scope; the new effort levels; the key fallback table.
- **`docs/USAGE.md`:** `/effort` levels, applied effort in `/usage`, `mux endpoint models` coverage.
- **`README.md`:** the Model providers section and highlights (Responses for OpenAI, model listing for every major provider, API keys only), plus the version note at the top for 1.8.0.
- **`src/Mux.Core/PACKAGE.md`:** the PolyPrompt 4.0 dependency.
- **`docs/REST_API.md`, `ApiDoc.cs`:** effort values, `apiVersion`.
- **`CHANGELOG.md`:** a `1.8.0` entry:
  - **Changed:** `openai` uses Responses (with the gateway note and how to switch); Azure on the v1 API; PolyPrompt 4.0.
  - **Added:** effort levels, applied effort, model listing for Anthropic, Gemini, and Bedrock, and key fallbacks.
  - **Deprecated:** `apiVersion`.
  - **Removed:** `QueryStringAuthHandler` (internal).
  - **Validation:** test counts.

## 7. Tests

Mux's suites live in `Test.Shared` and run through `Test.Automated`, `Test.Xunit`, and `Test.Nunit`. Run them on net8.0 and net10.0.

- **Bridge suite (`LlmBridgeSuite`, `LocalLlmTestServer`):**
  - Existing `openai` adapter cases against the Chat Completions mock move to `openai-compatible` and must pass unchanged in substance.
  - Add a Responses route to `LocalLlmTestServer` (`/v1/responses`, SSE and non-streaming, with `reasoning` items carrying `encrypted_content`). Add `openai` adapter cases: text, streaming tool calls, reasoning display, usage including cached and cache-write tokens, and the 404 hint.
- **State:** an `openai` Responses turn's `ProviderState` survives `SessionStore` save and resume, `SessionMergePolicy`, compaction, and cross-surface export and import. A 3.7-era saved session (fixture) with xAI and OpenAI-compatible state resumes and replays correctly on 1.8.
- **Construction:** every adapter builds the right 4.0 client:
  - `AuthPlacement` bearer, header, and query reach the mock correctly for `openai-compatible` and `vllm`;
  - the key fallbacks apply and lose to an explicit `apiKey`;
  - construction-error text surfaces;
  - `apiVersion` is ignored with exactly one warning;
  - an `openai` endpoint with a non-OpenAI `baseUrl` logs the notice once.
- **Model listing:** each adapter in 4.4. Unsupported adapters give the classified "not supported" result.
- **Effort:** every `ReasoningLevelEnum` value round-trips through JSON config and maps to the right PolyPrompt level. `AppliedReasoningEffort` lands in `usage.db` (with the migration tested on an existing 1.7 database) and in `LlmCallMetrics`.
- **Cost:** `CacheCreationTokens` math for OpenAI; no double counting.
- **Surfaces:**
  - form tests (`FormModalsSuite`) for the hidden `apiVersion` and the help text;
  - the desktop effort dialog;
  - VS Code unit tests for types and actions;
  - the web dashboard endpoint and effort controls, where they are covered today.
- **Probe:** a `mux probe` case for an `openai` endpoint whose mock lacks `/responses`.
- **Postman (optional):** add an OpenAI Responses mock and simulation under `postman/`, mirroring `postman/mocks/xai`, for end-to-end checks.

**Commands** (use the existing scripts and runners):

```bash
dotnet build src/Mux.sln -c Release
dotnet run --project src/Test.Automated -c Release -f net8.0
dotnet run --project src/Test.Automated -c Release -f net10.0
dotnet test src/Test.Xunit -c Release
dotnet test src/Test.Nunit -c Release
(cd src/Mux.VSCode && npm test)
```

Every case passes with none skipped.

**Live check (not a release gate; record in the CHANGELOG whether it ran):** with real keys, run one tool-using turn on each of `openai` (a reasoning model, two or more tool calls, then resume the session), `anthropic`, `gemini`, and `xai`, and `mux endpoint models` for each.

## 8. Work plan

| Phase | Work | Exit gate |
|---|---|---|
| **M0 Prep** | Read the PolyPrompt 4.0 as-built notes and CHANGELOG, and reconcile any names in this plan with what shipped. | This file updated with the reconciled names |
| **M1 Core swap** | Package bump; `CreateClient` (4.2); request and usage mapping (4.3); delete `QueryStringAuthHandler`; migrate and extend the bridge tests. | Build green; bridge, state, and construction tests pass |
| **M2 Listing, config, probe** | `EndpointModelLister` (4.4); `SettingsLoader` notices and `ApiVersion` handling (4.5); `mux probe` (4.6). | Listing, config, and probe tests pass |
| **M3 Effort and telemetry** | `ReasoningLevelEnum` values and mapping (4.8); `applied_effort` column and metrics; cache-write cost math; default prices from the pricing pages (4.7). | Effort, usage, and cost tests pass |
| **M4 Surfaces** | TUI, desktop (11 languages), web, VS Code, REST docs, Postman collection (section 5). | Surface tests pass; manual smoke test of each surface's endpoint form and effort picker |
| **M5 Docs and release** | Section 6 docs, then section 9. | Released |

Commit at the end of each phase.

## 9. Release

1. **Version:** bump every `1.7.0` to `1.8.0`:
   - each project `.csproj` (`Mux.Core`, `Mux.Search`, `Mux.Server`, `Mux.Desktop.Core`, `Mux.Desktop`, `Mux.Cli`, `Mux.Agent`, `Mux.Publisher`);
   - `src/Mux.Core/Settings/Defaults.cs`;
   - `src/Mux.Server/Documentation/ApiDoc.cs`;
   - the README badge and version note;
   - `src/Mux.VSCode/package.json`, `package-lock.json`, and `src/api/types.ts`;
   - `test/unit/agentStart.test.ts` where it pins the version.

   `grep -rn "1\.7\.0"` (excluding CHANGELOG history and `archive/`) should come back empty.
2. **Final validation:** run everything in section 7 on a clean checkout of the release commit.
3. **NuGet:** publish Mux's libraries and their symbol packages with the existing script, which packs in dependency order and skips versions already published. The maintainer supplies the API key at release time; never write it to a file or commit it.
   ```bash
   ./scripts/macos/publish-nuget.sh "$NUGET_API_KEY"
   ```
4. **Other artifacts, as usual for a Mux release:** installers (`scripts/<os>/build-installers.sh`), the VS Code extension (`scripts/<os>/publish-vscode.sh`, per `PUBLISH_VSCODE.md`), and the desktop publish scripts.
5. **Commit and push to `main`** with the phase commits first, then the release commit carrying the version and CHANGELOG.

## 10. Not in 1.8.0 (follow-ups)

- **Citations and `RawResponse`:** Mux enables no hosted search tools today, so citations would be empty. If Mux later exposes provider web search, render `Citations` in the transcript on every surface.
- **Hosted tools:** OpenAI and xAI hosted tools (web search, code interpreter, MCP) stay off, as today. Mux runs its own local tools.
- **Native compaction:** PolyPrompt's `CompactConversationAsync` and OpenAI's `context_management` could replace or augment Mux's own compaction for Responses providers. Evaluate separately.
- **Ollama model metadata:** surface `SupportsTools` in the import UI to warn about models that drop tools.

## 11. If subscription access is wanted later

Do not implement OAuth for Anthropic, OpenAI, or Google consumer accounts directly. Anthropic restricts consumer OAuth tokens to its own products, and Antigravity's account quota goes through an unofficial endpoint. The sanctioned route is PolyPrompt's harness clients (`ClaudeCodeClient`, `CodexClient`, `GeminiCliClient`), which drive the user's own installed and signed-in CLI. In Mux they would be a **subagent backend**, not an endpoint adapter, because the CLI runs its own tools, approvals, and sandbox, so Mux's tools, approval modal, and write lease don't apply to it. That would be a separate plan.

## 12. Definition of done

- Mux builds against PolyPrompt 4.0.0, with no references to removed 3.x types.
- `openai` endpoints use the Responses API, and encrypted reasoning survives tool calls, save, resume, and compaction. `openai-compatible` and `vllm` keep Chat Completions with every key placement working.
- Anthropic, Gemini, OpenAI, and xAI each work with an API key from config or from their environment-variable fallback, and each supports `mux endpoint models`.
- `azure-openai` works on the v1 API, and `apiVersion` is ignored with a warning.
- Users with an `openai` endpoint on a gateway get a clear notice, a clear turn error, and a clear `mux probe` result.
- The new effort levels work on every surface, applied effort is recorded and shown, and cache-write costs are correct.
- 1.7-era sessions and `usage.db` files load and work.
- Docs (PROVIDERS, CONFIG, USAGE, README, REST_API, PACKAGE, CHANGELOG) are updated, including the Antigravity and API-keys-only notes.
- All tests pass on net8.0 and net10.0, and the VS Code tests pass.
- The version is 1.8.0; packages, symbols, and installers are published; work is committed and pushed to `main`.

## 13. As-built record (1.8.0)

### 13.1 Reconciliation with shipped PolyPrompt 4.0 (M0)

The shipped API matched this plan's names: `ProviderClientBase`, `ClientOptions`, `OpenAiCompatibleClientOptions.AuthPlacement`, the provider clients and constructors, `Capability`, `ReasoningEffortLevel.None`/`XHigh`/`Max`, `ResponseBase.AppliedReasoningEffort`, and `TokenUsage.CacheCreationTokens`. `Pp.ReasoningEffort` kept `OpenAiValue`, `GeminiThinkingBudget`, and `OllamaThink`, so `ReasoningEffortConfig` maps onto it unchanged. Two shipped behaviors shaped the implementation:
- **Keys are required at construction** for the keyed native clients. Mux resolves the key, then the environment fallback, and otherwise throws its own `InvalidOperationException` naming the endpoint and the variable.
- **PolyPrompt's `ListModelsAsync` ends quietly on an HTTP failure.** Mux recovers the status from the client's `CallDetails`, so `EndpointModelLister` keeps its error classification.

### 13.2 Deviations from the plan

- **`QueryStringAuthHandler` kept (4.2).** PolyPrompt's `AuthPlacement` sets the key per request, which would override an explicit `headers` entry (Mux's rule is that the explicit entry wins) and would append a duplicate query parameter. PolyPrompt's `OllamaClient` also has no placement option. Mux keeps its transport placement for `ollama`, `openai-compatible`, and `vllm`; `openai` always sends a bearer key.
- **`openai` key from a header.** For compatibility, an `openai` endpoint without `apiKey` takes its bearer token from a hand-written `Authorization: Bearer ...` header.
- **Advisories instead of `SettingsLoader` log lines (4.5).** Mux.Core has no logger, and console output would corrupt the TUI. A new `EndpointAdvisories` helper produces the notes, shown by `mux probe` (text, and an `advisories` array in JSON) and `mux endpoint show`. The endpoint editors hide the settings that no longer apply instead of showing a note.
- **`Store = false`** is not set by Mux explicitly. PolyPrompt 4.0 always sends `store: false` on Responses clients unless set, and a test asserts it on the wire.
- **`--effort none`** was an alias for `off` and is now the `None` level. Only `off` clears the effort. Recorded in the CHANGELOG.
- **Enum order.** `XHigh`, `Max`, and `None` were appended to `ReasoningLevelEnum`, so earlier numeric values are unchanged for Mux.Core consumers. The UI order is set explicitly per surface.
- **Applied effort in the terminal (4.7).** It is recorded and shown in the web dashboard's call details, as a tooltip on each row of the desktop call table, in the REST call records, and in the VS Code type. The terminal calls list (a fixed-width one-line table) and VS Code (which has no per-call detail view) do not display it yet.
- **Default prices (4.7).** Added from the providers' pricing pages on 2026-11-12 for `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-luna`, `claude-opus-5-5`, `claude-sonnet-5-5`, `claude-haiku-5-5`, and `gemini-3.8-flash`. Cohere's pricing page did not list `command-a-plus-05-2026`, so it has no default rate. `DefaultPricing.Version` is `2026-11`.
- **Postman collection.** No change was needed: it has no effort or `apiVersion` values.
- **Cache-write cost (4.7).** No code change was needed. OpenAI reports cached and cache-write tokens inside `input_tokens`, and Mux adds cache buckets only for Anthropic and Bedrock. A test asserts the totals.

### 13.3 Validation

- `Test.Automated` (net10.0, Debug, before the final doc, price, and version changes): 2034 cases, 2011 passed, 0 failed, 23 skipped (the same 23 environment-gated cases as on `main`). 13 new cases: OpenAI Responses round trip, the 404 hint, required keys, the key fallback, every effort level on Responses, five model-listing dispatch cases, advisories, the probe Responses check, applied effort, the v1-to-v2 `usage.db` migration, and the `--effort` levels.
- Final validation on a fresh Release build: Test.Automated 2035 cases on .NET 8 and .NET 10 (2012 passed, 0 failed, 23 skipped); Test.Xunit 2011 passed and Test.Nunit 2011 passed (23 skipped) on each framework, run before the last two fixes; `tools/i18n/gen.js --check` clean.
- Two fixes after the plan's scope: the `openAiValue` effort override now reaches `reasoning.effort` on `openai` and `azure-openai` (PolyPrompt 4.0 reads it from the Responses options), and JSON errors in `endpoints.json`, `settings.json`, and `mcp-servers.json` name the file, its path, and a 1-based line and column (`ConfigFileJsonException`).
- VS Code: 137 tests, all passing.
- No live provider calls were made (no credentials in this environment); the section 7 live check was not run.

### 13.4 Not done here (needs the maintainer)

Committing and pushing to `main`, NuGet publishing (`publish-nuget.sh` with the maintainer's key), installers, the VS Code extension, and desktop publishing.

