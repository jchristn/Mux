import * as assert from 'assert';
import * as vscode from 'vscode';

/**
 * Extension-host integration tests. These run inside a real VS Code, so they verify what unit tests cannot:
 * that the extension activates and contributes its commands and views. Server-dependent behavior (a streamed
 * turn, approvals) is exercised against a stub server in a follow-up harness; those are marked pending here
 * so the gap is visible rather than silent.
 */
suite('mux extension', () => {
    test('activates', async () => {
        const extension = vscode.extensions.getExtension('usemux.mux-ai');
        assert.ok(extension, 'extension is present');
        await extension!.activate();
        assert.strictEqual(extension!.isActive, true);
    });

    test('contributes its commands', async () => {
        const commands = await vscode.commands.getCommands(true);
        const expected = [
            'mux.newConversation',
            'mux.explainSelection',
            'mux.fixDiagnostic',
            'mux.selectEndpoint',
            'mux.sessions.refresh',
            'mux.sessions.delete',
        ];
        for (const command of expected) {
            assert.ok(commands.includes(command), `command ${command} is registered`);
        }
    });

    test.skip('streams a turn against a stub server (pending stub-server harness)', () => {
        // Requires a local stub implementing /v1.0/api/chat/stream; tracked as a follow-up.
    });
});
