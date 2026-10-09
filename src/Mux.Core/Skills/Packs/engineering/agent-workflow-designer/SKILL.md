---
name: agent-workflow-designer
description: >-
  Design production-grade multi-agent workflows with clear pattern choice (sequential, parallel, hierarchical),
  handoff contracts, failure handling, and cost/context controls. Use when architecting a multi-step agent pipeline,
  choosing between single-agent vs multi-agent approaches, or refactoring an LLM workflow that suffers from context
  bloat or unreliable handoffs.
category: workflow
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/agent-workflow-designer"
license: MIT
commands:
  - name: workflow-scaffolder
    description: Generate a workflow skeleton config from a pattern.
    run: scripts/workflow_scaffolder.py
    interpreter: python
---

# Agent Workflow Designer

**Tier:** POWERFUL  
**Category:** Engineering  
**Domain:** Multi-Agent Systems / AI Orchestration

---

## Overview

Design production-grade multi-agent workflows with clear pattern choice, handoff contracts, failure handling, and cost/context controls.

## Core Capabilities

- Workflow pattern selection for multi-step agent systems
- Skeleton config generation for fast workflow bootstrapping
- Context and cost discipline across long-running flows
- Error recovery and retry strategy scaffolding
- Documentation pointers for operational pattern tradeoffs

---

## When to Use

- A single prompt is insufficient for task complexity
- You need specialist agents with explicit boundaries
- You want deterministic workflow structure before implementation
- You need validation loops for quality or safety gates

---

## Quick Start

```bash
# Generate a sequential workflow skeleton
python3 "${SKILL_DIR}/scripts/workflow_scaffolder.py" sequential --name content-pipeline

# Generate an orchestrator workflow and save it
python3 "${SKILL_DIR}/scripts/workflow_scaffolder.py" orchestrator --name incident-triage --output workflows/incident-triage.json
```

---

## Pattern Map

- `sequential`: strict step-by-step dependency chain
- `parallel`: fan-out/fan-in for independent subtasks
- `router`: dispatch by intent/type with fallback
- `orchestrator`: planner coordinates specialists with dependencies
- `evaluator`: generator + quality gate loop

Detailed templates: `${SKILL_DIR}/references/workflow-patterns.md`

---

## Recommended Workflow

1. Select pattern based on dependency shape and risk profile.
2. Scaffold config via `${SKILL_DIR}/scripts/workflow_scaffolder.py`.
3. Define handoff contract fields for every edge.
4. Add retry/timeouts and output validation gates.
5. Dry-run with small context budgets before scaling.

---

## Common Pitfalls

- Over-orchestrating tasks solvable by one well-structured prompt
- Missing timeout/retry policies for external-model calls
- Passing full upstream context instead of targeted artifacts
- Ignoring per-step cost accumulation

## Best Practices

1. Start with the smallest pattern that can satisfy requirements.
2. Keep handoff payloads explicit and bounded.
3. Validate intermediate outputs before fan-in synthesis.
4. Enforce budget and timeout limits in every step.

## Commands

The bundled scripts are skill commands. Run them with `run_skill`, for example `run_skill agent-workflow-designer workflow-scaffolder --help`; the arguments after the command go straight to the script. They also run directly, as `python3 "${SKILL_DIR}/scripts/workflow_scaffolder.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `workflow-scaffolder` | `scripts/workflow_scaffolder.py` | Generate a workflow skeleton config from a pattern. |

Exit codes pass straight through from the scripts and match mux's convention: 0 means success, 1 means the script reported findings or failed, and 2 means invalid input or a missing dependency. A dry run (`MUX_SKILL_DRY_RUN=1`) prints the command instead of running it.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/agent-workflow-designer (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
