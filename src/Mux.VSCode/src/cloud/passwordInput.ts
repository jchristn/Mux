import * as vscode from 'vscode';

/** Options for {@link promptPassword}. */
export interface PasswordPromptOptions {
    /** The input box title. */
    title: string;

    /** The prompt line under the field. */
    prompt: string;

    /** Returns an error message for an invalid value, or undefined when it is valid. */
    validate: (value: string) => string | undefined;
}

/**
 * Asks for a password in a masked input box with an eye button in its title bar that shows or hides what was
 * typed. `showInputBox` has no such button, so this uses `createInputBox`.
 *
 * @param options The title, prompt, and validation.
 * @returns The entered password, or undefined when the box was dismissed.
 */
export function promptPassword(options: PasswordPromptOptions): Promise<string | undefined> {
    const showButton: vscode.QuickInputButton = { iconPath: new vscode.ThemeIcon('eye'), tooltip: vscode.l10n.t('Show password') };
    const hideButton: vscode.QuickInputButton = { iconPath: new vscode.ThemeIcon('eye-closed'), tooltip: vscode.l10n.t('Hide password') };
    const input = vscode.window.createInputBox();
    input.title = options.title;
    input.prompt = options.prompt;
    input.password = true;
    input.ignoreFocusOut = true;
    input.buttons = [showButton];

    return new Promise<string | undefined>((resolve) => {
        let settled = false;
        const finish = (value: string | undefined): void => {
            if (settled) {
                return;
            }

            settled = true;
            resolve(value);
            input.dispose();
        };

        input.onDidTriggerButton((button) => {
            input.password = button !== showButton;
            input.buttons = [input.password ? showButton : hideButton];
        });
        input.onDidChangeValue((value) => {
            input.validationMessage = undefined;
            if (value) {
                input.validationMessage = options.validate(value);
            }
        });
        input.onDidAccept(() => {
            const message = options.validate(input.value);
            if (message) {
                input.validationMessage = message;
                return;
            }

            finish(input.value);
        });
        input.onDidHide(() => finish(undefined));
        input.show();
    });
}
