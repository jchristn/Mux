---
name: product-discovery
description: >-
  Use when validating product opportunities, mapping assumptions, planning discovery sprints, or testing
  problem-solution fit before committing delivery resources.
category: product
source: "https://github.com/alirezarezvani/claude-skills@19392f7/product-team/skills/product-discovery"
license: MIT
commands:
  - name: assumption-mapper
    description: Prioritize product assumptions and suggest validation tests.
    run: scripts/assumption_mapper.py
    interpreter: python
---

# Product Discovery

Run structured discovery to identify high-value opportunities and de-risk product bets.

## When To Use

Use this skill for:
- Opportunity Solution Tree facilitation
- Assumption mapping and test planning
- Problem validation interviews and evidence synthesis
- Solution validation with prototypes/experiments
- Discovery sprint planning and outputs

## Core Discovery Workflow

1. Define desired outcome
- Set one measurable outcome to improve.
- Establish baseline and target horizon.

2. Build Opportunity Solution Tree (OST)
- Outcome -> opportunities -> solution ideas -> experiments
- Keep opportunities grounded in user evidence, not internal opinions.

3. Map assumptions
- Identify desirability, viability, feasibility, and usability assumptions.
- Score assumptions by risk and certainty.

Use:
```bash
python3 "${SKILL_DIR}/scripts/assumption_mapper.py" assumptions.csv
```

4. Validate the problem
- Conduct interviews and behavior analysis.
- Confirm frequency, severity, and willingness to solve.
- Reject weak opportunities early.

5. Validate the solution
- Prototype before building.
- Run concept, usability, and value tests.
- Measure behavior, not only stated preference.

6. Plan discovery sprint
- 1-2 week cycle with explicit hypotheses
- Daily evidence reviews
- End with decision: proceed, pivot, or stop

## Opportunity Solution Tree (Teresa Torres)

Structure:
- Outcome: metric you want to move
- Opportunities: unmet customer needs/pains
- Solutions: candidate interventions
- Experiments: fastest learning actions

Quality checks:
- At least 3 distinct opportunities before converging.
- At least 2 experiments per top opportunity.
- Tie every branch to evidence source.

## Assumption Mapping

Assumption categories:
- Desirability: users want this
- Viability: business value exists
- Feasibility: team can build/operate it
- Usability: users can successfully use it

Prioritization rule:
- High risk + low certainty assumptions are tested first.

## Problem Validation Techniques

- Problem interviews focused on current behavior
- Journey friction mapping
- Support ticket and sales-call synthesis
- Behavioral analytics triangulation

Evidence threshold examples:
- Same pain repeated across multiple target users
- Observable workaround behavior
- Measurable cost of current pain

## Solution Validation Techniques

- Concept tests (value proposition comprehension)
- Prototype usability tests (task success/time-to-complete)
- Fake door or concierge tests (demand signal)
- Limited beta cohorts (retention/activation signals)

## Discovery Sprint Planning

Suggested 10-day structure:
- Day 1-2: Outcome + opportunity framing
- Day 3-4: Assumption mapping + test design
- Day 5-7: Problem and solution tests
- Day 8-9: Evidence synthesis + decision options
- Day 10: Stakeholder decision review

## Tooling

### `${SKILL_DIR}/scripts/assumption_mapper.py`

CLI utility that:
- reads assumptions from CSV or inline input
- scores risk/certainty priority
- emits prioritized test plan with suggested test types

See `${SKILL_DIR}/references/discovery-frameworks.md` for framework details.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/product-team/skills/product-discovery (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->

## Commands

Run the bundled scripts through mux with `run_skill product-discovery <command> [arguments]` (pass `--help` to see a command's options), or call them directly with `python3 "${SKILL_DIR}/scripts/<file>"`. Exit codes pass through: 0 means success, 1 means findings or a failed check, and 2 means invalid arguments. With `MUX_SKILL_DRY_RUN=1`, mux prints the command instead of running it.

| Command | Script | What it does |
|---|---|---|
| `assumption-mapper` | `scripts/assumption_mapper.py` | Prioritize product assumptions and suggest validation tests. |
