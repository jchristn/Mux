# Compliance pack

Audit preparation and readiness for SOC 2, ISO 27001, ISO 13485, GDPR, FDA, and the EU AI Act.

Install with `mux skill pack install compliance`; nothing in this pack is listed or seeded until then. Skills marked as bundling scripts call them as `python3 "${SKILL_DIR}/scripts/<name>.py"`, so Python 3 must be on PATH, and a few need third-party packages named in their own instructions.

Adapted from https://github.com/alirezarezvani/claude-skills (MIT); see THIRD_PARTY_NOTICES.md in the mux repository.

| Skill | When to use it | Bundles scripts |
|---|---|:---:|
| `agent-decision-receipts` | Mint a tamper-evident, post-quantum-signed receipt for a consequential agent action (deploy, delete, pay, grant-access, model decision) so it can be verified later from the certificate alone. Use when an autonomous agent takes a side-effecting action that may need to be proven later, or when satisfying EU AI Act Article 12 record-keeping. | yes |
| `ai-act-readiness` | /ai-act-readiness <system>, EU AI Act 6-question forcing interrogation. | no |
| `aims-audit` | /aims-audit <scope>, ISO/IEC 42001 AIMS internal-audit 6-question forcing interrogation. | no |
| `compliance-readiness` | /compliance-readiness <program>, Multi-framework compliance officer 6-question forcing interrogation of any compliance program. | no |
| `fda-qsr-audit-prep` | /fda-qsr-audit-prep <scope>, FDA 21 CFR 820 (QSR / QMSR) audit 6-question forcing interrogation. | no |
| `gdpr-audit-prep` | /gdpr-audit-prep <scope>, GDPR audit 6-question Article-cited forcing interrogation. | no |
| `gdpr-dsgvo-expert` | GDPR and German DSGVO compliance automation. Use when running GDPR compliance assessments, privacy audits, data protection planning, DPIA generation, or data subject rights (DSAR) management (e. | yes |
| `iso13485-audit-prep` | /iso13485-audit-prep <scope>, ISO 13485 QMS audit 6-question forcing interrogation. | no |
| `iso27001-audit-prep` | /iso27001-audit-prep <scope>, ISO 27001 ISMS audit readiness 6-question forcing interrogation. | no |
| `soc2-audit-prep` | /soc2-audit-prep <scope>, SOC 2 Type II readiness 6-question forcing interrogation. | no |
