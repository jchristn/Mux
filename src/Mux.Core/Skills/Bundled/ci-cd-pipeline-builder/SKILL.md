---
name: ci-cd-pipeline-builder
description: >-
  Generate pragmatic CI/CD pipelines from detected project stack signals, fast baseline generation, repeatable
  checks, environment-aware deployment stages. Use when setting up CI for a new project, refactoring existing
  pipelines, or standardizing deployment workflows across multiple repos.
category: devops
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/ci-cd-pipeline-builder"
license: MIT
appliesTo:
  - "*.sln"
  - "*.slnx"
  - "**/*.csproj"
  - "package.json"
  - "pyproject.toml"
  - "requirements*.txt"
  - "go.mod"
  - "Cargo.toml"
  - "pom.xml"
  - "build.gradle"
  - "build.gradle.kts"
  - "CMakeLists.txt"
---

# CI/CD Pipeline Builder

**Tier:** POWERFUL  
**Category:** Engineering  
**Domain:** DevOps / Automation

## Overview

Use this skill to generate pragmatic CI/CD pipelines from detected project stack signals, not guesswork. It focuses on fast baseline generation, repeatable checks, and environment-aware deployment stages.

## Core Capabilities

- Detect language/runtime/tooling from repository files
- Recommend CI stages (`lint`, `test`, `build`, `deploy`)
- Generate GitHub Actions or GitLab CI starter pipelines
- Include caching and matrix strategy based on detected stack
- Emit machine-readable detection output for automation
- Keep pipeline logic aligned with project lockfiles and build commands

## When to Use

- Bootstrapping CI for a new repository
- Replacing brittle copied pipeline files
- Migrating between GitHub Actions and GitLab CI
- Auditing whether pipeline steps match actual stack
- Creating a reproducible baseline before custom hardening

## Key Workflows

### 1. Detect Stack

```bash
python3 "${SKILL_DIR}/scripts/stack_detector.py" --repo . --format text
python3 "${SKILL_DIR}/scripts/stack_detector.py" --repo . --format json > detected-stack.json
```

Supports input via stdin or `--input` file for offline analysis payloads.

### 2. Generate Pipeline From Detection

```bash
python3 "${SKILL_DIR}/scripts/pipeline_generator.py" \
  --input detected-stack.json \
  --platform github \
  --output .github/workflows/ci.yml \
  --format text
```

Or end-to-end from repo directly:

```bash
python3 "${SKILL_DIR}/scripts/pipeline_generator.py" --repo . --platform gitlab --output .gitlab-ci.yml
```

### 3. Validate Before Merge

1. Confirm commands exist in project (`test`, `lint`, `build`).
2. Run generated pipeline locally where possible.
3. Ensure required secrets/env vars are documented.
4. Keep deploy jobs gated by protected branches/environments.

### 4. Add Deployment Stages Safely

- Start with CI-only (`lint/test/build`).
- Add staging deploy with explicit environment context.
- Add production deploy with manual gate/approval.
- Keep rollout/rollback commands explicit and auditable.

## Script Interfaces

- `python3 "${SKILL_DIR}/scripts/stack_detector.py" --help`
  - Detects stack signals from repository files
  - Reads optional JSON input from stdin/`--input`
- `python3 "${SKILL_DIR}/scripts/pipeline_generator.py" --help`
  - Generates GitHub/GitLab YAML from detection payload
  - Writes to stdout or `--output`

## References

- [${SKILL_DIR}/references/pipeline-design-notes.md](${SKILL_DIR}/references/pipeline-design-notes.md), common pitfalls, best practices, detection heuristics, generation strategy, platform decision notes, pre-merge validation checklist, and scaling guidance
- [${SKILL_DIR}/references/github-actions-templates.md](${SKILL_DIR}/references/github-actions-templates.md)
- [${SKILL_DIR}/references/gitlab-ci-templates.md](${SKILL_DIR}/references/gitlab-ci-templates.md)
- [${SKILL_DIR}/references/deployment-gates.md](${SKILL_DIR}/references/deployment-gates.md)
- [README.md](README.md)

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/ci-cd-pipeline-builder (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
