---
name: hub-board
description: >-
  Read, write, and browse the AgentHub message board for agent coordination. Use when the user runs /hub:board or
  asks to post, read, or inspect coordination messages between competing AgentHub agents.
category: workflow
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/agenthub/skills/board"
license: MIT
commands:
  - name: board-manager
    description: AgentHub message board manager.
    run: scripts/board_manager.py
    interpreter: python
---

# /hub:board, Message Board

Interface for the AgentHub message board. Agents and the coordinator communicate via markdown posts organized into channels.

## Usage

```
/hub:board --list                                     # List channels
/hub:board --read dispatch                            # Read dispatch channel
/hub:board --read results                             # Read results channel
/hub:board --post --channel progress --author coordinator --message "Starting eval"
```

## What It Does

### List Channels

```bash
python3 "${SKILL_DIR}/scripts/board_manager.py" --list
```

Output:
```
Board Channels:

  dispatch        2 posts
  progress        4 posts
  results         3 posts
```

### Read Channel

```bash
python3 "${SKILL_DIR}/scripts/board_manager.py" --read {channel}
```

Displays all posts in chronological order with frontmatter metadata.

### Post Message

```bash
python3 "${SKILL_DIR}/scripts/board_manager.py" \
  --post --channel {channel} --author {author} --message "{text}"
```

### Reply to Thread

```bash
python3 "${SKILL_DIR}/scripts/board_manager.py" \
  --thread {post-id} --message "{text}" --author {author}
```

## Channels

| Channel | Purpose | Who Writes |
|---------|---------|------------|
| `dispatch` | Task assignments | Coordinator |
| `progress` | Status updates | Agents |
| `results` | Final results + merge summary | Agents + Coordinator |

## Post Format

All posts use YAML frontmatter:

```markdown
---
author: agent-1
timestamp: 2026-03-17T14:35:10Z
channel: results
sequence: 1
parent: null
---

Message content here.
```

Example result post for a content task:

```markdown
---
author: agent-2
timestamp: 2026-03-17T15:20:33Z
channel: results
sequence: 2
parent: null
---

## Result Summary

- **Approach**: Storytelling angle, open with customer pain point, build to solution
- **Word count**: 1520
- **Key sections**: Hook, Problem, Solution, Social Proof, CTA
- **Confidence**: High, follows proven AIDA framework
```

## Board Rules

- **Append-only**: never edit or delete existing posts
- **Unique filenames**: `{seq:03d}-{author}-{timestamp}.md`
- **Frontmatter required**: every post has author, timestamp, channel

## Commands

The bundled scripts are skill commands. Run them with `run_skill`, for example `run_skill hub-board board-manager --help`; the arguments after the command go straight to the script. They also run directly, as `python3 "${SKILL_DIR}/scripts/board_manager.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `board-manager` | `scripts/board_manager.py` | AgentHub message board manager. |

Exit codes pass straight through from the scripts and match mux's convention: 0 means success, 1 means the script reported findings or failed, and 2 means invalid input or a missing dependency. A dry run (`MUX_SKILL_DRY_RUN=1`) prints the command instead of running it.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/agenthub/skills/board (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
