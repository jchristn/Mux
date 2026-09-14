# Publishing the mux VS Code extension

This is the operator runbook for getting `src/Mux.VSCode` onto the VS Code Marketplace, and optionally Open
VSX. It assumes no prior experience with either marketplace and calls out the two places that trip people up:
landing on the wrong Microsoft site, and a token that logs in but cannot publish. Follow it top to bottom the
first time; after that, only the short "Publish an update" section at the end matters.

The extension is a normal VS Code extension packaged as a `.vsix`. Two marketplaces distribute it: the **VS
Code Marketplace** (what Microsoft's VS Code installs from) and **Open VSX** (what VSCodium, Cursor, Gitpod,
and other non-Microsoft builds install from). They are independent — separate accounts, separate tokens,
separate publish commands — and you can ship to one without the other. Publish to the VS Code Marketplace
first; add Open VSX whenever you want broader reach.

## Before you start

- **Node.js 20+** installed, and a terminal open at `C:\Code\Mux\src\Mux.VSCode`.
- The extension's Marketplace identity is **`usemux.mux-ai`** — publisher `usemux` (`"publisher"` in
  `package.json`) plus the internal name `mux-ai` (`"name"`), with display name `mux-ai` (`"displayName"`).
  The Marketplace requires **both** the `name` and the `displayName` to be unique across the whole
  Marketplace — plain `mux` is taken for each — which is why both are `mux-ai`. The identity also appears in
  one integration test (`usemux.mux-ai`). If you change any of them, change `package.json` and that test
  together.
- Decide nothing else — versions, categories, and the manifest are already set.

## Part 1 — VS Code Marketplace token

The Marketplace authenticates through **Azure DevOps** (`dev.azure.com`), which is a different product from
the Azure cloud portal (`portal.azure.com`). You need a free Azure DevOps organization and a Personal Access
Token (PAT). No Azure subscription and no credit card are required.

### 1.1 Create an Azure DevOps organization

Signing in at `dev.azure.com` when you have no organization often bounces you to `portal.azure.com` (the Azure
cloud), where there is **no** Personal Access Token option — that is the wrong place. To get into Azure DevOps
proper:

1. Open a **new private/incognito browser window**.
2. Go to **`https://aex.dev.azure.com/me`** and sign in with your Microsoft account.
3. Click **Create new organization**, accept the defaults (any name, any region, pass the captcha). If it
   asks for a first project, name it anything; you will never use it. The org only needs to exist.

You end up at `https://dev.azure.com/joelchristner` (that is your organization). That is the right product.

### 1.2 Create the Personal Access Token

The single most common publishing failure — a token that logs in but returns **`401`** on publish — comes from
getting these two settings wrong. Set them exactly.

1. Go to **`https://dev.azure.com/joelchristner/_usersSettings/tokens`** → **+ New Token**.
2. **Organization:** change the dropdown to **All accessible organizations** (not your specific org).
3. **Expiration:** up to one year.
4. **Scopes:** click **Show all scopes** at the bottom, find **Marketplace**, and check **Manage**.
5. **Create**, then **copy the token immediately** — it is shown once. Store it somewhere safe. This is your
   `VSCE_PAT`.

## Part 2 — Create the publisher

1. Go to **`https://marketplace.visualstudio.com/manage`** and sign in with the **same Microsoft account** you
   used for the token. Using a different account here is the second most common cause of a `401` at publish
   time — the token and the publisher must belong to the same account.
2. Click **Create publisher**.
3. **ID:** `usemux` (must match `package.json`). **Name:** any display name.
4. Save.

## Part 3 — Publish to the VS Code Marketplace

The fastest first publish is manual from your machine. It needs only the token from Part 1.

```
cd C:\Code\Mux\src\Mux.VSCode
npm ci
npm install -g @vscode/vsce
vsce login usemux
```

`vsce login usemux` prompts for the token — paste the `VSCE_PAT` and press Enter. Then:

```
vsce publish
```

This compiles (via the `vscode:prepublish` script), packages the `.vsix`, and uploads it. Warnings about a
missing icon or activation events are fine; they are not errors. When it succeeds the extension is live at:

```
https://marketplace.visualstudio.com/items?itemName=usemux.mux-ai
```

It becomes searchable as "mux" in the VS Code Extensions panel within a few minutes.

## Part 4 — Troubleshooting

- **`Failed request: (401)` on publish, even though `vsce login` worked.** The token lacks the right scope or
  organization, or the publisher belongs to a different account. `vsce login` only checks that the token is a
  valid Azure DevOps token; publishing additionally needs **Marketplace → Manage** and **Organization = All
  accessible organizations**. Recreate the token with both (Part 1.2), run `vsce login usemux` again, and
  republish. If it still fails, confirm at `marketplace.visualstudio.com/manage` that the publisher `usemux`
  is owned by the same Microsoft account that issued the token.
- **"The license is not attached" or a license warning.** The MIT license ships as `LICENSE.md` inside the
  `.vsix` (you can see it in the file list `vsce` prints before uploading). No action is needed. If you want
  the Marketplace page to show a License tab explicitly, that is already satisfied by `LICENSE.md` plus
  `"license": "MIT"` in `package.json`.
- **`The extension '<name>' already exists in the Marketplace`.** The `name` field must be unique across the
  entire Marketplace, not just within your publisher. Change `"name"` in `package.json` to something unique.
- **`This extension display name is taken`.** The `displayName` must **also** be unique across the whole
  Marketplace. Change `"displayName"` too. (Plain `mux` is taken for both `name` and `displayName`, so the
  extension uses `mux-ai` for each.) Update the id in the integration test to match.
- **`ERROR  Missing publisher name`.** `package.json` has no `publisher`, or you ran from the wrong folder.
  Run from `src/Mux.VSCode`.
- **Publish rejected because the version already exists.** Each publish needs a higher version than the last.
  Bump `"version"` in `package.json` (or run `vsce publish patch`).
- **No icon on the listing.** Optional. Add a 128×128 PNG under `dashboard/media/` and an `"icon"` field to
  `package.json`, then publish an update.

## Part 5 — Open VSX (optional, for VSCodium / Cursor / Gitpod)

Open VSX is a separate registry with its own account and token. Skip this if you only want the VS Code
Marketplace.

1. Go to **`https://open-vsx.org`**, **Log in with GitHub**.
2. Open your avatar → **Settings → Namespaces** and **sign the Eclipse publisher agreement** (a one-time
   click; you cannot publish until you accept it).
3. Avatar → **Settings → Access Tokens → Generate New Token**, and copy it — this is your `OVSX_PAT`.
4. Publish from `src/Mux.VSCode`:
   ```
   npm install -g ovsx
   ovsx create-namespace usemux -p <OVSX_PAT>
   ovsx publish -p <OVSX_PAT>
   ```
   The namespace only needs to be created once; later publishes are just `ovsx publish -p <OVSX_PAT>`.

## Part 6 — Automated publishing from CI (optional)

The repository ships `.github/workflows/vscode-extension.yml`, which builds, checks, packages, and — on a tag
matching `vscode-v*` — publishes to **both** marketplaces and attaches a checksummed `.vsix` to a GitHub
Release. Once the accounts above exist, this replaces the manual commands.

1. In the GitHub repo, go to **Settings → Secrets and variables → Actions → New repository secret** and add
   two: `VSCE_PAT` (Part 1) and `OVSX_PAT` (Part 5).
2. Tag and push a release:
   ```
   git tag vscode-v0.1.0
   git push origin vscode-v0.1.0
   ```
3. Watch **Actions → vscode-extension**. The `publish` job runs only on that tag.

Note: the workflow publishes to both marketplaces, so the Open VSX step fails if `OVSX_PAT` is not set. If you
want the VS Code Marketplace only, either set `OVSX_PAT` anyway or remove/guard the Open VSX step in the
workflow.

## Publish an update

Once the accounts and publisher exist, shipping a new version is short:

1. Make your changes and bump `"version"` in `src/Mux.VSCode/package.json` (for example `0.1.0` → `0.1.1`).
2. Manual: `vsce publish` (and `ovsx publish -p <OVSX_PAT>` if you use Open VSX). Or CI: push a new
   `vscode-v0.1.1` tag.
3. Updates appear on the Marketplace within a few minutes; installed copies update automatically.
