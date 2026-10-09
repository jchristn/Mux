# CI/CD Pipeline Builder

Detects your repository stack and generates practical CI pipeline templates for GitHub Actions and GitLab CI. Designed as a fast baseline you can extend with deployment controls.

## Quick Start

```bash
# Detect stack
python3 "${SKILL_DIR}/scripts/stack_detector.py" --repo . --format json > stack.json

# Generate GitHub Actions workflow
python3 "${SKILL_DIR}/scripts/pipeline_generator.py" \
  --input stack.json \
  --platform github \
  --output .github/workflows/ci.yml \
  --format text
```

## Included Tools

- `${SKILL_DIR}/scripts/stack_detector.py`: repository signal detection with JSON/text output
- `${SKILL_DIR}/scripts/pipeline_generator.py`: generate GitHub/GitLab CI YAML from detection payload

## References

- `${SKILL_DIR}/references/github-actions-templates.md`
- `${SKILL_DIR}/references/gitlab-ci-templates.md`
- `${SKILL_DIR}/references/deployment-gates.md`

## Installation

### mux

```bash
cp -R engineering/ci-cd-pipeline-builder ~/.mux/skills/ci-cd-pipeline-builder
```

### OpenAI Codex

```bash
cp -R engineering/ci-cd-pipeline-builder ~/.codex/skills/ci-cd-pipeline-builder
```

### OpenClaw

```bash
cp -R engineering/ci-cd-pipeline-builder ~/.openclaw/skills/ci-cd-pipeline-builder
```
