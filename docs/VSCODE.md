# mux for VS Code

The VS Code extension makes mux a surface in the editor rather than a separate window you alt-tab to. It runs
your prompts through a local `mux serve`, so the agent, its tools, its approval policy, and its session store
are the same ones the terminal UI and desktop app use. A conversation started at the terminal shows up here;
one started here shows up there. This guide covers installing it, the first run, the commands, and what to do
when something does not connect.

## Install and first run

Install the extension from the Marketplace or Open VSX, then open a folder — mux runs against a workspace, so
an empty window has nowhere to work. Open the mux view from the activity bar and send a message. On that first
message the extension looks for a `mux serve` it can reach and authenticate against; finding none, it starts
one bound to `127.0.0.1` with a key it generates and stores in VS Code's secret storage. The `mux` CLI must be
installed and on `PATH`, or `mux.server.path` must point at it.

Runs execute in the workspace root. When the agent writes a file, it writes into your repo, because the
extension passes the workspace directory to the server and the server runs the run's tools there.

## The chat panel

The panel streams a run as it happens: assistant text arrives token by token, tool calls show up as cards that
move from running to succeeded or failed, and the turn's stats land when it finishes. Replies stream as plain
text for responsiveness and render as Markdown — headings, code blocks, lists, links — once the turn
completes. Stop a run with the
button that replaces Send while a turn is active, or run **mux: Stop the current run** from the command
palette. Stopping now cancels the run **server-side** — it POSTs to `/v1.0/api/runs/{runId}/cancel` so the
agent loop and any in-flight tool actually stop, not just the editor's view of the stream — and the incomplete
turn is dropped so the conversation stays clean.

If you start `mux serve --allow-tools` (or let the extension start the server, which passes that flag), a tool
that would change files prompts you in the panel — approve once, approve for the rest of the session, or deny.
Without that flag the server denies mutating tools outright, and a run can read but not write.

**Mirror a live run.** Run **mux: Mirror a session's live run** from the command palette and pick a session:
the panel then live-tails that session's run read-only over the WebSocket bridge. Use it to watch a run
started on another surface — the desktop app (with its embedded server on), the web dashboard, or a
`mux serve` — as it happens. Stop mirroring with the Stop button or by starting a new conversation.

## Inline commands

Some work is faster as a command than as a typed prompt. These appear in the command palette under **mux**, and
the selection- and diagnostic-anchored ones also appear as code actions:

- **Explain selection** and **Refactor selection** act on the current selection.
- **Fix this problem** acts on the diagnostic at the cursor.
- **Generate tests** uses the selection, or the whole file when nothing is selected.
- **Write commit message** describes your staged diff.
- **Summarize diff** and **Review current file** speak for themselves.

Each command attaches the right context automatically — the diagnostic for a fix, the staged diff for a commit
message — and streams its answer in the panel.

## Context

A prompt carries whatever editor context you have enabled in `mux.context.sources`: the active file, the
selection, diagnostics, open tabs, and the working-tree diff. A large active file is no longer hard-sliced —
the extension sends it to `mux serve` to be turned into a structural map (or summary) with line ranges and
inlines that, so the model can read exact ranges rather than losing the tail; if the server is unreachable it
falls back to truncation. The panel tells you when an attachment was truncated to fit, and when a requested
source could not be provided — terminal scrollback, for one, which the stable VS Code API does not expose.
Nothing is dropped silently.

## Sessions

The **Sessions** view lists the shared mux session store, newest first. Resume opens a conversation into the
panel with its transcript intact; rename, duplicate, export (Markdown or HTML), and delete are on the row's
context menu. Because this is the same store the terminal UI and desktop app read, a session you started
anywhere is here, and a session you start here is there.

## Managing mux from the editor

The **Manage** view in the mux side bar gives you the visibility and control the terminal UI and desktop app
have, without leaving the editor. At the top it shows whether the extension is connected, to which server,
and at what version and API contract; clicking it (or the status-bar item) reconnects. Below that, each
section reads the server's live configuration:

- **Endpoints** — add, edit, delete, and set the default from a form (adapter, base URL, model, API key,
  token/context limits, per-endpoint tool auto-approval). The API key field is blank on edit and leaving it
  blank keeps the stored key.
- **MCP Servers** — add, edit, and delete stdio or HTTP servers.
- **Prompts** — edit your prompt profiles (system, tools-disabled, and compaction prompts) and set which one
  is active, and browse the operational prompt catalog grouped by kind — edit or reset any entry. Subagent
  personas are listed read-through and deep-link to the subagent editor.
- **Subagents** — see the configured subagents and their descriptions.
- **Skills** — see every discovered skill and toggle it on or off.
- **Settings** — opens an editor over the server's settings (approval policy, iteration and concurrency
  limits, compaction, task planning, and more).
- **Usage** — a quick summary of calls, tokens, cost, errors, and latency for the last day.

Everything here changes the same configuration the CLI, desktop app, and dashboard read, because it goes
through the same local server — so a change you make in the editor shows up everywhere.

## Settings

- `mux.server.autoStart` — start a local server when none is reachable. Turn it off to require your own.
- `mux.server.port` — the port to reach or bind. `0` uses the extension default, `8710`.
- `mux.server.path` — the `mux` executable name or full path.
- `mux.defaultEndpoint` — the endpoint runs use; empty follows the server's default. Change it from the status
  bar.
- `mux.approvalPosture` — prompt for mutating tools, or auto-safe.
- `mux.context.sources` — the context attached by default.
- `mux.locale` — a display-language override; empty follows the editor language.

## Status, help, and About

The status-bar item and the top of the **Manage** view show whether the extension is connected. When it is
not, clicking either opens a help dialog that explains the likely cause and offers to retry the connection,
start `mux serve` in a terminal for you, or open the server settings — so a failed connection tells you what
to do rather than just failing. When it is connected, the same click opens **About**: the logo, the extension
and server versions, links to mux on GitHub and the docs, and a button to open the connected server's
dashboard, which is the fuller configuration, monitoring, and usage surface in the browser.

## Troubleshooting

Failures fall into a few classes. When the panel reports it cannot reach a server and auto-start is off, either
start `mux serve --allow-tools` yourself or turn `mux.server.autoStart` back on. When it reports the server is
too old or the extension is too old, the two disagree on the API contract version — update whichever is behind.
When a run says no endpoint is configured, add one in mux (the terminal UI, the desktop app, or the dashboard
all edit the same config) and pick it from the status bar. The **mux** output channel records what the
extension did, including why a connection was refused, and is the first place to look when the message on
screen is not enough.
