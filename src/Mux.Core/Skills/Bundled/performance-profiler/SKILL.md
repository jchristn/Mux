---
name: performance-profiler
description: >-
  Systematic performance profiling for Node.js, Python, and Go applications. Identifies CPU, memory, and I/O
  bottlenecks, generates flamegraphs, analyzes bundle sizes, optimizes database queries, runs load tests with k6 and
  Artillery. Always measures before and after. Use when investigating a slow endpoint, planning a performance budget,
  or hunting a memory leak in production.
category: devops
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/performance-profiler"
license: MIT
appliesTo:
  - "package.json"
  - "pyproject.toml"
  - "requirements*.txt"
  - "go.mod"
---

# Performance Profiler

**Tier:** POWERFUL  
**Category:** Engineering  
**Domain:** Performance Engineering  

---

## Overview

Systematic performance profiling for Node.js, Python, and Go applications. Identifies CPU, memory, and I/O bottlenecks; generates flamegraphs; analyzes bundle sizes; optimizes database queries; detects memory leaks; and runs load tests with k6 and Artillery. Always measures before and after.

## Core Capabilities

- **CPU profiling**: flamegraphs for Node.js, py-spy for Python, pprof for Go
- **Memory profiling**: heap snapshots, leak detection, GC pressure
- **Bundle analysis**: webpack-bundle-analyzer, Next.js bundle analyzer
- **Database optimization**: EXPLAIN ANALYZE, slow query log, N+1 detection
- **Load testing**: k6 scripts, Artillery scenarios, ramp-up patterns
- **Before/after measurement**: establish baseline, profile, optimize, verify

---

## When to Use

- App is slow and you don't know where the bottleneck is
- P99 latency exceeds SLA before a release
- Memory usage grows over time (suspected leak)
- Bundle size increased after adding dependencies
- Preparing for a traffic spike (load test before launch)
- Database queries taking >100ms

---

## Quick Start

```bash
# Analyze a project for performance risk indicators
python3 "${SKILL_DIR}/scripts/performance_profiler.py" /path/to/project

# JSON output for CI integration
python3 "${SKILL_DIR}/scripts/performance_profiler.py" /path/to/project --json

# Custom large-file threshold
python3 "${SKILL_DIR}/scripts/performance_profiler.py" /path/to/project --large-file-threshold-kb 256
```

---

## Golden Rule: Measure First

```bash
# Establish baseline BEFORE any optimization
# Record: P50, P95, P99 latency | RPS | error rate | memory usage

# Wrong: "I think the N+1 query is slow, let me fix it"
# Right: Profile → confirm bottleneck → fix → measure again → verify improvement
```

---

## Node.js Profiling
→ See ${SKILL_DIR}/references/profiling-recipes.md for details

## References

- [${SKILL_DIR}/references/profiling-recipes.md](${SKILL_DIR}/references/profiling-recipes.md), Node.js/Python/Go profiling commands, flamegraph generation, heap snapshots
- [${SKILL_DIR}/references/optimization-playbook.md](${SKILL_DIR}/references/optimization-playbook.md), before/after measurement template, quick-win optimization checklist (DB/Node/bundle/API), common pitfalls, best practices

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering/skills/performance-profiler (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
