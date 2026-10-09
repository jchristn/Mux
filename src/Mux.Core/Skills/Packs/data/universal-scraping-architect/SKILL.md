---
name: universal-scraping-architect
description: >-
  Use for web scraping, crawling, document extraction, API parsing, or building validation-heavy data pipelines using
  Firecrawl or local Python scripts.
category: engineering
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/universal-scraping-architect/skills/universal-scraping-architect"
license: MIT
commands:
  - name: validate-extraction
    description: Validate extracted JSON records against the expected shape
    run: scripts/validate_extraction.py
    interpreter: python
  - name: local-bs4-example
    description: Run the local BeautifulSoup and pandas scraping template (needs bs4, pandas, and requests)
    run: scripts/local_bs4_example.py
    interpreter: python
    timeoutMs: 300000
  - name: firecrawl-example
    description: Run the Firecrawl extraction template (needs the firecrawl package and an API key)
    run: scripts/firecrawl_example.py
    interpreter: python
    timeoutMs: 300000
---

# Universal Scraping Architect

Design complete, robust data-extraction pipelines with intelligent routing, validation, and token-budget tracking, not brittle one-off scripts.

**Dependency Notice:** BYOK (Bring Your Own Key) pattern for Firecrawl; API keys must only be loaded via environment variables. Per-script dependencies:

| Script | Dependencies | Exact CLI |
|---|---|---|
| `${SKILL_DIR}/scripts/validate_extraction.py` | stdlib only | `python3 "${SKILL_DIR}/scripts/validate_extraction.py" output.json --json` |
| `${SKILL_DIR}/scripts/firecrawl_example.py` | `firecrawl`, `requests` (template; `--sample` runs offline) | `python3 "${SKILL_DIR}/scripts/firecrawl_example.py" --sample` |
| `${SKILL_DIR}/scripts/local_bs4_example.py` | `beautifulsoup4`, `pandas` (template; `--sample` runs offline) | `python3 "${SKILL_DIR}/scripts/local_bs4_example.py" --sample` |

## Before Starting
**Check for context first:**
If `project-context.md` exists, read it before asking questions. Determine the target data format, scale of extraction, and deployment environment before writing any code.

## How This Skill Works

This skill supports 3 extraction modes based on intelligent routing:

### Mode 1: API-Driven (Firecrawl)
Use when the source is a public URL, heavily dynamic (JS/SPA), requires search-first discovery, or involves bulk crawling across a domain.
### Mode 2: Local Python (Traditional)
Use when extracting from local files (PDF, Excel, CSV), the data is private/sensitive, or the target is a simple static HTML page where Firecrawl is overkill.
### Mode 3: Hybrid Pipeline
Use when Firecrawl handles URL discovery/web extraction, but local Python (Pandas) is required to clean, normalize, and structure the output before saving.

## The Extraction Pipeline

When executing a scraping task, always follow this sequence:
1. **Route the Approach:** Explicitly state whether Firecrawl or Local Python is being used and why.
2. **Track Budgets:** Estimate Firecrawl API quotas or LLM token context limits before executing large jobs. 
3. **Extract Safely:** Implement checkpointing for multi-page jobs. Handle pagination and dynamic layouts gracefully. Start from the editable runner templates, `${SKILL_DIR}/scripts/firecrawl_example.py` (Mode 1) or `${SKILL_DIR}/scripts/local_bs4_example.py` (Mode 2); run each with `--sample` first to see the expected summary shape without network access.
4. **Validate & Clean:** Run `python3 "${SKILL_DIR}/scripts/validate_extraction.py" extracted_output.json --json` on every extraction result before delivering it. It exits 0 only on `{"status": "ok"}`; `warning` (empty output) or `error` (malformed JSON) exit 1, fix and re-extract, never ship unvalidated data. Beyond this structural gate, also check required fields and duplicates against the pipeline spec before delivering.
5. **Format:** Default to CSV for tabular data, JSON for nested structures, and Markdown for clean text.

## Proactive Triggers

Surface these issues WITHOUT being asked when you notice them in context:
- **Hardcoded API Keys** → Flag immediately and rewrite to use `os.getenv('FIRECRAWL_API_KEY')`.
- **Private Data Leakage** → If the user asks to send local, sensitive files to an external API, flag the privacy risk and suggest Mode 2 (Local Python).
- **Missing Pagination** → If the target implies hundreds of records but no pagination logic is requested, flag it and add checkpointing.

## Output Artifacts

| When you ask for... | You get... |
|---------------------|------------|
| "Scrape this site" | A fully validated Python extraction script with routing logic and error handling. |
| "Get data from this table" | A clean CSV/JSON dataset with a summary log of row counts and empty values. |
| "Crawl these docs" | A Markdown deliverable chunked for LLM token limits. |

## Anti-Patterns
- **Brittle Selectors:** Never use highly nested CSS selectors (e.g., `div > span > ul > li:nth-child(3)`). Use data attributes or robust structural anchors.
- **Ignoring Etiquette:** Never scrape without checking `robots.txt` or implementing sensible rate limits.
- **No Validation:** Never blindly write scraped data to a file without checking if the array is empty or missing critical keys.

## Related Skills
- **data-cleaning**: Use when the scraped data requires complex statistical normalization or deduplication.
- **browser-automation**: Use for highly interactive scraping requiring user emulation (clicks, logins) where Firecrawl is insufficient.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/universal-scraping-architect/skills/universal-scraping-architect (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->

## Commands

Run these with `run_skill` (skill `universal-scraping-architect`); each passes its arguments to the bundled script and returns its output and exit code. Add `--help` to see a script's options. Outside mux, run the same script directly, for example `python3 "${SKILL_DIR}/scripts/validate_extraction.py" --help`.

| Command | Script | What it does |
|---|---|---|
| `validate-extraction` | `scripts/validate_extraction.py` | Validate extracted JSON records against the expected shape. |
| `local-bs4-example` | `scripts/local_bs4_example.py` | Run the local BeautifulSoup and pandas scraping template (needs bs4, pandas, and requests). |
| `firecrawl-example` | `scripts/firecrawl_example.py` | Run the Firecrawl extraction template (needs the firecrawl package and an API key). |

Exit codes pass through from the script: 0 means success, 1 means the script reported problems, and 2 usually means invalid arguments (argparse uses 2 for usage errors).
