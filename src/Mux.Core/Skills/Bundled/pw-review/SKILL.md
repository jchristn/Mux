---
name: pw-review
description: >-
  Review Playwright tests for quality. Use when user says "review tests", "check test quality", "audit tests",
  "improve tests", "test code review", or "playwright best practices check".
category: testing
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering-team/playwright-pro/skills/pw-review"
license: MIT
appliesTo:
  - "playwright.config.*"
---

# Review Playwright Tests

Systematically review Playwright test files for anti-patterns, missed best practices, and coverage gaps.

## Input

`$ARGUMENTS` can be:
- A file path: review that specific test file
- A directory: review all test files in the directory
- Empty: review all tests in the project's `testDir`

## Steps

### 1. Gather Context

- Read `playwright.config.ts` for project settings
- List all `*.spec.ts` / `*.spec.js` files in scope
- If reviewing a single file, also check related page objects and fixtures

### 2. Check Each File Against Anti-Patterns

Load `anti-patterns.md` from this skill directory. Check for all 20 anti-patterns.

**Critical (must fix):**
1. `waitForTimeout()` usage
2. Non-web-first assertions (`expect(await ...)`)
3. Hardcoded URLs instead of `baseURL`
4. CSS/XPath selectors when role-based exists
5. Missing `await` on Playwright calls
6. Shared mutable state between tests
7. Test execution order dependencies

**Warning (should fix):**
8. Tests longer than 50 lines (consider splitting)
9. Magic strings without named constants
10. Missing error/edge case tests
11. `page.evaluate()` for things locators can do
12. Nested `test.describe()` more than 2 levels deep
13. Generic test names ("should work", "test 1")

**Info (consider):**
14. No page objects for pages with 5+ locators
15. Inline test data instead of factory/fixture
16. Missing accessibility assertions
17. No visual regression tests for UI-heavy pages
18. Console error assertions not checked
19. Network idle waits instead of specific assertions
20. Missing `test.describe()` grouping

### 3. Score Each File

Rate 1-10 based on:
- **9-10**: Production-ready, follows all golden rules
- **7-8**: Good, minor improvements possible
- **5-6**: Functional but has anti-patterns
- **3-4**: Significant issues, likely flaky
- **1-2**: Needs rewrite

### 4. Generate Review Report

For each file:
```
## <filename>: Score: X/10

### Critical
- Line 15: `waitForTimeout(2000)` → use `expect(locator).toBeVisible()`
- Line 28: CSS selector `.btn-submit` → `getByRole('button', { name: "submit" })`

### Warning
- Line 42: Test name "test login" → "should redirect to dashboard after login"

### Suggestions
- Consider adding error case: what happens with invalid credentials?
```

### 5. For Project-Wide Review

If reviewing an entire test suite:
- Spawn sub-agents per file for parallel review (up to 5 concurrent)
- Or use `/batch` for very large suites
- Aggregate results into a summary table

### 6. Offer Fixes

For each critical issue, provide the corrected code. Ask user: "Apply these fixes? [Yes/No]"

If yes, apply all fixes using `Edit` tool.

## Output

- File-by-file review with scores
- Summary: total files, average score, critical issue count
- Actionable fix list
- Coverage gaps identified (pages/features with no tests)

## Playwright golden rules

These come from the Playwright Pro family and apply to every test this skill writes, reviews, or fixes.

1. Prefer `getByRole()` over CSS or XPath; it survives markup changes.
2. Never use `page.waitForTimeout()`; use web-first assertions instead.
3. `expect(locator)` retries until it passes; `expect(await locator.textContent())` does not.
4. Isolate every test: no state shared between tests.
5. Put `baseURL` in the config; no hardcoded URLs in tests.
6. Retries: `2` in CI, `0` locally.
7. Traces: `'on-first-retry'`, for rich debugging without slowing every run.
8. Fixtures over globals: use `test.extend()` for shared setup.
9. One behavior per test; several related assertions are fine.
10. Mock external services only, never your own app.

Locator priority, first match wins: `getByRole()` (buttons, links, headings, form elements), `getByLabel()` (labelled fields), `getByText()` (non-interactive text), `getByPlaceholder()`, `getByTestId()` (when nothing semantic exists), and `page.locator()` with CSS or XPath only as a last resort.

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering-team/playwright-pro/skills/pw-review (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
