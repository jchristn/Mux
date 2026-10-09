# MCP Server Builder

Generate and validate MCP servers from OpenAPI contracts with production-focused tooling. This skill helps teams bootstrap fast and enforce schema quality before shipping.

## Quick Start

```bash
# Generate scaffold from OpenAPI
python3 "${SKILL_DIR}/scripts/openapi_to_mcp.py" \
  --input openapi.json \
  --server-name my-mcp \
  --language python \
  --output-dir ./generated \
  --format text

# Validate generated manifest
python3 "${SKILL_DIR}/scripts/mcp_validator.py" --input generated/tool_manifest.json --strict --format text
```

## Included Tools

- `${SKILL_DIR}/scripts/openapi_to_mcp.py`: OpenAPI -> `tool_manifest.json` + starter server scaffold
- `${SKILL_DIR}/scripts/mcp_validator.py`: structural and quality validation for MCP tool definitions

## References

- `${SKILL_DIR}/references/openapi-extraction-guide.md`
- `${SKILL_DIR}/references/python-server-template.md`
- `${SKILL_DIR}/references/typescript-server-template.md`
- `${SKILL_DIR}/references/validation-checklist.md`

## Installation

### mux

```bash
cp -R engineering/mcp-server-builder ~/.mux/skills/mcp-server-builder
```

### OpenAI Codex

```bash
cp -R engineering/mcp-server-builder ~/.codex/skills/mcp-server-builder
```

### OpenClaw

```bash
cp -R engineering/mcp-server-builder ~/.openclaw/skills/mcp-server-builder
```
