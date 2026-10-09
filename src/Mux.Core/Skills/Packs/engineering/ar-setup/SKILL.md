---
name: ar-setup
description: >-
  Set up a new autoresearch experiment interactively. Collects domain, target file, eval command, metric, direction,
  and evaluator. Use when the user runs /ar:setup or asks to start optimizing a file with the autoresearch loop.
category: research
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/autoresearch-agent/skills/setup"
license: MIT
commands:
  - name: setup-experiment
    description: Initialize an experiment (domain, target, evaluator, git branch) or list the built-in evaluators.
    run: scripts/setup_experiment.py
    interpreter: python
    timeoutMs: 600000
---

# /ar:setup, Create New Experiment

Set up a new autoresearch experiment with all required configuration.

## Usage

```
/ar:setup                                    # Interactive mode
/ar:setup engineering api-speed src/api.py "pytest bench.py" p50_ms lower
/ar:setup --list                             # Show existing experiments
/ar:setup --list-evaluators                  # Show available evaluators
```

## What It Does

### If arguments provided

Pass them directly to the setup script:

```bash
python3 "${SKILL_DIR}/scripts/setup_experiment.py" \
  --domain {domain} --name {name} \
  --target {target} --eval "{eval_cmd}" \
  --metric {metric} --direction {direction} \
  [--evaluator {evaluator}] [--scope {scope}]
```

### If no arguments (interactive mode)

Collect each parameter one at a time:

1. **Domain**, Ask: "What domain? (engineering, marketing, content, prompts, custom)"
2. **Name**, Ask: "Experiment name? (e.g., api-speed, blog-titles)"
3. **Target file**, Ask: "Which file to optimize?" Verify it exists.
4. **Eval command**, Ask: "How to measure it? (e.g., pytest bench.py, python evaluate.py)"
5. **Metric**, Ask: "What metric does the eval output? (e.g., p50_ms, ctr_score)"
6. **Direction**, Ask: "Is lower or higher better?"
7. **Evaluator** (optional), Show built-in evaluators. Ask: "Use a built-in evaluator, or your own?"
8. **Scope**, Ask: "Store in project (.autoresearch/) or user (~/.autoresearch/)?"

Then run `setup_experiment.py` with the collected parameters.

### Listing

```bash
# Show existing experiments
python3 "${SKILL_DIR}/scripts/setup_experiment.py" --list

# Show available evaluators
python3 "${SKILL_DIR}/scripts/setup_experiment.py" --list-evaluators
```

## Built-in Evaluators

| Name | Metric | Use Case |
|------|--------|----------|
| `benchmark_speed` | `p50_ms` (lower) | Function/API execution time |
| `benchmark_size` | `size_bytes` (lower) | File, bundle, Docker image size |
| `test_pass_rate` | `pass_rate` (higher) | Test suite pass percentage |
| `build_speed` | `build_seconds` (lower) | Build/compile/Docker build time |
| `memory_usage` | `peak_mb` (lower) | Peak memory during execution |
| `llm_judge_content` | `ctr_score` (higher) | Headlines, titles, descriptions |
| `llm_judge_prompt` | `quality_score` (higher) | System prompts, agent instructions |
| `llm_judge_copy` | `engagement_score` (higher) | Social posts, ad copy, emails |

## After Setup

Report to the user:
- Experiment path and branch name
- Whether the eval command worked and the baseline metric
- Suggest: "Run `/ar:run {domain}/{name}` to start iterating, or `/ar:loop {domain}/{name}` for autonomous mode."

## Commands

The bundled scripts are skill commands. Run them with `run_skill`, for example `run_skill ar-setup setup-experiment --help`; the arguments after the command go straight to the script. They also run directly, as `python3 "${SKILL_DIR}/scripts/setup_experiment.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `setup-experiment` | `scripts/setup_experiment.py` | Initialize an experiment (domain, target, evaluator, git branch) or list the built-in evaluators. |

Exit codes pass straight through from the scripts and match mux's convention: 0 means success, 1 means the script reported findings or failed, and 2 means invalid input or a missing dependency. A dry run (`MUX_SKILL_DRY_RUN=1`) prints the command instead of running it.

Built-in evaluators ship in `${SKILL_DIR}/evaluators/` (`setup-experiment --list-evaluators` shows them); the LLM judges call `mux -p` by default (edit `CLI_TOOL` to use another CLI).

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/autoresearch-agent/skills/setup (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
