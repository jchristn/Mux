---
name: hub-eval
description: >-
  Evaluate and rank agent results by metric or LLM judge for an AgentHub session. Use when the user runs /hub:eval or
  asks to score, compare, or pick a winner among completed AgentHub agents.
category: workflow
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/agenthub/skills/eval"
license: MIT
commands:
  - name: result-ranker
    description: Rank AgentHub agent results.
    run: scripts/result_ranker.py
    interpreter: python
    timeoutMs: 600000
  - name: session-manager
    description: AgentHub session state machine and lifecycle manager.
    run: scripts/session_manager.py
    interpreter: python
    timeoutMs: 600000
---

# /hub:eval, Evaluate Agent Results

Rank all agent results for a session. Supports metric-based evaluation (run a command), LLM judge (compare diffs), or hybrid.

## Usage

```
/hub:eval                           # Eval latest session using configured criteria
/hub:eval 20260317-143022           # Eval specific session
/hub:eval --judge                   # Force LLM judge mode (ignore metric config)
```

## What It Does

### Metric Mode (eval command configured)

Run the evaluation command in each agent's worktree:

```bash
python3 "${SKILL_DIR}/scripts/result_ranker.py" \
  --session {session-id} \
  --eval-cmd "{eval_cmd}" \
  --metric {metric} --direction {direction}
```

Output:
```
RANK  AGENT       METRIC      DELTA      FILES
1     agent-2     142ms       -38ms      2
2     agent-1     165ms       -15ms      3
3     agent-3     190ms       +10ms      1

Winner: agent-2 (142ms)
```

### LLM Judge Mode (no eval command, or --judge flag)

For each agent:
1. Get the diff: `git diff {base_branch}...{agent_branch}`
2. Read the agent's result post from `.agenthub/board/results/agent-{i}-result.md`
3. Compare all diffs and rank by:
   - **Correctness**: Does it solve the task?
   - **Simplicity**: Fewer lines changed is better (when equal correctness)
   - **Quality**: Clean execution, good structure, no regressions

Present rankings with justification.

Example LLM judge output for a content task:
```
RANK  AGENT    VERDICT                               WORD COUNT
1     agent-1  Strong narrative, clear CTA            1480
2     agent-3  Good data points, weak intro           1520
3     agent-2  Generic tone, no differentiation       1350

Winner: agent-1 (strongest narrative arc and call-to-action)
```

### Hybrid Mode

1. Run metric evaluation first
2. If top agents are within 10% of each other, use LLM judge to break ties
3. Present both metric and qualitative rankings

## After Eval

1. Update session state:
```bash
python3 "${SKILL_DIR}/scripts/session_manager.py" --update {session-id} --state evaluating
```

2. Tell the user:
   - Ranked results with winner highlighted
   - Next step: `/hub:merge` to merge the winner
   - Or `/hub:merge {session-id} --agent {winner}` to be explicit

## Commands

The bundled scripts are skill commands. Run them with `run_skill`, for example `run_skill hub-eval result-ranker --help`; the arguments after the command go straight to the script. They also run directly, as `python3 "${SKILL_DIR}/scripts/result_ranker.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `result-ranker` | `scripts/result_ranker.py` | Rank AgentHub agent results. |
| `session-manager` | `scripts/session_manager.py` | AgentHub session state machine and lifecycle manager. |

Exit codes pass straight through from the scripts and match mux's convention: 0 means success, 1 means the script reported findings or failed, and 2 means invalid input or a missing dependency. A dry run (`MUX_SKILL_DRY_RUN=1`) prints the command instead of running it.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/agenthub/skills/eval (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
