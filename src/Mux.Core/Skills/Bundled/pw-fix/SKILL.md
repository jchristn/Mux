---
name: pw-fix
description: >-
  Fix failing or flaky Playwright tests. Use when user says "fix test", "flaky test", "test failing", "debug test",
  "test broken", "test passes sometimes", or "intermittent failure".
category: testing
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering-team/playwright-pro/skills/fix"
license: MIT
appliesTo:
  - "playwright.config.*"
  - "package.json"
---

# Fix Failing or Flaky Tests

Diagnose and fix a Playwright test that fails or passes intermittently using a systematic taxonomy.

## Input

`$ARGUMENTS` contains:
- A test file path: `e2e/login.spec.ts`
- A test name: ""should redirect after login"`
- A description: `"the checkout test fails in CI but passes locally"`

## Steps

### 1. Reproduce the Failure

Run the test to capture the error:

```bash
npx playwright test <file> --reporter=list
```

If the test passes, it's likely flaky. Run burn-in:

```bash
npx playwright test <file> --repeat-each=10 --reporter=list
```

If it still passes, try with parallel workers:

```bash
npx playwright test --fully-parallel --workers=4 --repeat-each=5
```

### 2. Capture Trace

Run with full tracing:

```bash
npx playwright test <file> --trace=on --retries=0
```

Read the trace output. Use `/debug` to analyze trace files if available.

### 3. Categorize the Failure

Load `flaky-taxonomy.md` from this skill directory.

Every failing test falls into one of four categories:

| Category | Symptom | Diagnosis |
|---|---|---|
| **Timing/Async** | Fails intermittently everywhere | `--repeat-each=20` reproduces locally |
| **Test Isolation** | Fails in suite, passes alone | `--workers=1 --grep "test name"` passes |
| **Environment** | Fails in CI, passes locally | Compare CI vs local screenshots/traces |
| **Infrastructure** | Random, no pattern | Error references browser internals |

### 4. Apply Targeted Fix

**Timing/Async:**
- Replace `waitForTimeout()` with web-first assertions
- Add `await` to missing Playwright calls
- Wait for specific network responses before asserting
- Use `toBeVisible()` before interacting with elements

**Test Isolation:**
- Remove shared mutable state between tests
- Create test data per-test via API or fixtures
- Use unique identifiers (timestamps, random strings) for test data
- Check for database state leaks

**Environment:**
- Match viewport sizes between local and CI
- Account for font rendering differences in screenshots
- Use `docker` locally to match CI environment
- Check for timezone-dependent assertions

**Infrastructure:**
- Increase timeout for slow CI runners
- Add retries in CI config (`retries: 2`)
- Check for browser OOM (reduce parallel workers)
- Ensure browser dependencies are installed

### 5. Verify the Fix

Run the test 10 times to confirm stability:

```bash
npx playwright test <file> --repeat-each=10 --reporter=list
```

All 10 must pass. If any fail, go back to step 3.

### 6. Prevent Recurrence

Suggest:
- Add to CI with `retries: 2` if not already
- Enable `trace: 'on-first-retry'` in config
- Add the fix pattern to project's test conventions doc

## Output

- Root cause category and specific issue
- The fix applied (with diff)
- Verification result (10/10 passes)
- Prevention recommendation

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

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering-team/playwright-pro/skills/fix (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
