import * as crypto from 'crypto';
import * as fs from 'fs';
import * as vscode from 'vscode';
import { ConnectionState } from '../server/lifecycle';

const GITHUB_URL = 'https://github.com/jchristn/Mux';
const DOCS_URL = 'https://github.com/jchristn/Mux/blob/main/docs/VSCODE.md';

/**
 * A small "About mux" webview: the logo, the extension and (when connected) server versions, and links out to
 * the project on GitHub, the full docs, and the live dashboard for the configuration, monitoring, and
 * management surfaces that are richer in the browser than in a side panel. The dashboard link uses the
 * connected server's own URL so it points at the instance the editor is actually talking to.
 */
export class AboutPanel {
    private static current: vscode.WebviewPanel | undefined;

    /**
     * Opens (or reveals) the About panel.
     *
     * @param context The extension context, used for the extension version and the logo path.
     * @param state The current connection state, used for the server version and dashboard URL.
     */
    public static show(context: vscode.ExtensionContext, state: ConnectionState): void {
        if (AboutPanel.current) {
            AboutPanel.current.reveal();
            AboutPanel.current.webview.html = AboutPanel.render(context, state);
            return;
        }

        const panel = vscode.window.createWebviewPanel('muxAbout', vscode.l10n.t('About mux'), vscode.ViewColumn.Active, {
            enableScripts: true,
        });
        panel.webview.html = AboutPanel.render(context, state);
        panel.webview.onDidReceiveMessage((message: { type: string; url?: string }) => {
            if (message.type === 'open' && message.url) {
                void vscode.env.openExternal(vscode.Uri.parse(message.url));
            }
        });
        panel.onDidDispose(() => {
            AboutPanel.current = undefined;
        });
        AboutPanel.current = panel;
    }

    private static render(context: vscode.ExtensionContext, state: ConnectionState): string {
        const nonce = crypto.randomBytes(16).toString('hex');
        const csp = `default-src 'none'; img-src data:; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';`;
        const version = (context.extension.packageJSON as { version?: string }).version ?? '';
        const logo = AboutPanel.logoDataUri(context);
        const dashboardUrl = state.connected && state.baseUrl ? `${state.baseUrl.replace(/\/+$/, '')}/dashboard` : '';

        const serverLine = state.connected
            ? vscode.l10n.t('Connected to mux {0} (contract {1})', state.productVersion, state.contractVersion)
            : vscode.l10n.t('Not connected to a mux server');

        const dashboardButton = dashboardUrl
            ? `<button class="link" data-url="${AboutPanel.escape(dashboardUrl)}">${AboutPanel.escape(vscode.l10n.t('Open the mux dashboard'))}</button>`
            : `<div class="muted">${AboutPanel.escape(vscode.l10n.t('Connect to a server to open its dashboard.'))}</div>`;

        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta http-equiv="Content-Security-Policy" content="${csp}" />
<style nonce="${nonce}">
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); padding: 28px; text-align: center; }
  img { width: 96px; height: 96px; }
  h1 { font-size: 1.5em; margin: 12px 0 2px; }
  .version { color: var(--vscode-descriptionForeground); margin-bottom: 4px; }
  .server { margin: 12px 0 20px; }
  .muted { color: var(--vscode-descriptionForeground); font-size: 0.9em; }
  .links { display: flex; flex-direction: column; gap: 8px; max-width: 320px; margin: 0 auto; }
  button.link { color: var(--vscode-button-foreground); background: var(--vscode-button-background);
    border: none; border-radius: 4px; padding: 8px 14px; cursor: pointer; font-size: var(--vscode-font-size); }
  button.link.secondary { color: var(--vscode-button-secondaryForeground); background: var(--vscode-button-secondaryBackground); }
  button.link:hover { background: var(--vscode-button-hoverBackground); }
  button.link:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 2px; }
  .tagline { color: var(--vscode-descriptionForeground); margin: 8px auto 22px; max-width: 380px; line-height: 1.5; }
</style>
</head>
<body>
  ${logo ? `<img src="${logo}" alt="mux" />` : ''}
  <h1>mux</h1>
  <div class="version">${AboutPanel.escape(vscode.l10n.t('Extension v{0}', version))}</div>
  <div class="server">${AboutPanel.escape(serverLine)}</div>
  <div class="tagline">${AboutPanel.escape(vscode.l10n.t('A backend-agnostic AI coding agent, in your editor and across the terminal, desktop, and web.'))}</div>
  <div class="links">
    ${dashboardButton}
    <button class="link secondary" data-url="${GITHUB_URL}">${AboutPanel.escape(vscode.l10n.t('mux on GitHub'))}</button>
    <button class="link secondary" data-url="${DOCS_URL}">${AboutPanel.escape(vscode.l10n.t('Extension documentation'))}</button>
  </div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  document.querySelectorAll('button.link').forEach((b) => b.addEventListener('click', () => vscode.postMessage({ type: 'open', url: b.getAttribute('data-url') })));
</script>
</body>
</html>`;
    }

    private static logoDataUri(context: vscode.ExtensionContext): string {
        try {
            const path = vscode.Uri.joinPath(context.extensionUri, 'dashboard', 'media', 'icon.png').fsPath;
            const base64 = fs.readFileSync(path).toString('base64');
            return `data:image/png;base64,${base64}`;
        } catch {
            return '';
        }
    }

    private static escape(text: string): string {
        return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }
}
