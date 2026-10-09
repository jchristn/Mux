---
name: mcp-server-builder
description: >-
  Design and ship production-ready MCP (Model Context Protocol) servers from OpenAPI contracts instead of
  hand-written tool wrappers. Python and TypeScript support, schema validation, safe evolution. Use when exposing an
  existing API as an MCP server, building tool integrations for Claude or Codex or Cursor, or scaffolding an MCP
  project from scratch.
category: engineering
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/mcp-server-builder"
license: MIT
commands:
  - name: mcp-validator
    description: Validate MCP tool manifest files for common contract issues.
    run: scripts/mcp_validator.py
    interpreter: python
  - name: openapi-to-mcp
    description: Generate MCP server scaffold files from an OpenAPI specification.
    run: scripts/openapi_to_mcp.py
    interpreter: python
---

# MCP Server Builder

**Tier:** POWERFUL · **Category:** Engineering · **Domain:** AI / API Integration

## Overview

Use this skill to design and ship production-ready MCP servers from API contracts instead of hand-written one-off tool wrappers. It focuses on fast scaffolding, schema quality, validation, and safe evolution.

The workflow supports both Python and TypeScript MCP implementations and treats OpenAPI as the source of truth.

## Core Capabilities

- Convert OpenAPI paths/operations into MCP tool definitions
- Generate starter server scaffolds (Python or TypeScript)
- Enforce naming, descriptions, and schema consistency
- Validate MCP tool manifests for common production failures
- Apply versioning and backward-compatibility checks
- Separate transport/runtime decisions from tool contract design

## When to Use

- You need to expose an internal/external REST API to an LLM agent
- You are replacing brittle browser automation with typed tools
- You want one MCP server shared across teams and assistants
- You need repeatable quality checks before publishing MCP tools
- You want to bootstrap an MCP server from existing OpenAPI specs

## Key Workflows

### 1. OpenAPI to MCP Scaffold

1. Start from a valid OpenAPI spec.
2. Generate tool manifest + starter server code.
3. Review naming and auth strategy.
4. Add endpoint-specific runtime logic.

```bash
python3 "${SKILL_DIR}/scripts/openapi_to_mcp.py" \
  --input openapi.json \
  --server-name billing-mcp \
  --language python \
  --output-dir ./out \
  --format text
```

Supports stdin as well:

```bash
cat openapi.json | python3 "${SKILL_DIR}/scripts/openapi_to_mcp.py" --server-name billing-mcp --language typescript
```

### 2. Validate MCP Tool Definitions

Run validator before integration tests:

```bash
python3 "${SKILL_DIR}/scripts/mcp_validator.py" --input out/tool_manifest.json --strict --format text
```

Checks include duplicate names, invalid schema shape, missing descriptions, empty required fields, and naming hygiene.

### 3. Runtime Selection

- Choose **Python** for fast iteration and data-heavy backends.
- Choose **TypeScript** for unified JS stacks and tighter frontend/backend contract reuse.
- Keep tool contracts stable even if transport/runtime changes.

### 4. Harden for Production

Key items before publishing:

- Keep secrets in env vars, not tool schemas
- Prefer outbound host allowlists over open proxies
- Use additive-only changes; never rename tool names in-place

Full hardening guidance: [${SKILL_DIR}/references/production-hardening-guide.md](${SKILL_DIR}/references/production-hardening-guide.md).

## Script Interfaces

- `python3 "${SKILL_DIR}/scripts/openapi_to_mcp.py" --help`
  - Reads OpenAPI from stdin or `--input`
  - Produces manifest + server scaffold
  - Emits JSON summary or text report
- `python3 "${SKILL_DIR}/scripts/mcp_validator.py" --help`
  - Validates manifests and optional runtime config
  - Returns non-zero exit in strict mode when errors exist

## Reference Material

- [${SKILL_DIR}/references/production-hardening-guide.md](${SKILL_DIR}/references/production-hardening-guide.md), auth & safety design, versioning strategy, common pitfalls, best practices, architecture decisions, contract quality gates, testing strategy, deployment practices, security controls
- [${SKILL_DIR}/references/openapi-extraction-guide.md](${SKILL_DIR}/references/openapi-extraction-guide.md)
- [${SKILL_DIR}/references/python-server-template.md](${SKILL_DIR}/references/python-server-template.md)
- [${SKILL_DIR}/references/typescript-server-template.md](${SKILL_DIR}/references/typescript-server-template.md)
- [${SKILL_DIR}/references/validation-checklist.md](${SKILL_DIR}/references/validation-checklist.md)
- [README.md](README.md)

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/mcp-server-builder (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->

## Commands

Run the bundled scripts through mux with `run_skill mcp-server-builder <command> [arguments]` (pass `--help` to see a command's options), or call them directly with `python3 "${SKILL_DIR}/scripts/<file>"`. Exit codes pass through: 0 means success, 1 means findings or a failed check, and 2 means invalid arguments. With `MUX_SKILL_DRY_RUN=1`, mux prints the command instead of running it.

| Command | Script | What it does |
|---|---|---|
| `mcp-validator` | `scripts/mcp_validator.py` | Validate MCP tool manifest files for common contract issues. |
| `openapi-to-mcp` | `scripts/openapi_to_mcp.py` | Generate MCP server scaffold files from an OpenAPI specification. |

`openapi-to-mcp` reads JSON specs directly; YAML specs need PyYAML (`pip install pyyaml`).
