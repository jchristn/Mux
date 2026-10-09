---
name: runbook-generator
description: >-
  Generate operational runbooks from a service name, deployment, incident response, maintenance, and rollback
  workflows. Templated structure customizable per environment. Use when documenting on-call procedures for a new
  service, standardizing incident response across teams, or producing runbooks before launching to production.
category: devops
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/runbook-generator"
license: MIT
---

# Runbook Generator

**Tier:** POWERFUL  
**Category:** Engineering  
**Domain:** DevOps / Site Reliability Engineering

---

## Overview

Generate operational runbooks quickly from a service name, then customize for deployment, incident response, maintenance, and rollback workflows.

## Core Capabilities

- Runbook skeleton generation from a CLI
- Standard sections for start/stop/health/rollback
- Structured escalation and incident handling placeholders
- Reference templates for deployment and incident playbooks

---

## When to Use

- A service has no runbook and needs a baseline immediately
- Existing runbooks are inconsistent across teams
- On-call onboarding requires standardized operations docs
- You need repeatable runbook scaffolding for new services

---

## Quick Start

```bash
# Print runbook to stdout
python3 "${SKILL_DIR}/scripts/runbook_generator.py" payments-api

# Write runbook file
python3 "${SKILL_DIR}/scripts/runbook_generator.py" payments-api --owner platform --output docs/runbooks/payments-api.md
```

---

## Recommended Workflow

1. Generate the initial skeleton with `${SKILL_DIR}/scripts/runbook_generator.py`.
2. Fill in service-specific commands and URLs.
3. Add verification checks and rollback triggers.
4. Dry-run in staging.
5. Store runbook in version control near service code.

---

## Reference Docs

- `${SKILL_DIR}/references/runbook-templates.md`

---

## Common Pitfalls

- Missing rollback triggers or rollback commands
- Steps without expected output checks
- Stale ownership/escalation contacts
- Runbooks never tested outside of incidents

## Best Practices

1. Keep every command copy-pasteable.
2. Include health checks after every critical step.
3. Validate runbooks on a fixed review cadence.
4. Update runbook content after incidents and postmortems.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/runbook-generator (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
