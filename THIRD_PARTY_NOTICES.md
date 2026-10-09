# Third-Party Notices

mux includes material adapted from the projects below. Each is distributed under the MIT License, whose notice is reproduced here as that license requires. mux's own code is under its own license; these notices cover only the files and text named in each section.

## alirezarezvani/claude-skills

Source: https://github.com/alirezarezvani/claude-skills, reviewed at commit `19392f7a08264ed00486a251f5b2098321771f94`. The skills were adapted for mux: em-dashes removed, paths to bundled files rewritten to `${SKILL_DIR}`, Claude Code specifics replaced with their mux equivalents, and mux frontmatter (`category`, `source`, `license`, and gating) added. Each adapted `SKILL.md` names its source folder in its `source` field and in a closing comment.

**Bundled default skills** (`src/Mux.Core/Skills/Bundled`): `a11y-audit`, `api-design-reviewer`, `ci-cd-pipeline-builder`, `handoff`, `performance-profiler`, `pw-coverage`, `pw-fix`, `pw-generate`, `pw-init`, `pw-migrate`, `pw-report`, `pw-review`, `security-guidance`, `ship-gate`, `skill-extract`, `skill-security-auditor`, `tdd-guide`.

**Optional pack skills** (`src/Mux.Core/Skills/Packs`):

- business: `board-deck-builder`, `board-meeting`, `board-prep`, `boardroom`, `business-investment-advisor`, `caio-review`, `cco-review`, `cdo-review`, `cfo-review`, `change-management`, `chief-of-staff`, `ciso-review`, `cmo-review`, `company-os`, `competitive-intel`, `context-engine`, `contract-and-proposal-writer`, `cpo-review`, `cro-review`, `cross-eval`, `cs-onboard`, `cto-advisor`, `cto-review`, `culture-architect`, `exec-brief`, `exec-decide`, `exec-execute`, `exec-freeze`, `exec-onboard`, `exec-post-mortem`, `founder-coach`, `founder-mode`, `gc-review`, `internal-narrative`, `intl-expansion`, `knowledge-ops`, `ma-playbook`, `mentor-challenge`, `mentor-hard-call`, `mentor-postmortem`, `mentor-stress-test`, `office-hours`, `rfp-responder`, `vpe-review`.
- compliance: `agent-decision-receipts`, `ai-act-readiness`, `aims-audit`, `compliance-readiness`, `fda-qsr-audit-prep`, `gdpr-audit-prep`, `gdpr-dsgvo-expert`, `iso13485-audit-prep`, `iso27001-audit-prep`, `soc2-audit-prep`.
- data: `data-quality-auditor`, `senior-data-scientist`, `senior-ml-engineer`, `senior-prompt-engineer`, `statistical-analyst`, `universal-scraping-architect`.
- docs: `md-document`, `md-review`.
- engineering: `agent-designer`, `agent-harness`, `agent-workflow-designer`, `api-test-suite-builder`, `ar-loop`, `ar-resume`, `ar-run`, `ar-setup`, `ar-status`, `autoresearch-agent`, `aws-solution-architect`, `azure-cloud-architect`, `boost-asio-pro`, `browser-automation`, `browserstack`, `chaos-engineering`, `code-tour`, `database-designer`, `database-schema-designer`, `email-template-builder`, `embedded-iot-mentor`, `env-secrets-manager`, `epic-design`, `feature-flags-architect`, `full-page-screenshot`, `gcp-cloud-architect`, `grill-me`, `grill-with-docs`, `hub-board`, `hub-eval`, `hub-init`, `hub-merge`, `hub-run`, `hub-spawn`, `hub-status`, `incident-commander`, `karpathy-coder`, `kubernetes-operator`, `llm-cost-optimizer`, `loop-library`, `mcp-server-builder`, `migration-architect`, `minimalist`, `named-persona-adversarial-review`, `observability-designer`, `prompt-governance`, `rag-architect`, `runbook-generator`, `secrets-vault-manager`, `self-eval`, `senior-architect`, `senior-backend`, `senior-devops`, `senior-frontend`, `senior-qa`, `senior-secops`, `slo-architect`, `snowflake-development`, `spec-driven-workflow`, `sql-database-assistant`, `strict-api`, `stripe-integration-expert`, `testrail`, `zero-hallucination-coder`.
- marketing: `brand-guidelines`, `business-name-fit`, `marketing-ideas`, `marketing-psychology`, `marketing-strategy-pmm`, `paywall-upgrade-cro`, `popup-cro`, `social-content`, `video-content-strategist`, `youtube-full`.
- product: `apple-hig-expert`, `code-to-prd`, `competitive-teardown`, `confluence-expert`, `experiment-designer`, `jira-expert`, `landing-page-generator`, `meeting-analyzer`, `product-analytics`, `product-discovery`, `product-manager-toolkit`, `product-strategist`, `roadmap-communicator`, `saas-scaffolder`, `scrum-master`, `spec-to-repo`, `team-communications`, `ui-design-system`.
- productivity: `reflect`.
- research: `deep-research`, `deepread`, `dossier`, `litreview`, `pulse`.
- security: `ai-security`, `cloud-security`, `incident-response`, `red-team`, `security-pen-testing`, `threat-detection`.

**Material merged into existing mux skills** (files under `src/Mux.Core/Skills/Resources/Adapted` and text in the skill bodies):

- `code-review`: from adversarial-reviewer, pr-review-expert, and code-reviewer (its universal and per-language rule files ship under resources/review-rules).
- `security-review`: from dependency-auditor (license_checker.py) and senior-security (STRIDE guidance and threat_modeler.py).
- `debug`: from focused-fix.
- `release-notes`: from changelog-generator.
- `explain-codebase`: from codebase-onboarding (codebase_analyzer.py and onboarding-template.md).
- `new-skill`: from write-a-skill.
- `pw-generate`, `pw-review`, and `pw-fix`: from playwright-pro (the golden rules and locator priority).
- `project-detect`: from monorepo-navigator (monorepo_analyzer.py).
- `todo-scan`: from tech-debt-tracker.
- `terraform`: from terraform-patterns (tf_module_analyzer.py and tf_security_scanner.py).
- `dockerfile-lint`: from docker-development (dockerfile_analyzer.py and compose_validator.py).

```text
MIT License

Copyright (c) 2025 Alireza Rezvani

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## mattpocock/skills

Several of the skills above are themselves derived from https://github.com/mattpocock/skills by Matt Pocock, also under the MIT License: `handoff` (from handoff), the pack skills `grill-me` (from grill-me) and `grill-with-docs` (from grill-with-docs), and the guidance merged into `new-skill` (from write-a-skill). The notice below applies to that material in addition to the one above.

```text
MIT License

Copyright (c) Matt Pocock

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
