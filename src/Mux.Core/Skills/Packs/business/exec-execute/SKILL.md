---
name: exec-execute
description: >-
  Generate a 90-day execution plan with weekly milestones, DRIs, and check-in cadence from an approved decision. Use
  when a logged decision needs to become an operating plan, e.g. turning an approved market-entry call into weekly
  milestones with DRIs.
category: business
source: "https://github.com/alirezarezvani/claude-skills@19392f7/c-level-agents/skills/execute"
license: MIT
---

# /exec-execute, 90-Day Execution Plan

**Command:** `/exec-execute <decision-path>`

Turns an approved decision into a 90-day plan with weekly milestones, named DRIs, and a check-in cadence. Where most decisions die: between "we decided" and "what's next Monday?"

## Pipeline Position

```
/office-hours  →  /exec-brief  →  /boardroom  →  /exec-decide  →  /exec-execute  →  /exec-post-mortem
                                                                       ↑ you are here
```

## Input

An approved decision record (output of `/exec-decide`).

## Output Plan Format

Saved to `~/.mux/execution/YYYY-MM-DD-<slug>.md`:

```markdown
# Execution Plan: <decision title>
**Decision:** <link to /exec-decide record>
**Owner (Sponsor):** <founder or exec>
**Start:** YYYY-MM-DD
**Checkpoint:** YYYY-MM-DD (90d)

## Outcome (binding)
[Copied from decision: success + kill criteria]

## Workstreams
| Workstream | DRI | Success Metric | Status |
|---|---|---|---|
| <e.g., Pricing rollout> | <name> | <metric, threshold> | Not started |
| <e.g., Comms> | <name> | <metric> | Not started |
| <e.g., Eng changes> | <name> | <metric> | Not started |

## Weekly Milestones
| Week | Milestone | DRI | Definition of Done |
|---|---|---|---|
| 1 | <e.g., positioning locked> | <name> | <observable outcome> |
| 2 | <e.g., draft launched> | <name> | <observable> |
| 3 | ... | | |
| 12 | <e.g., checkpoint review> | <name> | <observable> |

## Cadence
- **Weekly:** Owner reviews status (15 min)
- **Bi-weekly:** Cross-functional sync (30 min)
- **Day 30 / 60 / 90:** Checkpoint with cs-chief-of-staff

## Dependencies
- Internal: <list>
- External: <vendors, regulators, customers>

## Risk Register
| Risk | Likelihood | Impact | Owner | Mitigation |
|---|---|---|---|---|
| <e.g., delayed legal review> | M | H | <name> | <plan> |

## Kill Criteria Watch
[Copied from decision; reviewed at every checkpoint]
- <metric, threshold, action>
```

## Workflow

1. Read the decision record
2. Decompose the chosen option into 3-6 workstreams
3. Name a DRI for each workstream
4. Reverse-engineer 12 weekly milestones from the checkpoint date
5. Set the cadence (weekly + bi-weekly + 30/60/90 checkpoints)
6. Build the risk register (cross-reference original Phase 4 devil's-advocate concerns)
7. Save and notify DRIs

## Why 90 Days

- Long enough to show real signal (not just activity)
- Short enough to course-correct before damage compounds
- Matches quarterly OKR cycle, fundraise sprints, and most board cadences

## Routing

- `/exec-post-mortem <decision>`, at day 90 (or earlier if kill criteria trigger)
- `/boardroom`, if a checkpoint reveals a need to re-decide

## Related

- Skills: [`coo-advisor`](the `coo-advisor` skill (not included in mux)), [`strategic-alignment`](the `strategic-alignment` skill (not included in mux)), [`change-management`](the `change-management` skill)
- Agent: [`cs-coo-advisor`](${SKILL_DIR}/shared/agents/cs-coo-advisor.md)

---

**Version:** 1.0.0

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/c-level-agents/skills/execute (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
