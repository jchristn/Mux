import * as crypto from 'crypto';
import * as vscode from 'vscode';

/**
 * One clause of a field's visibility rule: the field is shown only when the current value of the control named
 * {@link key} is one of {@link anyOf}. Multiple clauses on a field are ANDed together. Clauses are declarative
 * (not functions) so they serialize into the webview, which re-evaluates them live as other controls change.
 */
export interface ShowIfClause {
    /** The key of the control this clause depends on. */
    key: string;

    /** The values of that control for which the field is shown. */
    anyOf: string[];
}

/** A single field in a {@link FormPanel}. */
export interface FormField {
    /** The value key returned in the result object. */
    key: string;

    /** The visible label. */
    label: string;

    /** The control type. */
    type: 'text' | 'password' | 'number' | 'checkbox' | 'select' | 'textarea';

    /** The initial value. */
    value?: string | number | boolean;

    /** Options for a `select` control. */
    options?: string[];

    /** Help text shown under the control. */
    hint?: string;

    /** When true, an empty value is rejected on submit. */
    required?: boolean;

    /** Optional visibility rule; when present the field is shown only while every clause matches. */
    showIf?: ShowIfClause[];
}

/**
 * A generic modal webview form. It renders a titled set of fields (text, password, number, checkbox, select,
 * textarea) using VS Code theme tokens, and resolves to the submitted values or `undefined` when cancelled.
 * The extension uses it for the endpoint, MCP-server, and settings editors so those surfaces get real forms
 * rather than a chain of input boxes. Dependency-free vanilla script; nothing is sent anywhere until the user
 * clicks Save, and the panel closes itself on submit or cancel.
 */
export class FormPanel {
    /**
     * Shows the form and resolves with the field values, or undefined if cancelled.
     *
     * @param title The panel title.
     * @param fields The fields to render.
     * @returns The submitted values keyed by field key, or undefined.
     */
    public static show(title: string, fields: FormField[]): Promise<Record<string, string | boolean> | undefined> {
        const panel = vscode.window.createWebviewPanel('muxForm', title, vscode.ViewColumn.Active, {
            enableScripts: true,
            retainContextWhenHidden: true,
        });

        panel.webview.html = FormPanel.render(title, fields);

        return new Promise((resolve) => {
            let settled = false;
            const sub = panel.webview.onDidReceiveMessage((message: { type: string; values?: Record<string, string | boolean> }) => {
                if (message.type === 'submit') {
                    settled = true;
                    resolve(message.values ?? {});
                    panel.dispose();
                } else if (message.type === 'cancel') {
                    settled = true;
                    resolve(undefined);
                    panel.dispose();
                }
            });

            panel.onDidDispose(() => {
                sub.dispose();
                if (!settled) {
                    resolve(undefined);
                }
            });
        });
    }

    private static render(title: string, fields: FormField[]): string {
        const nonce = crypto.randomBytes(16).toString('hex');
        const csp = `default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}';`;
        const controls = fields.map((f) => FormPanel.control(f)).join('\n');
        const fieldsJson = JSON.stringify(fields.map((f) => ({ key: f.key, type: f.type, showIf: f.showIf ?? null })));

        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta http-equiv="Content-Security-Policy" content="${csp}" />
<style nonce="${nonce}">
  body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); padding: 16px; }
  h1 { font-size: 1.2em; margin: 0 0 16px; }
  .field { margin-bottom: 14px; }
  label { display: block; margin-bottom: 4px; font-weight: 600; }
  .hint { color: var(--vscode-descriptionForeground); font-size: 0.9em; margin-top: 3px; }
  input[type=text], input[type=password], input[type=number], select, textarea {
    width: 100%; box-sizing: border-box; padding: 6px;
    color: var(--vscode-input-foreground); background: var(--vscode-input-background);
    border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 4px;
    font-family: var(--vscode-font-family); font-size: var(--vscode-font-size);
  }
  textarea { min-height: 120px; resize: vertical; font-family: var(--vscode-editor-font-family); }
  .check { display: flex; align-items: center; gap: 8px; }
  .check input { width: auto; }
  .actions { margin-top: 20px; display: flex; gap: 8px; }
  button { color: var(--vscode-button-foreground); background: var(--vscode-button-background);
    border: none; border-radius: 4px; padding: 6px 16px; cursor: pointer; font-size: var(--vscode-font-size); }
  button.secondary { color: var(--vscode-button-secondaryForeground); background: var(--vscode-button-secondaryBackground); }
  button:hover { background: var(--vscode-button-hoverBackground); }
  button:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 2px; }
  .error { color: var(--vscode-errorForeground); margin-top: 10px; min-height: 18px; }
</style>
</head>
<body>
<h1>${FormPanel.escape(title)}</h1>
<form id="form">
${controls}
<div class="error" id="error" role="alert"></div>
<div class="actions">
  <button type="submit" id="save">Save</button>
  <button type="button" class="secondary" id="cancel">Cancel</button>
</div>
</form>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  const fields = ${fieldsJson};
  const required = ${JSON.stringify(fields.filter((f) => f.required).map((f) => f.key))};
  function currentValue(key) {
    const el = document.getElementById('f_' + key);
    if (!el) return '';
    const f = fields.find((x) => x.key === key);
    return (f && f.type === 'checkbox') ? el.checked : el.value;
  }
  // A field is visible only when every showIf clause matches the current value of the control it names.
  function isVisible(f) {
    if (!f.showIf) return true;
    for (const clause of f.showIf) {
      if (clause.anyOf.indexOf(String(currentValue(clause.key))) < 0) return false;
    }
    return true;
  }
  function applyVisibility() {
    for (const f of fields) {
      const wrap = document.getElementById('field_' + f.key);
      if (wrap) wrap.style.display = isVisible(f) ? '' : 'none';
    }
  }
  // Re-evaluate visibility whenever any control changes, and once on load.
  for (const f of fields) {
    const el = document.getElementById('f_' + f.key);
    if (el) { el.addEventListener('change', applyVisibility); el.addEventListener('input', applyVisibility); }
  }
  applyVisibility();
  document.getElementById('cancel').addEventListener('click', () => vscode.postMessage({ type: 'cancel' }));
  document.getElementById('form').addEventListener('submit', (e) => {
    e.preventDefault();
    const values = {};
    for (const f of fields) {
      const el = document.getElementById('f_' + f.key);
      if (!el) continue;
      values[f.key] = f.type === 'checkbox' ? el.checked : el.value;
    }
    for (const key of required) {
      const rf = fields.find((x) => x.key === key);
      if (rf && !isVisible(rf)) continue;
      if (!String(values[key] || '').trim()) {
        document.getElementById('error').textContent = 'Please fill in all required fields.';
        return;
      }
    }
    vscode.postMessage({ type: 'submit', values });
  });
</script>
</body>
</html>`;
    }

    private static control(field: FormField): string {
        const id = `f_${field.key}`;
        const label = `<label for="${id}">${FormPanel.escape(field.label)}${field.required ? ' *' : ''}</label>`;
        const hint = field.hint ? `<div class="hint">${FormPanel.escape(field.hint)}</div>` : '';

        const wrapId = `field_${field.key}`;
        let control: string;
        switch (field.type) {
            case 'checkbox':
                return `<div class="field" id="${wrapId}"><div class="check"><input type="checkbox" id="${id}" ${field.value ? 'checked' : ''} /><label for="${id}" style="margin:0">${FormPanel.escape(field.label)}</label></div>${hint}</div>`;
            case 'select':
                control = `<select id="${id}">${(field.options ?? [])
                    .map((o) => `<option value="${FormPanel.escape(o)}" ${String(field.value) === o ? 'selected' : ''}>${FormPanel.escape(o)}</option>`)
                    .join('')}</select>`;
                break;
            case 'textarea':
                control = `<textarea id="${id}">${FormPanel.escape(String(field.value ?? ''))}</textarea>`;
                break;
            case 'number':
                control = `<input type="number" id="${id}" value="${FormPanel.escape(String(field.value ?? ''))}" />`;
                break;
            case 'password':
                control = `<input type="password" id="${id}" value="" placeholder="${field.value ? '••••••• (unchanged)' : ''}" />`;
                break;
            default:
                control = `<input type="text" id="${id}" value="${FormPanel.escape(String(field.value ?? ''))}" />`;
                break;
        }

        return `<div class="field" id="${wrapId}">${label}${control}${hint}</div>`;
    }

    private static escape(text: string): string {
        return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }
}
