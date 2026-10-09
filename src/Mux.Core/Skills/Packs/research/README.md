# Research pack

Deep research, literature review, and briefing playbooks.

Install with `mux skill pack install research`; nothing in this pack is listed or seeded until then. Skills marked as bundling scripts call them as `python3 "${SKILL_DIR}/scripts/<name>.py"`, so Python 3 must be on PATH, and a few need third-party packages named in their own instructions.

Adapted from https://github.com/alirezarezvani/claude-skills (MIT); see THIRD_PARTY_NOTICES.md in the mux repository.

| Skill | When to use it | Bundles scripts |
|---|---|:---:|
| `deep-research` | Run a disciplined, multi-source research investigation for a high-stakes question or decision, fan-out web search across many channels, parallel sub-agents, source triangulation (each claim backed by ≥3 independent sources), an adversarial review pass, and every source saved to its own file with verbatim quotes for reuse. Use when a low-quality answer is expensive: strategy work, comparing N products/methods/markets, validating a hypothesis with external data, or mapping how a field works. | no |
| `deepread` | Use when the user asks to deeply read a book, article, PDF, or document set; extract claims and evidence; build a knowledge map; or learn through Feynman explanation and recall. | no |
| `dossier` | Decision-grade entity research skill: produces a hypothesis-tested dossier on a specific company, person, nonprofit, or government org, not a generic profile. Use when the user asks for background research, diligence, or meeting prep on a specific entity (e. | yes |
| `litreview` | Academic literature orientation skill that searches papers via free keyless APIs (PubMed E-utilities + OpenAlex) by default, with the Consensus MCP as an optional enhancement lane when connected, builds a strategic search plan using PICO (default) or SPIDER / Decomposition / hybrid as fallbacks, and synthesizes findings into a formatted Word (.docx) research guide. Use when the user starts literature-oriented research (e. | yes |
| `pulse` | Multi-source recency research skill that takes the pulse of any topic across Reddit, Hacker News, the open web, and optionally X/Twitter within a configurable recent window (default 30 days). Use when the user requests multi-source recency intelligence on a topic (e. | yes |
