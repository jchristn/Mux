# Docs pack

Markdown document authoring and review.

Install with `mux skill pack install docs`; nothing in this pack is listed or seeded until then. Skills marked as bundling scripts call them as `python3 "${SKILL_DIR}/scripts/<name>.py"`, so Python 3 must be on PATH, and a few need third-party packages named in their own instructions.

Adapted from https://github.com/alirezarezvani/claude-skills (MIT); see THIRD_PARTY_NOTICES.md in the mux repository.

| Skill | When to use it | Bundles scripts |
|---|---|:---:|
| `md-document` | Converts long-form markdown (specs, RFCs, reports, plans, explainers) into a single-file, lightly-interactive HTML document with sticky TOC, scrollspy, search filter, code-copy buttons, and design-system-driven brand tokens. | yes |
| `md-review` | Converts a markdown PR writeup or code review (one with ```diff fenced blocks and severity-tagged > [!BLOCKER]/[!MAJOR]/[!MINOR]/[!NIT] callouts) into a single-file 2-column HTML review, unified-diff on the left, severity-tagged annotation cards on the right, top jump-nav listing every finding, mandatory named reviewer footer. | yes |
