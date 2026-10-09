---
name: ar-status
description: >-
  Show experiment dashboard with results, active loops, and progress. Use when the user runs /ar:ar-status or asks
  how an autoresearch experiment is going.
category: research
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/autoresearch-agent/skills/ar-status"
license: MIT
commands:
  - name: log-results
    description: Show experiment results as a table, CSV, or Markdown, for one experiment, a domain, or a dashboard.
    run: scripts/log_results.py
    interpreter: python
---

# /ar:ar-status, Experiment Dashboard

Show experiment results, active loops, and progress across all experiments.

## Usage

```
/ar:ar-status                                  # Full dashboard
/ar:ar-status engineering/api-speed            # Single experiment detail
/ar:ar-status --domain engineering             # All experiments in a domain
/ar:ar-status --format markdown                # Export as markdown
/ar:ar-status --format csv --output results.csv  # Export as CSV
```

## What It Does

### Single experiment

```bash
python3 "${SKILL_DIR}/scripts/log_results.py" --experiment {domain}/{name}
```

Also check for active loop:
```bash
cat .autoresearch/{domain}/{name}/loop.json 2>/dev/null
```

If loop.json exists, show:
```
Active loop: every {interval} (cron ID: {id}, started: {date})
```

### Domain view

```bash
python3 "${SKILL_DIR}/scripts/log_results.py" --domain {domain}
```

### Full dashboard

```bash
python3 "${SKILL_DIR}/scripts/log_results.py" --dashboard
```

For each experiment, also check for loop.json and show loop status.

### Export

```bash
# CSV
python3 "${SKILL_DIR}/scripts/log_results.py" --dashboard --format csv --output {file}

# Markdown
python3 "${SKILL_DIR}/scripts/log_results.py" --dashboard --format markdown --output {file}
```

## Output Example

```
DOMAIN          EXPERIMENT          RUNS  KEPT  BEST         CHANGE    STATUS   LOOP
engineering     api-speed            47    14   185ms        -76.9%    active   every 1h
engineering     bundle-size          23     8   412KB        -58.3%    paused, marketing       medium-ctr           31    11   8.4/10       +68.0%    active   daily
prompts         support-tone         15     6   82/100       +46.4%    done, ```

## Commands

The bundled scripts are skill commands. Run them with `run_skill`, for example `run_skill ar-status log-results --help`; the arguments after the command go straight to the script. They also run directly, as `python3 "${SKILL_DIR}/scripts/log_results.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `log-results` | `scripts/log_results.py` | Show experiment results as a table, CSV, or Markdown, for one experiment, a domain, or a dashboard. |

Exit codes pass straight through from the scripts and match mux's convention: 0 means success, 1 means the script reported findings or failed, and 2 means invalid input or a missing dependency. A dry run (`MUX_SKILL_DRY_RUN=1`) prints the command instead of running it.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/autoresearch-agent/skills/ar-status (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
