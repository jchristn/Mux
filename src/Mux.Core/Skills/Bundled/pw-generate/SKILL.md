---
name: pw-generate
description: >-
  Generate Playwright tests. Use when user says "write tests", "generate tests", "add tests for", "test this
  component", "e2e test", "create test for", "test this page", or "test this feature".
category: testing
source: "https://github.com/alirezarezvani/claude-skills@19392f7/engineering-team/playwright-pro/skills/generate"
license: MIT
appliesTo:
  - "playwright.config.*"
  - "package.json"
---

# Generate Playwright Tests

Generate production-ready Playwright tests from a user story, URL, component name, or feature description.

## Input

`$ARGUMENTS` contains what to test. Examples:
- `"user can log in with email and password"`
- `"the checkout flow"`
- `"src/components/UserProfile.tsx"`
- `"the search page with filters"`

## Steps

### 1. Understand the Target

Parse `$ARGUMENTS` to determine:
- **User story**: Extract the behavior to verify
- **Component path**: Read the component source code
- **Page/URL**: Identify the route and its elements
- **Feature name**: Map to relevant app areas

### 2. Explore the Codebase

Use the `Explore` subagent to gather context:

- Read `playwright.config.ts` for `testDir`, `baseURL`, `projects`
- Check existing tests in `testDir` for patterns, fixtures, and conventions
- If a component path is given, read the component to understand its props, states, and interactions
- Check for existing page objects in `pages/`
- Check for existing fixtures in `fixtures/`
- Check for auth setup (`auth.setup.ts` or `storageState` config)

### 3. Select Templates

Check `templates/` in this plugin for matching patterns:

| If testing... | Load template from |
|---|---|
| Login/auth flow | `${SKILL_DIR}/shared/pw/templates/auth/login.md` |
| CRUD operations | `templates/crud/` |
| Checkout/payment | `templates/checkout/` |
| Search/filter UI | `templates/search/` |
| Form submission | `templates/forms/` |
| Dashboard/data | `templates/dashboard/` |
| Settings page | `templates/settings/` |
| Onboarding flow | `templates/onboarding/` |
| API endpoints | `templates/api/` |
| Accessibility | `templates/accessibility/` |

Adapt the template to the specific app: replace `{{placeholders}}` with actual selectors, URLs, and data.

### 4. Generate the Test

Follow these rules:

**Structure:**
```typescript
import { test, expect } from '@playwright/test';
// Import custom fixtures if the project uses them

test.describe('Feature Name', () => {
  // Group related behaviors

  test('should <expected behavior>', async ({ page }) => {
    // Arrange: navigate, set up state
    // Act: perform user action
    // Assert: verify outcome
  });
});
```

**Locator priority** (use the first that works):
1. `getByRole()`, buttons, links, headings, form elements
2. `getByLabel()`, form fields with labels
3. `getByText()`, non-interactive text content
4. `getByPlaceholder()`, inputs with placeholder text
5. `getByTestId()`, when semantic options aren't available

**Assertions**: always web-first:
```typescript
// GOOD: auto-retries
await expect(page.getByRole('heading')).toBeVisible();
await expect(page.getByRole('alert')).toHaveText('Success');

// BAD: no retry
const text = await page.textContent('.msg');
expect(text).toBe('Success');
```

**Never use:**
- `page.waitForTimeout()`
- `page.$(selector)` or `page.$$(selector)`
- Bare CSS selectors unless absolutely necessary
- `page.evaluate()` for things locators can do

**Always include:**
- Descriptive test names that explain the behavior
- Error/edge case tests alongside happy path
- Proper `await` on every Playwright call
- `baseURL`-relative navigation (`page.goto('/')` not `page.goto('http://...')`)

### 5. Match Project Conventions

- If project uses TypeScript → generate `.spec.ts`
- If project uses JavaScript → generate `.spec.js` with `require()` imports
- If project has page objects → use them instead of inline locators
- If project has custom fixtures → import and use them
- If project has a test data directory → create test data files there

### 6. Generate Supporting Files (If Needed)

- **Page object**: If the test touches 5+ unique locators on one page, create a page object
- **Fixture**: If the test needs shared setup (auth, data), create or extend a fixture
- **Test data**: If the test uses structured data, create a JSON file in `test-data/`

### 7. Verify

Run the generated test:

```bash
npx playwright test <generated-file> --reporter=list
```

If it fails:
1. Read the error
2. Fix the test (not the app)
3. Run again
4. If it's an app issue, report it to the user

## Output

- Generated test file(s) with path
- Any supporting files created (page objects, fixtures, data)
- Test run result
- Coverage note: what behaviors are now tested

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

<!-- Adapted for mux from https://github.com/alirezarezvani/claude-skills@19392f7/engineering-team/playwright-pro/skills/generate (MIT License, Copyright (c) 2025 Alireza Rezvani). See THIRD_PARTY_NOTICES.md. -->
