import * as path from 'path';
import { runTests } from '@vscode/test-electron';

/**
 * Downloads a VS Code build and runs the extension-host integration suite inside it. Invoked by
 * `npm run test:integration`; needs network access to fetch VS Code the first time.
 */
async function main(): Promise<void> {
    try {
        const extensionDevelopmentPath = path.resolve(__dirname, '../../../');
        const extensionTestsPath = path.resolve(__dirname, './suite/index');
        await runTests({ extensionDevelopmentPath, extensionTestsPath });
    } catch (error) {
        console.error('Integration tests failed to run.', error);
        process.exit(1);
    }
}

void main();
