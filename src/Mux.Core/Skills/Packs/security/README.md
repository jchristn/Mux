# Security pack

Application, cloud, AI, and offensive security playbooks.

Install with `mux skill pack install security`; nothing in this pack is listed or seeded until then. Skills marked as bundling scripts call them as `python3 "${SKILL_DIR}/scripts/<name>.py"`, so Python 3 must be on PATH, and a few need third-party packages named in their own instructions.

Adapted from https://github.com/alirezarezvani/claude-skills (MIT); see THIRD_PARTY_NOTICES.md in the mux repository.

| Skill | When to use it | Bundles scripts |
|---|---|:---:|
| `ai-security` | Use when assessing AI/ML systems for prompt injection, jailbreak vulnerabilities, model inversion risk, data poisoning exposure, or agent tool abuse. | yes |
| `cloud-security` | Use when assessing cloud infrastructure for security misconfigurations, IAM privilege escalation paths, S3 public exposure, open security group rules, or IaC security gaps. | yes |
| `incident-response` | Use when a security incident has been detected or declared and needs classification, triage, escalation path determination, and forensic evidence collection. | yes |
| `red-team` | Use when planning or executing authorized red team engagements, attack path analysis, or offensive security simulations. | yes |
| `security-pen-testing` | Use when the user asks to perform security audits, penetration testing, vulnerability scanning, OWASP Top 10 checks, or offensive security assessments. | yes |
| `threat-detection` | Use when hunting for threats in an environment, analyzing IOCs, or detecting behavioral anomalies in telemetry. | yes |
