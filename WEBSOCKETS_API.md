# mux WebSockets API

`mux serve` and the tray agent host one WebSocket endpoint, `/v1.0/ws`, on the same port as the REST API. Every mux
surface uses it: the web dashboard and VS Code watch runs and answer tool approvals over it, and the terminal and
desktop app publish the runs they drive in their own process into it so the other surfaces can mirror them. Mux
Cloud relays the same socket to the cloud dashboard and the mobile app (section 9).

This document covers the full message surface. The REST API is in [docs/REST_API.md](docs/REST_API.md).

## 1. Protocol summary

- URL: `ws://127.0.0.1:<port>/v1.0/ws` (default port 8710). `wss://` when the server runs with `rest.ssl`.
- Every message is one JSON text frame. Binary frames are ignored.
- Server frames carry an `eventType` discriminator. Client frames carry an `action` discriminator.
- Run event frames are the **canonical event envelope**: byte for byte the shape `mux print --output-format jsonl`
  emits, including `contractVersion` (currently `2`), `eventType`, and `timestampUtc`.
- Names are camelCase. Receivers ignore unknown fields and unknown `eventType` values.
- Timestamps are UTC ISO 8601.

## 2. Authentication

When an API key is configured (`rest.apiKey`, generated on first `mux serve`), the upgrade must carry it:

| Carrier | Example | Notes |
| --- | --- | --- |
| `Authorization` header | `Authorization: Bearer mux_...` | Native clients, VS Code, the Mux Cloud tunnel forwarder |
| `apiKey` query parameter | `/v1.0/ws?apiKey=mux_...` | Browsers, which cannot set upgrade headers |

A failed check sends one frame and ends the session:

```json
{ "eventType": "error", "code": "unauthorized", "message": "Authentication required." }
```

With `--no-auth` (no key configured) every connection is accepted.

## 3. Connection lifecycle

1. Connect with the key.
2. The server sends `server.connected`.
3. Optionally send `subscribe` with `all: true` to receive conversation-list changes.
4. Send at most one run or session `subscribe`.
5. The server replays the frames the run has already emitted (up to 512 per run), then streams new frames live.
6. A run-scoped subscription closes the socket with `1000` when the run ends. A session-scoped subscription stays
   open across runs.
7. Send `approve` frames to answer tool approvals at any time.
8. Close the socket when done. Closing never cancels a run. Cancel a run with `POST /v1.0/api/runs/{runId}/cancel`.

## 4. Server-to-client frames

### `server.connected`

Sent once after authentication.

| Field | Type | Description |
| --- | --- | --- |
| `eventType` | string | `server.connected` |
| `product` | string | `mux` |
| `version` | string | Server version |

```json
{ "eventType": "server.connected", "product": "mux", "version": "2.0.0" }
```

### `sessions_changed`

The conversation list changed (a run finished, a session was saved, renamed, tagged, or deleted). Sent to
sockets that subscribed with `all: true`. Refresh the list with `GET /v1.0/api/sessions`.

| Field | Type | Description |
| --- | --- | --- |
| `sessionId` | string | The session that changed, or empty when unknown |

### `transcript_changed`

A session's transcript changed without a run streaming through this hub (a turn persisted by the terminal or
desktop app, a `PUT /v1.0/api/sessions` upsert, or a session file changed on disk). Sent to sockets subscribed to
that session. Reload with `GET /v1.0/api/sessions/detail?id=`.

| Field | Type | Description |
| --- | --- | --- |
| `sessionId` | string | The session |

### `error`

| Field | Type | Description |
| --- | --- | --- |
| `code` | string | `unauthorized` or `not_found` (subscribe to an unknown run id) |
| `message` | string | Description |

Run error envelopes (section 5) also use `eventType: "error"`. They carry `contractVersion` and `errorCode`.

### Run event envelopes

Every frame of a subscribed run. Common fields: `contractVersion`, `eventType`, `timestampUtc`.

| `eventType` | Key fields | Meaning |
| --- | --- | --- |
| `run_started` | `runId`, `sessionId`, `endpointName`, `model`, `approvalPolicy`, `workingDirectory`, `maxIterations`, `toolsEnabled`, context window fields | The run began |
| `assistant_text` | `text` | A token delta of the reply |
| `assistant_thinking` | `text` | A reasoning delta, when the model streams reasoning |
| `tool_call_proposed` | `toolCall: { id, name, arguments }` | The model proposed a tool call |
| `approval_requested` | `runId`, `sessionId`, `toolCallId`, `toolName`, `arguments`, `toolAutoApproveSeconds` | The run is waiting for a decision on a tool call (section 6) |
| `approval_resolved` | `runId`, `sessionId`, `toolCallId`, `decision` | The pending approval was decided: `y`, `always`, or `n` |
| `ask_user` | `runId`, `sessionId`, `questionId`, `question`, `options: [ { label, description } ]`, `multiSelect`, `allowOther`, `allowSkip`, `timeoutSeconds` | The model asked the user a question and the run is waiting (section 6a) |
| `ask_user_resolved` | `runId`, `sessionId`, `questionId`, `dismissed` | The question was answered, skipped, or timed out |
| `tool_call_approved` | `toolCallId` | The tool call will run |
| `tool_call_completed` | `toolCallId`, `toolName`, `elapsedMs`, `result: { success, ... }` | The tool finished |
| `heartbeat` | `stepNumber` | Progress marker while tools run |
| `context_status` | `estimatedTokens`, `usableInputLimit`, `remainingTokens`, `remainingPercent`, `warningLevel` | Context window usage |
| `context_compacted` | `strategy`, `messagesBefore`, `messagesAfter`, `estimatedTokensBefore`, `estimatedTokensAfter` | History was compacted |
| `task_plan_updated` | `changeKind`, `changedTaskId`, `totalCount`, `completedCount`, `tasks` | The run's task plan changed |
| `hook` | `hookEvent`, `hookName`, `outcome`, `exitCode` | A configured hook ran |
| `error` | `code`, `errorCode`, `message` | A run error |
| `run_completed` | `runId`, `sessionId`, `status`, `iterationsCompleted`, `toolCallCount`, `errorCount`, `durationMs`, `usage`, optional `modelCallCount`, `firstModelCallTtftMs`, `modelStreamingMs` | The run ended. Terminal. |

`run_completed` timing fields are optional and nonnegative. `modelCallCount` counts primary model calls, excluding
transport retries, compaction, and title generation. Omission means unknown, not zero.

## 5. Client-to-server frames

Every client frame is `{ "action": "...", ... }`. Field names are case-insensitive. Malformed frames are ignored.

### `subscribe` (run)

```json
{ "action": "subscribe", "runId": "8b2f..." }
```

Attach to one run (the id comes from the chat stream's `run` SSE event or `GET /v1.0/api/runs`). The server
replays and then live-tails. The socket closes when the run ends. An unknown id returns `error` with
`not_found`.

### `subscribe` (session)

```json
{ "action": "subscribe", "sessionId": "9f1c..." }
```

Attach to every run of a session: the runs already in flight and every run that starts later, from any surface.
Never errors when the session is idle. Also delivers `transcript_changed` for the session. This is what "mirror on
by default" uses when a conversation is opened.

Only one run or session subscription is allowed per socket.

### `subscribe` (all)

```json
{ "action": "subscribe", "all": true }
```

Receive `sessions_changed` for every conversation-list change. It combines with one run or session
subscription on the same socket.

### `approve`

```json
{ "action": "approve", "runId": "8b2f...", "toolCallId": "call_1", "decision": "y" }
```

Resolve a pending tool approval. `decision` is `y` (approve once), `always` (approve and remember for this tool),
or anything else (deny). When `runId` is present, the approval resolves on that run, whether or not this socket
subscribed to it. Without `runId`, it resolves on the most recently attached run that has not ended. This
resolves the same pending approval as `POST /v1.0/api/chat/approve`.

### `answer`

```json
{ "action": "answer", "runId": "8b2f...", "questionId": "q_3c1...", "answers": ["Postgres"], "other": "and keep SQLite for tests", "skip": false }
```

Answer a pending `ask_user` question: `answers` are choice labels (one unless `multiSelect`), `other` is free text,
and `skip: true` skips. Run targeting works like `approve`. Not gated by `cloud.allowRemoteToolApproval`, because a
question has no side effects of its own. This resolves the same question as `POST /v1.0/api/chat/answer`.

### `publish`

```json
{ "action": "publish", "runId": "8b2f...", "sessionId": "9f1c...", "endpointName": "openai-gpt4o",
  "model": "gpt-4o", "frame": "{\"eventType\":\"assistant_text\",\"text\":\"...\"}" }
```

Producer path. A surface that runs the engine in its own process (the terminal, the desktop app) streams its run
into the hub so other surfaces can mirror it. The first frame for a `runId` creates the run in the hub. Each
`frame` is one serialized canonical envelope, relayed verbatim to subscribers. A `run_completed` frame ends the
run.

### `notify`

```json
{ "action": "notify", "sessionId": "9f1c..." }
```

A surface changed the conversation list out of band (rename, duplicate, delete). The hub broadcasts
`sessions_changed`.

### `notify-transcript`

```json
{ "action": "notify-transcript", "sessionId": "9f1c..." }
```

An in-process surface persisted a turn without streaming a run through the hub. The hub broadcasts
`transcript_changed` to the session's subscribers and `sessions_changed` to list subscribers.

## 6. Tool approvals

A run started through `POST /v1.0/api/chat/stream` uses the `AutoSafe` policy. Read-only tools run
immediately. A tool that would prompt (writing a file, running a process) behaves as follows:

- If the server runs without interactive approvals (`mux serve` without `--allow-tools`, and the tray agent when
  it is not connected to Mux Cloud), the tool is denied.
- Otherwise the run pauses. The hub emits `approval_requested` into the run's stream, and the chat stream response
  also receives an `approval` SSE event. A socket that subscribes while the run waits still sees the request,
  because it is part of the run's replay buffer.
- Any client may answer with an `approve` frame or `POST /v1.0/api/chat/approve`. The first answer wins. The hub
  then emits `approval_resolved`, and `tool_call_approved` or a denial error follows.
- `toolAutoApproveSeconds` is the countdown a client may show before approving on the user's behalf. The server
  never auto-approves on a timer, and canceling the run or disconnecting the chat stream denies the call.
- A run started with `autoApproveTools: true` (the dashboard's **Auto-approve tools** switch) approves these tools
  itself, so no `approval_requested` is emitted. It applies only when the caller could approve interactively.
- While a run waits, `GET /v1.0/api/runs/{runId}` reports status `awaiting_approval`.

For a run relayed from Mux Cloud while `cloud.allowRemoteToolApproval` is false, prompting tools are denied as if
interactive approvals were off, and `autoApproveTools` is ignored.

## 6a. Questions (`ask_user`)

A run started through `POST /v1.0/api/chat/stream` with `askUser: true` hands the model's `ask_user` calls to the user
(without it, the model is told no user is available and picks a default). The hub emits
`ask_user` into the run's stream (so it is in the replay buffer) and the chat stream response receives an `ask_user`
SSE event. Any client answers with an `answer` frame or `POST /v1.0/api/chat/answer`; the first answer wins and the
hub emits `ask_user_resolved`. An unanswered question times out after `timeoutSeconds` (900), and the model is told
to choose a sensible default. While a question waits, the run's status reads `awaiting_approval` and
`GET /v1.0/api/runs/{runId}` reports `PendingQuestionCount`. A question also raises the Mux Cloud
`approval_requested` machine status, so a phone gets a push notification.

## 7. Ordering, replay, and limits

- Frames of one run arrive in emission order.
- Each run keeps its last 512 frames for replay. A long run's oldest frames can fall out of the buffer.
- A slow subscriber never stalls a run. Each subscriber has a bounded queue of 1024 frames, and the oldest queued
  frame is dropped when it is full.
- Frames of different runs on one session-scoped socket may interleave.

## 8. Error handling

- An unauthorized upgrade gets one `error` frame (`unauthorized`).
- A malformed client frame is ignored.
- Subscribing to an unknown run gets `error` (`not_found`), and the socket stays open.
- A transport failure ends the session. Reconnect and subscribe again. The replay buffer fills in what was missed.

## 9. Mux Cloud tunnel (client side)

When a machine is connected to Mux Cloud (`mux cloud login`, or the Mux Cloud panel on any surface), the process
that owns the hub port (normally the tray agent) keeps one outbound WebSocket open to
`wss://app.usemux.ai/v1.0/tunnel`. Through it, the cloud dashboard and the mobile app reach this machine's REST API
and `/v1.0/ws` exactly as a local client does. The tunnel protocol is specified in Mux Cloud's
[WEBSOCKETS_API.md](https://github.com/jchristn/MuxCloud/blob/main/WEBSOCKETS_API.md), and the contract types ship in
the [`MuxCloud.Protocol`](https://www.nuget.org/packages/MuxCloud.Protocol) NuGet package (2.0.0).

On the machine side:

- **Enrollment.** `mux cloud login` exchanges your email and password for a machine id and machine secret
  (`POST https://app.usemux.ai/v1.0/api/machines/enroll`). They are saved in `settings.json` under `cloud`. The
  password is never stored.
- **Sign-out.** `mux cloud logout` (and Sign out on every other surface) first calls
  `POST /v1.0/api/machines/sign-out` with an HMAC proof of the machine secret (prefix `mux-signout:`), so the cloud
  revokes the machine and closes its tunnel with `4403` instead of listing it as offline. Then it deletes the
  credential. If the cloud cannot be reached, the local sign-out still happens and the machine stays listed (offline)
  until it is removed in the cloud dashboard.
- **Handshake and heartbeat.** The tunnel sends `tunnel.hello` with an HMAC proof of the machine secret. The machine
  counts as online after `tunnel.welcome`. Both sides ping on the welcome's `heartbeatSeconds`, and three missed pongs
  trigger a reconnect.
- **Reconnects** use exponential backoff with jitter (2 seconds doubling to 60). A revoked machine (`4403`), an
  unknown credential (`4401`), or an unsupported protocol version (`4426`) stops reconnecting and shows the reason on
  every surface.
- **Forwarding.** Each relayed request is forwarded to `http://127.0.0.1:<rest.port>` with the local API key and two
  headers:

  | Header | Value | Effect |
  | --- | --- | --- |
  | `X-Mux-Relay` | `cloud` | Marks the request as relayed. The dashboard page renders without the local key. |
  | `X-Mux-Base-Path` | `/m/{machineId}` | The dashboard builds every API and WebSocket URL under this prefix, so they route back through the cloud. |

  Responses stream back as they are produced, so chat streaming works remotely. Requests run concurrently, so a
  slow request never blocks another one or the heartbeat. Relayed WebSockets (`/v1.0/ws`) are bridged frame by
  frame.
- **Parity.** A relayed caller can do everything a local caller can. The one remote-specific switch is
  `cloud.allowRemoteToolApproval` (default `true`). When it is `false`, relayed runs deny tools that would prompt.
- **Status pushes.** The tunnel sends `machine.status` frames (active runs, runs awaiting approval, approval
  requested, run completed) so the cloud machine list and mobile notifications stay current. They carry no
  conversation content.

## 10. Minimal client checklist

1. Connect with the key (header or `?apiKey=`) and wait for `server.connected`.
2. Subscribe to a run, a session, or `all`.
3. Render `assistant_text` deltas. Show `tool_call_proposed` and `tool_call_completed`.
4. On `approval_requested`, prompt the user and send `approve` with `runId`, `toolCallId`, and `decision`.
   Dismiss the prompt on `approval_resolved`.
5. Stop on `run_completed`. For a session subscription, keep the socket open for the next run.
6. Reconnect after a transport failure, and resubscribe.
