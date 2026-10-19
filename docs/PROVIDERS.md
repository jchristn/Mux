# Model providers

Mux connects to local runners, self-hosted servers, and hosted APIs. Choose the adapter
for your backend and a model that supports function tools. Each section below uses the
same pattern: connection, credentials, endpoint example, and provider-specific notes.

## Configure an endpoint

Endpoints live in `~/.mux/endpoints.json`, or under your `MUX_CONFIG_DIR`. The TUI,
desktop, web dashboard, and VS Code extension share this file. Use their endpoint
editors or edit the JSON directly. Each example below is **one entry** in the
`endpoints` array; add it alongside your other endpoints:

```json
{
  "endpoints": [
    {
      "name": "my-model",
      "adapterType": "openai-compatible",
      "baseUrl": "http://localhost:1234/v1",
      "model": "MODEL_ID"
    }
  ]
}
```

Replace `MODEL_ID` and other placeholders with values available on your backend.
Select a saved endpoint with `mux --endpoint <name>`, or choose it in your surface's
endpoint picker. `isDefault: true` makes an endpoint the default.

For API keys, put an environment reference such as `${OPENAI_API_KEY}` in `apiKey`
and set that variable in the environment of the process running Mux. An unset
referenced variable fails the request before sending it. The name of the variable
is your choice; the names below are conventions. Ollama Cloud and xAI additionally
fall back to `OLLAMA_API_KEY` and `XAI_API_KEY`, respectively, when `apiKey` is absent.
Their endpoint-level key takes precedence over that fallback.

Common controls for context size, reasoning effort, timeouts, and approval apply to
these endpoints. See the [configuration reference](CONFIG.md) for the full schema.

## Ollama

- **Connection:** a local or remote Ollama service, usually `http://localhost:11434`.
- **Credentials:** none for an ordinary local server; configure a key if your gateway requires one.

```json
{
  "name": "ollama-local",
  "adapterType": "ollama",
  "baseUrl": "http://localhost:11434",
  "model": "MODEL_ID"
}
```

Use Ollama's native API root; Mux tolerates and strips a trailing `/v1` for this
adapter. The TUI's **Import from Ollama** action can add installed models in bulk.
A signed-in Ollama service can also proxy cloud models using their CLI names,
such as `deepseek-v4.1-flash:cloud`.

`quirks.enableMalformedToolCallRecovery` defaults to `true`. Recognized tool-call
parsing and token-repeat HTTP 500 errors retry up to twice with corrective context,
before streaming starts or during nonstreaming chat. Other HTTP errors and failures
after a stream opens are not retried by this setting. Exhausted recovery retains
`llm_error`; retry telemetry distinguishes transport, malformed-tool-call, and
token-repeat failures. Set the quirk to `false` to disable generation recovery.

The [proposed Ollama launcher integration](https://github.com/ollama/ollama/pull/19125)
adds `ollama launch mux --model <model>`. It requires an Ollama build containing the
Mux integration until that contribution is accepted and released upstream.

## Ollama Cloud

- **Connection:** fixed native API at `https://ollama.com`; no local runner required.
- **Credentials:** Ollama API key, sent as a bearer token.

```json
{
  "name": "ollama-cloud",
  "adapterType": "ollama-cloud",
  "model": "MODEL_ID",
  "apiKey": "${OLLAMA_API_KEY}"
}
```

Use direct API model names from `https://ollama.com/api/tags`, such as
`deepseek-v4.1-flash`, rather than the local CLI's `:cloud` suffix. This adapter's
URL and bearer scheme are fixed; `baseUrl` and custom key placement do not change them.
Use `ollama` for connections through a signed-in local Ollama service.

## OpenAI

- **Connection:** OpenAI API at `https://api.openai.com/v1`.
- **Credentials:** OpenAI API key, normally sent as a bearer token.

```json
{
  "name": "openai",
  "adapterType": "openai",
  "baseUrl": "https://api.openai.com/v1",
  "model": "MODEL_ID",
  "apiKey": "${OPENAI_API_KEY}"
}
```

Choose a model your API account can access. Use `openai-compatible` for a different
server implementing that API format. OpenAI-family adapters also support custom
header or query-string key placement for gateways; see [authentication](CONFIG.md).

## Anthropic

- **Connection:** native Messages API at `https://api.anthropic.com`; blank `baseUrl` uses this default.
- **Credentials:** Anthropic API key, sent in `x-api-key`.

```json
{
  "name": "anthropic",
  "adapterType": "anthropic",
  "model": "MODEL_ID",
  "apiKey": "${ANTHROPIC_API_KEY}"
}
```

Use the native `anthropic` adapter for Anthropic's protocol and tool handling.
Custom `authPlacement` does not change its authentication scheme.

## Google Gemini

- **Connection:** Gemini API at `https://generativelanguage.googleapis.com`; blank `baseUrl` uses this default.
- **Credentials:** Google AI Studio API key, sent in `x-goog-api-key`.

```json
{
  "name": "gemini",
  "adapterType": "gemini",
  "model": "MODEL_ID",
  "apiKey": "${GEMINI_API_KEY}"
}
```

This is the direct Gemini API. Choose `vertex` when accessing Gemini through Google
Cloud with project, region, and Application Default Credentials. Native Gemini
thinking budgets use the existing reasoning-effort controls.

## Azure OpenAI

- **Connection:** your Azure resource endpoint, such as `https://RESOURCE.openai.azure.com`.
- **Credentials:** Azure OpenAI API key, sent in `api-key`.

```json
{
  "name": "azure-openai",
  "adapterType": "azure-openai",
  "baseUrl": "https://RESOURCE.openai.azure.com",
  "model": "DEPLOYMENT_NAME",
  "apiKey": "${AZURE_OPENAI_API_KEY}"
}
```

Both the resource URL and deployment name are required. `model` is the deployment
name, not the underlying model's catalog name. Set `apiVersion` if your resource
requires a version other than the provider library's default.

## Google Vertex AI

- **Connection:** regional Vertex AI endpoint derived from your project and region.
- **Credentials:** Application Default Credentials, including `GOOGLE_APPLICATION_CREDENTIALS` when using a credentials file.

```json
{
  "name": "vertex",
  "adapterType": "vertex",
  "model": "MODEL_ID",
  "project": "GOOGLE_CLOUD_PROJECT_ID",
  "region": "GOOGLE_CLOUD_REGION"
}
```

Both `project` and `region` are required. Credentials must authorize access to the
selected project and model. `apiKey` and custom key placement do not configure ADC.
A custom `baseUrl` can override the derived endpoint when needed.

## AWS Bedrock

- **Connection:** regional Bedrock endpoint derived from `region`.
- **Credentials:** `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, and `AWS_SESSION_TOKEN` for temporary credentials.

```json
{
  "name": "bedrock",
  "adapterType": "bedrock",
  "model": "BEDROCK_MODEL_OR_INFERENCE_PROFILE_ID",
  "region": "AWS_REGION"
}
```

The region is required. Use a model or inference profile your AWS credentials can
invoke in that region. Requests use AWS signing; `apiKey` does not configure AWS
credentials. A custom `baseUrl` can override the derived endpoint when needed.

## xAI Grok

- **Connection:** native Responses API at `https://api.x.ai/v1`; blank `baseUrl` uses this default.
- **Credentials:** xAI API key, sent as a bearer token.

```json
{
  "name": "grok",
  "adapterType": "xai",
  "model": "MODEL_ID",
  "apiKey": "${XAI_API_KEY}"
}
```

`grok`, `x-ai`, and `spacex` are accepted adapter aliases; saved JSON uses `xai`.
API-root URLs and custom gateways are supported. Custom key placement does not
change this adapter's bearer scheme.

Reasoning summaries use the existing thinking display. Encrypted reasoning remains
opaque continuation state and survives tool calls, persistence, and session resume.
Mux executes its own local function tools; xAI-hosted tools are not enabled automatically.

## vLLM

- **Connection:** your vLLM server's OpenAI-compatible URL, such as `http://localhost:8000/v1`.
- **Credentials:** none unless the server or gateway requires a key.

```json
{
  "name": "vllm",
  "adapterType": "vllm",
  "baseUrl": "http://localhost:8000/v1",
  "model": "MODEL_ID"
}
```

If authentication is enabled, set `apiKey` to a literal key or environment reference.
Model availability and tool support depend on your server configuration and model.

## OpenAI-compatible servers

- **Connection:** the API URL of LM Studio or another compatible server or gateway.
- **Credentials:** whatever the server requires; keys support bearer, custom-header, or query placement.

```json
{
  "name": "compatible-server",
  "adapterType": "openai-compatible",
  "baseUrl": "http://localhost:1234/v1",
  "model": "MODEL_ID"
}
```

Use the URL and model name advertised by your server. Optional `apiKey`,
`authPlacement`, `authParameterName`, and custom `headers` configure gateways.
Compatibility and tool support depend on the server's implemented capabilities.

## Verify the connection

Select the endpoint and run a short prompt before a coding task:

```bash
mux --endpoint my-model -p "Reply with hello."
mux probe --endpoint my-model
```

`mux endpoint models my-model` can discover models from Ollama, Ollama Cloud,
xAI, and OpenAI-compatible model-list endpoints. Other native providers do not
necessarily expose that discovery API through Mux; use the provider's catalog
or deployment configuration for their model identifiers.

For a tool check, ask Mux to read a small file and describe it. Tool permissions,
reasoning support, and context limits remain model- and endpoint-specific.
