import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vm from 'node:vm';
import { test } from 'node:test';

for (const provider of ['ollama-cloud', 'xai']) {
test(`${provider}: VS Code form shows its key and saves native bearer settings`, async () => {
    let fields: any[] = [];
    let saved: any[] = [];
    const formResult = { Name: 'cloud', AdapterType: provider, BaseUrl: '', Model: 'deepseek-v4.1-flash', ApiKey: '${OLLAMA_API_KEY}', AuthPlacement: 'query', AuthParameterName: 'stale' };
    const sandbox: any = {
        exports: {},
        require: (name: string) => {
            if (name === 'vscode') return {
                l10n: { t: (value: string) => value },
                CancellationTokenSource: class { token = {}; },
                window: { showInformationMessage: () => {}, showErrorMessage: (message: string) => { throw new Error(message); } },
            };
            if (name === './FormPanel') return { FormPanel: { show: async (_title: string, value: any[]) => { fields = value; return formResult; } } };
            if (name === '../util/logger') return { logError: () => {} };
            return {};
        },
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../src/manage/actions.js'), 'utf8'), sandbox);
    const lifecycle = { getClient: async () => ({ getEndpointDetails: async () => [], putEndpoints: async (value: any[]) => { saved = value; } }) };
    await new sandbox.exports.ManageActions(lifecycle, () => {}).addEndpoint();
    assert.ok(fields.find(f => f.key === 'AdapterType').options.includes(provider));
    assert.ok(fields.find(f => f.key === 'ApiKey').showIf[0].anyOf.includes(provider));
    assert.ok(!fields.find(f => f.key === 'AuthPlacement').showIf[0].anyOf.includes(provider));
    assert.equal(saved.length, 1);
    assert.equal(saved[0].AdapterType, provider);
    assert.equal(saved[0].BaseUrl, provider === 'xai' ? 'https://api.x.ai/v1' : 'https://ollama.com');
    assert.equal(saved[0].AuthPlacement, 'bearer');
    assert.equal(saved[0].AuthParameterName, '');
    assert.equal(saved[0].ApiKey, '${OLLAMA_API_KEY}');
});

test(`${provider}: web form saves its native URL and bearer scheme`, () => {
    const source = fs.readFileSync(path.join(__dirname, '../../../../Mux.Server/DashboardPage.cs'), 'utf8');
    let fields: any[] = [];
    let saved: any[] = [];
    const sandbox: any = {
        _ep: [], t: (value: string) => value,
        formModal: (_title: string, value: any[], _initial: any, submit: (values: any) => void) => {
            fields = value;
            submit({ Name: 'cloud', AdapterType: provider, Model: 'deepseek-v4.1-flash', ApiKey: '${OLLAMA_API_KEY}', BaseUrl: '', AuthPlacement: 'query', AuthParameterName: 'stale' });
        },
        saveCollection: (_url: string, value: any[]) => { saved = value; },
    };
    vm.runInNewContext(source.slice(source.indexOf('function epIsOpenAiFamily('), source.indexOf('function epIndexByName(')), sandbox);
    sandbox.openEp(-1);
    assert.ok(fields.find(f => f.id === 'AdapterType').options.includes(provider));
    assert.ok(fields.find(f => f.id === 'ApiKey').showIf({ AdapterType: provider }));
    assert.ok(!fields.find(f => f.id === 'AuthPlacement').showIf({ AdapterType: provider }));
    assert.equal(saved[0].BaseUrl, provider === 'xai' ? 'https://api.x.ai/v1' : 'https://ollama.com');
    assert.equal(saved[0].AuthPlacement, 'bearer');
    assert.equal(saved[0].AuthParameterName, '');
    assert.equal(saved[0].ApiKey, '${OLLAMA_API_KEY}');
});

}
