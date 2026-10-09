# Productivity pack

Personal reflection and working habits.

Install with `mux skill pack install productivity`; nothing in this pack is listed or seeded until then. Skills marked as bundling scripts call them as `python3 "${SKILL_DIR}/scripts/<name>.py"`, so Python 3 must be on PATH, and a few need third-party packages named in their own instructions.

Adapted from https://github.com/alirezarezvani/claude-skills (MIT); see THIRD_PARTY_NOTICES.md in the mux repository.

| Skill | When to use it | Bundles scripts |
|---|---|:---:|
| `reflect` | Mid-conversation reflection skill that pauses execution and zooms out from detail-mode to honestly reassess direction, assumptions, and bias. Use when the user says 'reflect', 'take a step back', 'step back', 'zoom out', 'are we missing something', 'bigger picture', 'sanity check this', 'are we on track', 'are we overthinking this', 'forest for the trees', or any variation signaling intent to break out of detail-mode and reassess. | yes |
