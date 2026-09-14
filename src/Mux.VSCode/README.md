# mux for VS Code

Bring the [mux](https://github.com/jchristn/Mux) coding agent into the editor. Ask about the file you are
reading, fix the diagnostic under the cursor, review what the agent changed as a native diff, and pick a
conversation back up tomorrow — against the backend and model you chose.

The extension is a thin client. It talks to a local `mux serve` over its REST and streaming API, so the agent
loop, tools, approvals, and session store stay in mux itself. Your editor sessions are the same ones the mux
terminal UI and desktop app show, because they read the same store. A conversation you start here resumes
there, and one you start there opens here.

> Alpha. Tracks the mux `contractVersion` API and may change alongside it.

## Requirements

- The `mux` CLI installed and on `PATH` (or set `mux.server.path`). See the mux install guide.
- VS Code 1.90 or newer.

## What it does

- **Chat panel** — stream a run with tool activity, and approve or deny a mutating tool without leaving the
  editor.
- **Inline commands** — Explain selection, Fix this problem, Generate tests, Refactor selection, Write commit
  message, Summarize diff, Review current file. The first three are also code actions.
- **Editor context** — attach the active file, selection, diagnostics, open tabs, or the working-tree diff to
  a prompt, and see exactly what was sent.
- **Sessions** — list, resume, rename, duplicate, export, and delete the sessions shared across every mux
  surface.
- **Endpoint picker** — choose the endpoint runs go against from the status bar.

## Getting started

Install the extension, open a folder, and open the mux view in the activity bar. On the first message the
extension connects to a running `mux serve` or starts one bound to loopback with a generated key. Type a
question, or run an inline command from the editor context menu.

The full guide lives in [docs/VSCODE.md](https://github.com/jchristn/Mux/blob/main/docs/VSCODE.md).

## Settings

- `mux.server.autoStart` — start a local server when none is reachable (default on).
- `mux.server.port` / `mux.server.path` — where to reach or how to launch `mux serve`.
- `mux.defaultEndpoint` — the endpoint runs use; empty follows the server's default.
- `mux.approvalPosture` — prompt in the editor for mutating tools, or auto-safe.
- `mux.context.sources` — which context attaches to a run by default.
- `mux.locale` — display-language override; empty follows the editor language.

## License

MIT.
