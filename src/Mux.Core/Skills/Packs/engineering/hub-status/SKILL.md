---
name: hub-status
description: >-
  Show DAG state, agent progress, and branch status for an AgentHub session. Use when the user runs /hub:hub-status
  or asks how the AgentHub agents are doing.
category: workflow
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/agenthub/skills/hub-status"
license: MIT
commands:
  - name: board-manager
    description: AgentHub message board manager.
    run: scripts/board_manager.py
    interpreter: python
  - name: dag-analyzer
    description: Analyze the AgentHub git DAG.
    run: scripts/dag_analyzer.py
    interpreter: python
    timeoutMs: 600000
  - name: session-manager
    description: AgentHub session state machine and lifecycle manager.
    run: scripts/session_manager.py
    interpreter: python
    timeoutMs: 600000
---

# /hub:hub-status, Session Status

Display the current state of an AgentHub session: agent branches, commit counts, frontier status, and board updates.

## Usage

```
/hub:hub-status                        # Status for latest session
/hub:hub-status 20260317-143022        # Status for specific session
```

## What It Does

1. Run session overview:
```bash
python3 "${SKILL_DIR}/scripts/session_manager.py" --status {session-id}
```

2. Run DAG analysis:
```bash
python3 "${SKILL_DIR}/scripts/dag_analyzer.py" --status --session {session-id}
```

3. Read recent board updates:
```bash
python3 "${SKILL_DIR}/scripts/board_manager.py" --read progress
```

## Output Format

```
Session: 20260317-143022 (running)
Task: Optimize API response time below 100ms
Agents: 3 | Base: dev

AGENT    BRANCH                                        COMMITS  STATUS     LAST UPDATE
agent-1  hub/20260317-143022/agent-1/attempt-1         3        frontier   2026-03-17 14:35:10
agent-2  hub/20260317-143022/agent-2/attempt-1         5        frontier   2026-03-17 14:36:45
agent-3  hub/20260317-143022/agent-3/attempt-1         2        frontier   2026-03-17 14:34:22

Recent Board Activity:
  [progress] agent-1: Implemented caching, running tests
  [progress] agent-2: Hash map approach working, benchmarking
  [results]  agent-2: Final result posted
```

Example output for a content task:

```
Session: 20260317-151200 (running)
Task: Draft 3 competing taglines for product launch
Agents: 3 | Base: dev

AGENT    BRANCH                                        COMMITS  STATUS     LAST UPDATE
agent-1  hub/20260317-151200/agent-1/attempt-1         2        frontier   2026-03-17 15:18:30
agent-2  hub/20260317-151200/agent-2/attempt-1         2        frontier   2026-03-17 15:19:12
agent-3  hub/20260317-151200/agent-3/attempt-1         1        frontier   2026-03-17 15:17:55

Recent Board Activity:
  [progress] agent-1: Storytelling angle draft complete, refining CTA
  [progress] agent-2: Benefit-led draft done, testing urgency variant
  [results]  agent-3: Final result posted
```

## After Status

If all agents have posted results:
- Suggest `/hub:eval` to rank results

If some agents are still running:
- Show which are done vs in-progress
- Suggest waiting or checking again later

## Commands

The bundled scripts are skill commands. Run them with `run_skill`, for example `run_skill hub-status board-manager --help`; the arguments after the command go straight to the script. They also run directly, as `python3 "${SKILL_DIR}/scripts/board_manager.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `board-manager` | `scripts/board_manager.py` | AgentHub message board manager. |
| `dag-analyzer` | `scripts/dag_analyzer.py` | Analyze the AgentHub git DAG. |
| `session-manager` | `scripts/session_manager.py` | AgentHub session state machine and lifecycle manager. |

Exit codes pass straight through from the scripts and match mux's convention: 0 means success, 1 means the script reported findings or failed, and 2 means invalid input or a missing dependency. A dry run (`MUX_SKILL_DRY_RUN=1`) prints the command instead of running it.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/agenthub/skills/hub-status (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
