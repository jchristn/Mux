/**
 * Pure helpers for the copy-to-clipboard control in {@link FormPanel} text areas. Kept free of `vscode` so they
 * can be unit tested under plain Node.
 */

/**
 * Escapes text for an HTML attribute or element body.
 *
 * @param text The text to escape.
 * @returns The escaped text.
 */
export function escapeHtml(text: string): string {
    return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/**
 * Builds the copy button placed beside a copyable field's label. The webview script copies the current value
 * of the control named by `data-copy` (unsaved edits included) when it is clicked.
 *
 * @param controlId The id of the control whose value is copied.
 * @param label The field label, used in the accessible name.
 * @returns The button markup.
 */
export function copyButtonHtml(controlId: string, label: string): string {
    const name = escapeHtml(`Copy ${label} to the clipboard`);
    return `<button type="button" class="copy secondary" data-copy="${escapeHtml(controlId)}" title="${name}" aria-label="${name}">⧉ Copy</button>`;
}
