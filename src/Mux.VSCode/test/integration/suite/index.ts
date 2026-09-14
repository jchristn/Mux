import * as path from 'path';
import Mocha from 'mocha';
import { glob } from 'glob';

/**
 * The extension-host test entry point. VS Code loads this inside a running editor and awaits the returned
 * promise; a rejection fails the run with a non-zero exit code.
 */
export async function run(): Promise<void> {
    const mocha = new Mocha({ ui: 'tdd', color: true, timeout: 20000 });
    const testsRoot = path.resolve(__dirname);
    const files = await glob('**/*.test.js', { cwd: testsRoot });
    for (const file of files) {
        mocha.addFile(path.resolve(testsRoot, file));
    }

    await new Promise<void>((resolve, reject) => {
        mocha.run((failures) => {
            if (failures > 0) {
                reject(new Error(`${failures} integration test(s) failed.`));
            } else {
                resolve();
            }
        });
    });
}
