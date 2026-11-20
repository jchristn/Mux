/**
 * Pure helpers for the show/hide toggle on password and key fields in {@link FormPanel} webviews. Kept free of
 * `vscode` so they can be unit tested under plain Node; the extension passes in the translated labels.
 */
import { escapeHtml } from './formCopy';

/** The translated accessible names for the toggle in each state. */
export interface RevealLabels {
    /** Shown while the value is masked (the button reveals it). */
    show: string;

    /** Shown while the value is visible (the button masks it again). */
    hide: string;
}

/** Eye icon: the value is masked; clicking shows it. */
export const REVEAL_ICON_SHOW =
    '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7S1 12 1 12Z"/><circle cx="12" cy="12" r="3"/></svg>';

/** Eye-off icon: the value is visible; clicking masks it. */
export const REVEAL_ICON_HIDE =
    '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9.9 4.24A9.1 9.1 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19"/><path d="M6.61 6.61A13.5 13.5 0 0 0 1 12s4 8 11 8a9.7 9.7 0 0 0 5.39-1.61"/><path d="M14.12 14.12a3 3 0 1 1-4.24-4.24"/><path d="M1 1l22 22"/></svg>';

/**
 * Builds a masked input with an eye button inside its end edge. The webview script ({@link REVEAL_SCRIPT})
 * flips the input between password and text and swaps the icon and label.
 *
 * @param id The input's id.
 * @param placeholder The placeholder text (empty for none).
 * @param labels The translated button labels.
 * @returns The control markup.
 */
export function passwordControlHtml(id: string, placeholder: string, labels: RevealLabels): string {
    const show = escapeHtml(labels.show);
    const hide = escapeHtml(labels.hide);
    return `<div class="pwwrap"><input type="password" id="${escapeHtml(id)}" value="" placeholder="${escapeHtml(placeholder)}" autocomplete="off" spellcheck="false" />`
        + `<button type="button" class="pwreveal" data-show="${show}" data-hide="${hide}" title="${show}" aria-label="${show}">${REVEAL_ICON_SHOW}</button></div>`;
}

/** Styles for {@link passwordControlHtml}; the button sits inside the field and the text never runs under it. */
export const REVEAL_CSS = `
  .pwwrap { position: relative; }
  .pwwrap input { padding-inline-end: 34px; }
  .pwreveal { position: absolute; inset-inline-end: 3px; top: 50%; transform: translateY(-50%);
    width: 26px; height: 24px; padding: 0; display: inline-flex; align-items: center; justify-content: center;
    background: none; color: var(--vscode-icon-foreground, var(--vscode-foreground)); border: none; border-radius: 4px; cursor: pointer; opacity: .8; }
  .pwreveal:hover { opacity: 1; background: var(--vscode-toolbar-hoverBackground, rgba(128,128,128,.2)); }
  .pwreveal:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 0; }
  .pwreveal svg { display: block; }
`;

/** Webview script for {@link passwordControlHtml}: keeps focus and the caret in the field while toggling. */
export const REVEAL_SCRIPT = `
  const REVEAL_ICONS = { show: ${JSON.stringify(REVEAL_ICON_SHOW)}, hide: ${JSON.stringify(REVEAL_ICON_HIDE)} };
  for (const button of document.querySelectorAll('button.pwreveal')) {
    button.addEventListener('mousedown', (e) => e.preventDefault());
    button.addEventListener('click', () => {
      const input = button.parentElement.querySelector('input');
      if (!input) return;
      const reveal = input.type === 'password';
      input.type = reveal ? 'text' : 'password';
      const label = button.getAttribute(reveal ? 'data-hide' : 'data-show');
      button.title = label;
      button.setAttribute('aria-label', label);
      button.innerHTML = reveal ? REVEAL_ICONS.hide : REVEAL_ICONS.show;
    });
  }
`;
