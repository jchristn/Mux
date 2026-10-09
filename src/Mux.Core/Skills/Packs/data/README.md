# Data pack

Databases, SQL, data quality, and statistics playbooks.

Install with `mux skill pack install data`; nothing in this pack is listed or seeded until then. Skills marked as bundling scripts call them as `python3 "${SKILL_DIR}/scripts/<name>.py"`, so Python 3 must be on PATH, and a few need third-party packages named in their own instructions.

Adapted from https://github.com/alirezarezvani/claude-skills (MIT); see THIRD_PARTY_NOTICES.md in the mux repository.

| Skill | When to use it | Bundles scripts |
|---|---|:---:|
| `data-quality-auditor` | Audit datasets for completeness, consistency, accuracy, and validity. Use when the user asks to check data quality, profile a dataset, hunt outliers or missing values, or validate data before analysis or model training. | yes |
| `senior-data-scientist` | World-class senior data scientist skill specialising in statistical modeling, experiment design, causal inference, and predictive analytics. Use when designing or analysing controlled experiments, building and evaluating classification or regression models, performing causal analysis on observational data, engineering features for structured tabular datasets, or translating statistical findings into data-driven business decisions. | yes |
| `senior-ml-engineer` | ML engineering skill for productionizing models, building MLOps pipelines, and integrating LLMs. Use when the user asks about deploying ML models to production, setting up MLOps infrastructure (MLflow, Kubeflow, Kubernetes, Docker), monitoring model performance or drift, building RAG pipelines, or integrating LLM APIs with retry logic and cost controls. | yes |
| `senior-prompt-engineer` | Use when the user asks to optimize prompts, design prompt templates, evaluate LLM outputs with an eval set, measure RAG retrieval quality, validate agent/tool configurations, analyze token usage, or design structured-output contracts. | yes |
| `statistical-analyst` | Run hypothesis tests, analyze A/B experiment results, calculate sample sizes, and interpret statistical significance with effect sizes. Use when you need to validate whether observed differences are real, size an experiment correctly before launch, or interpret test results with confidence. | yes |
| `universal-scraping-architect` | Use for web scraping, crawling, document extraction, API parsing, or building validation-heavy data pipelines using Firecrawl or local Python scripts. | yes |
