# Security Policy

## Supported versions

mux is beta software under active development. Security fixes land on the latest release line only.

| Version | Supported |
| ------- | --------- |
| Latest release (see [CHANGELOG.md](CHANGELOG.md)) | ✅ |
| Older releases | ❌ |

## Reporting a vulnerability

**Please do not open a public issue for a security vulnerability.**

Report vulnerabilities privately through
[GitHub Security Advisories](https://github.com/jchristn/Mux/security/advisories/new)
("Report a vulnerability" on the repository's Security tab).

Include:

- A description of the vulnerability and its impact.
- The mux version and surface affected (CLI, `mux serve`, desktop app, VS Code extension, SDKs).
- Steps to reproduce or a proof of concept.
- Any suggested mitigation, if you have one.

You can expect an acknowledgement within a few days. If the report is accepted, a fix is developed
privately, released as soon as practical, and credited to you in the release notes unless you prefer
to remain anonymous.

## Security model worth knowing before reporting

mux is a local-first agent that executes tools on the machine it runs on, by design:

- `mux serve` binds to **loopback only** and requires an access token; the web dashboard is
  read-only by default, and mutating tools require per-call browser approval even with
  `--allow-tools`.
- Tool execution in the CLI goes through the approval prompt unless the user opts into
  auto-approval for a session.
- SDKs (`sdk/python`, `sdk/typescript`) spawn the local `mux` binary; they inherit the CLI's
  approval behavior.

Issues are most interesting when they cross these boundaries: for example, a remote party able to
reach the API without the token, approvals being bypassed, a skill or MCP server escaping its
sandbox expectations, or credentials/tokens leaking into logs, telemetry, or published artifacts.

Ordinary "the agent ran a command the user approved" behavior is a feature, not a vulnerability.

## Handling of credentials

API keys are stored in the per-user configuration directory and are never written to telemetry,
logs, or test fixtures. If you find a code path that violates this, it is a security issue — please
report it as described above.
