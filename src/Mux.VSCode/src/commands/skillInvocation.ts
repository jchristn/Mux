/**
 * Pure helpers for invoking mux skills by name from editor commands. Kept free of `vscode` so they can be unit
 * tested under plain Node.
 */

/**
 * Builds the slash invocation that reviews one file with the code-review skill. Backslashes become forward
 * slashes, and a path containing whitespace is quoted so the skill receives it as one argument.
 *
 * @param relativePath The file path relative to the workspace root.
 * @returns The slash text, for example `/code-review file src/app.ts`.
 */
export function reviewFileInvocation(relativePath: string): string {
    const path = relativePath.replace(/\\/g, '/');
    return `/code-review file ${/\s/.test(path) ? `"${path}"` : path}`;
}
