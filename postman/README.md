# Ollama launcher smoke test

The local simulation models the native Ollama endpoints used by the launcher
and streams a deterministic `MUX_LAUNCH_OK` response. It is derived from Ollama's
API shapes and the launcher's requests, not from a provider-owned mock. Owner
mock discovery was unavailable because this Postman CLI is signed out.

Start it in one terminal:

```sh
postman simulation run postman/simulations/ollama-launch.sim.yaml --port 4917 --no-history --output ndjson
```

With the updated Mux executable on PATH and an Ollama build containing the Mux
integration, run in another terminal (use a temporary config directory):

```sh
export MUX_CONFIG_DIR="$(mktemp -d)"
export OLLAMA_HOST=http://127.0.0.1:4917/ollama
ollama launch mux --model deepseek-v4.1-flash:cloud -- -p "Reply MUX_LAUNCH_OK" --output-format json --effort off
```

Expect exit 0 and JSON with `status: completed` and `result: MUX_LAUNCH_OK`.
The simulation log must contain a successful POST `/api/chat` request. This
checks the actual Ollama-to-Mux command, endpoint configuration, model selection,
and native streaming path; it does not prove cloud sign-in or live inference.
Live `ollama-cloud` inference and a file-read tool round trip were verified
separately with `deepseek-v4.1-flash`. No credentials belong in these fixtures.

## xAI Responses integration

The xAI contract and fixture are reused from the local PolyPrompt repository's
`postman/xai.openapi.json` and `postman/mocks/xai/`, originally derived from the
provider's official OpenAPI. Mux-specific markers verify local file tools,
encrypted continuation replay, incomplete streams, and saved-session resume.
No xAI credentials or provider-hosted tools are used by this simulation.

```sh
postman simulation run postman/simulations/xai.sim.yaml --port 4912 --no-history --output ndjson
# In another terminal, after building Mux.Cli for net8.0:
python3 postman/test-xai.py
```

The script runs the real CLI and server, checks authenticated model discovery,
a two-tool/three-response native loop, usage, truncated streams, and a web-style
follow-up with a display-only client transcript. The server must restore the
complete native state from the persisted session. These tests verify Mux against
the local contract fixture; live xAI inference requires an API key.
