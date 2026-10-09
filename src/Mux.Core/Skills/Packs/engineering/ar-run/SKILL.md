---
name: ar-run
description: >-
  Run a single experiment iteration. Edit the target file, evaluate, keep or discard. Use when the user runs /ar:run
  or asks for one manual autoresearch iteration.
category: research
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/autoresearch-agent/skills/run"
license: MIT
commands:
  - name: run-experiment
    description: Run one experiment iteration: evaluate, parse the metric, keep or discard, and roll back on failure.
    run: scripts/run_experiment.py
    interpreter: python
    timeoutMs: 1800000
  - name: setup-experiment
    description: Initialize an experiment (domain, target, evaluator, git branch) or list the built-in evaluators.
    run: scripts/setup_experiment.py
    interpreter: python
    timeoutMs: 600000
---

# /ar:run, Single Experiment Iteration

Run exactly ONE experiment iteration: review history, decide a change, edit, commit, evaluate.

## Usage

```
/ar:run engineering/api-speed              # Run one iteration
/ar:run                                     # List experiments, let user pick
```

## What It Does

### Step 1: Resolve experiment

If no experiment specified, run `python3 "${SKILL_DIR}/scripts/setup_experiment.py" --list` and ask the user to pick.

### Step 2: Load context

```bash
# Read experiment config
cat .autoresearch/{domain}/{name}/config.cfg

# Read strategy and constraints
cat .autoresearch/{domain}/{name}/program.md

# Read experiment history
cat .autoresearch/{domain}/{name}/results.tsv

# Checkout the experiment branch
git checkout autoresearch/{domain}/{name}
```

### Step 3: Decide what to try

Review results.tsv:
- What changes were kept? What pattern do they share?
- What was discarded? Avoid repeating those approaches.
- What crashed? Understand why.
- How many runs so far? (Escalate strategy accordingly)

**Strategy escalation:**
- Runs 1-5: Low-hanging fruit (obvious improvements)
- Runs 6-15: Systematic exploration (vary one parameter)
- Runs 16-30: Structural changes (algorithm swaps)
- Runs 30+: Radical experiments (completely different approaches)

### Step 4: Make ONE change

Edit only the target file specified in config.cfg. Change one thing. Keep it simple.

### Step 5: Commit and evaluate

```bash
git add {target}
git commit -m "experiment: {short description of what changed}"

python3 "${SKILL_DIR}/scripts/run_experiment.py" \
  --experiment {domain}/{name} --single
```

### Step 6: Report result

Read the script output. Tell the user:
- **KEEP**: "Improvement! {metric}: {value} ({delta} from previous best)"
- **DISCARD**: "No improvement. {metric}: {value} vs best {best}. Reverted."
- **CRASH**: "Evaluation failed: {reason}. Reverted."

### Step 7: Self-improvement check

After every 10th experiment (check results.tsv line count), update the Strategy section of program.md with patterns learned.

## Rules

- ONE change per iteration. Don't change 5 things at once.
- NEVER modify the evaluator (evaluate.py). It's ground truth.
- Simplicity wins. Equal performance with simpler code is an improvement.
- No new dependencies.

## Commands

The bundled scripts are skill commands. Run them with `run_skill`, for example `run_skill ar-run run-experiment --help`; the arguments after the command go straight to the script. They also run directly, as `python3 "${SKILL_DIR}/scripts/run_experiment.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `run-experiment` | `scripts/run_experiment.py` | Run one experiment iteration: evaluate, parse the metric, keep or discard, and roll back on failure. |
| `setup-experiment` | `scripts/setup_experiment.py` | Initialize an experiment (domain, target, evaluator, git branch) or list the built-in evaluators. |

Exit codes pass straight through from the scripts and match mux's convention: 0 means success, 1 means the script reported findings or failed, and 2 means invalid input or a missing dependency. A dry run (`MUX_SKILL_DRY_RUN=1`) prints the command instead of running it.

`run-experiment` runs the experiment command you configured and can take a long time; its timeout is 30 minutes. Built-in evaluators ship in `${SKILL_DIR}/evaluators/`; the LLM judges call `mux -p` by default (edit `CLI_TOOL` to use another CLI).

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/autoresearch-agent/skills/run (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
