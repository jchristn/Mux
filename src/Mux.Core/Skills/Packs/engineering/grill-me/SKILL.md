---
name: grill-me
description: >-
  Interview the user relentlessly about a plan or design until reaching shared understanding, resolving each branch
  of the decision tree. Use when user wants to stress-test a plan, get grilled on their design, or mentions "grill
  me".
category: review
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/grill-me/skills/grill-me"
license: MIT
commands:
  - name: decision-tree-extractor
    description: Extract decision branches from a plan/design document.
    run: scripts/decision_tree_extractor.py
    interpreter: python
  - name: grill-session-tracker
    description: Track grill-me session state across turns.
    run: scripts/grill_session_tracker.py
    interpreter: python
  - name: question-generator
    description: Generate forcing questions from a plan/design document.
    run: scripts/question_generator.py
    interpreter: python
---

# Grill Me

> Derived from [Matt Pocock's grill-me](https://github.com/mattpocock/skills/tree/main/skills/productivity/grill-me) (MIT). Matt's interview discipline preserved verbatim. Additions: extraction + question + session tools + references + cs-* wrapper (see [${SKILL_DIR}/references/companion_tooling.md](${SKILL_DIR}/references/companion_tooling.md)).

Interview me relentlessly about every aspect of this plan until we reach a shared understanding. Walk down each branch of the design tree, resolving dependencies between decisions one-by-one. For each question, provide your recommended answer.

Ask the questions one at a time.

If a question can be answered by exploring the codebase, explore the codebase instead.

## Rules (preserved + amplified)

1. **One question per turn.** Never bundle.
2. **Provide a recommended answer with each question.** Defaulting to "what do you think?" is lazy.
3. **Explore the codebase before asking.** If `grep` / `Read` resolves it, do that first. Saves a turn.
4. **Walk the tree depth-first.** Finish a branch before opening another.
5. **Track dependencies.** If decision B depends on decision A, ask A first.

## Workflow

1. User provides a plan or design (or path to one).
2. Run `${SKILL_DIR}/scripts/decision_tree_extractor.py` to extract branches.
3. Run `${SKILL_DIR}/scripts/question_generator.py` to produce the question list with recommendations.
4. Start a session: `${SKILL_DIR}/scripts/grill_session_tracker.py --action start`.
5. Walk the tree, one question at a time, recording answers in the session.
6. When all branches resolved: report "shared understanding reached" + the locked-in decisions.

## Output Pattern

Per question turn:

```
Q[i]/[total]: [question]
Recommended answer: [your call + 1-sentence rationale]

(Or: I explored the codebase and found [evidence]. Confirm?)
```

## Tooling

See [${SKILL_DIR}/references/companion_tooling.md](${SKILL_DIR}/references/companion_tooling.md). Tools: extractor + generator + tracker. Agent: `cs-grill-master`. Command: `/grill-me`.

---

**Version:** 1.0.0
**Derived:** Matt Pocock (MIT) + this repo's wrapper

## Commands

The bundled scripts are skill commands. Run them with `run_skill`, for example `run_skill grill-me decision-tree-extractor --help`; the arguments after the command go straight to the script. They also run directly, as `python3 "${SKILL_DIR}/scripts/decision_tree_extractor.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `decision-tree-extractor` | `scripts/decision_tree_extractor.py` | Extract decision branches from a plan/design document. |
| `grill-session-tracker` | `scripts/grill_session_tracker.py` | Track grill-me session state across turns. |
| `question-generator` | `scripts/question_generator.py` | Generate forcing questions from a plan/design document. |

Exit codes pass straight through from the scripts and match mux's convention: 0 means success, 1 means the script reported findings or failed, and 2 means invalid input or a missing dependency. A dry run (`MUX_SKILL_DRY_RUN=1`) prints the command instead of running it.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/grill-me/skills/grill-me (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
