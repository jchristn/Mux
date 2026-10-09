---
name: ar-resume
description: >-
  Resume a paused experiment. Checkout the experiment branch, read results history, continue iterating. Use when the
  user runs /ar:ar-resume or asks to pick up a previously started autoresearch experiment.
category: research
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/autoresearch-agent/skills/ar-resume"
license: MIT
commands:
  - name: setup-experiment
    description: Initialize an experiment (domain, target, evaluator, git branch) or list the built-in evaluators.
    run: scripts/setup_experiment.py
    interpreter: python
    timeoutMs: 600000
---

# /ar:ar-resume, Resume Experiment

Resume a paused or context-limited experiment. Reads all history and continues where you left off.

## Usage

```
/ar:ar-resume                                  # List experiments, let user pick
/ar:ar-resume engineering/api-speed            # Resume specific experiment
```

## What It Does

### Step 1: List experiments if needed

If no experiment specified:

```bash
python3 "${SKILL_DIR}/scripts/setup_experiment.py" --list
```

Show status for each (active/paused/done based on results.tsv age). Let user pick.

### Step 2: Load full context

```bash
# Checkout the experiment branch
git checkout autoresearch/{domain}/{name}

# Read config
cat .autoresearch/{domain}/{name}/config.cfg

# Read strategy
cat .autoresearch/{domain}/{name}/program.md

# Read full results history
cat .autoresearch/{domain}/{name}/results.tsv

# Read recent git log for the branch
git log --oneline -20
```

### Step 3: Report current state

Summarize for the user:

```
Resuming: engineering/api-speed
  Target: src/api/search.py
  Metric: p50_ms (lower is better)
  Experiments: 23 total, 8 kept, 12 discarded, 3 crashed
  Best: 185ms (-42% from baseline of 320ms)
  Last experiment: "added response caching" → KEEP (185ms)

  Recent patterns:
  - Caching changes: 3 kept, 1 discarded (consistently helpful)
  - Algorithm changes: 2 discarded, 1 crashed (high risk, low reward so far)
  - I/O optimization: 2 kept (promising direction)
```

### Step 4: Ask next action

```
How would you like to continue?
  1. Single iteration (/ar:run), I'll make one change and evaluate
  2. Start a loop (/ar:loop), Autonomous with scheduled interval
  3. Just show me the results, I'll review and decide
```

If the user picks loop, hand off to `/ar:loop` with the experiment pre-selected.
If single, hand off to `/ar:run`.

## Commands

The bundled scripts are skill commands. Run them with `run_skill`, for example `run_skill ar-resume setup-experiment --help`; the arguments after the command go straight to the script. They also run directly, as `python3 "${SKILL_DIR}/scripts/setup_experiment.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `setup-experiment` | `scripts/setup_experiment.py` | Initialize an experiment (domain, target, evaluator, git branch) or list the built-in evaluators. |

Exit codes pass straight through from the scripts and match mux's convention: 0 means success, 1 means the script reported findings or failed, and 2 means invalid input or a missing dependency. A dry run (`MUX_SKILL_DRY_RUN=1`) prints the command instead of running it.

Built-in evaluators ship in `${SKILL_DIR}/evaluators/`; the LLM judges call `mux -p` by default (edit `CLI_TOOL` to use another CLI).

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/autoresearch-agent/skills/ar-resume (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
