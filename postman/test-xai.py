"""Run real Mux against the local xAI simulation; no provider credential required.
Start postman/simulations/xai.sim.yaml on port 4912 first, then run this script.
"""
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / 'src/Mux.Cli/bin/Debug/net8.0/Mux.Cli.dll'
BASE = os.environ.get('MUX_TEST_XAI_URL', 'http://127.0.0.1:4912/xai')

with tempfile.TemporaryDirectory(prefix='mux-xai-contract-') as directory:
    root = Path(directory)
    config = root / 'config'
    config.mkdir()
    work = root / 'work'
    work.mkdir()
    (work / '.console-smoke.txt').write_text('MUX_XAI_TOOL_OK\n')
    (config / 'endpoints.json').write_text(json.dumps({'endpoints': [{
        'name': 'grok', 'adapterType': 'xai', 'baseUrl': BASE, 'model': 'test-model',
        'apiKey': '${XAI_API_KEY}', 'isDefault': True, 'showThinking': True,
    }]}))
    env = dict(os.environ, MUX_CONFIG_DIR=str(config), XAI_API_KEY='fixture-token')

    def cli(*arguments):
        result = subprocess.run(['dotnet', str(DLL), *arguments], cwd=work, env=env,
                                capture_output=True, text=True, timeout=30)
        return result, json.loads(result.stdout)

    result, reply = cli('-p', 'mux-xai-smoke', '--output-format', 'json', '--yolo')
    assert result.returncode == 0 and reply['toolCallCount'] == 2, (result.stderr, reply)
    assert 'MUX_XAI_TOOL_OK' in reply['result'] and reply['usage']['reasoningTokens'] > 0
    print('PASS: streamed tool loop, encrypted reasoning replay, call IDs, token usage')

    result, models = cli('endpoint', 'models', 'grok', '--output-format', 'json')
    assert result.returncode == 0 and 'test-model' in json.dumps(models), (result.stderr, models)
    print('PASS: authenticated model discovery')

    result, reply = cli('-p', 'mux-xai-truncated', '--output-format', 'json', '--yolo')
    assert result.returncode != 0 and reply['toolCallCount'] == 0 and reply['status'] == 'failed', reply
    print('PASS: incomplete native stream fails without executing tools')

    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        port = probe.getsockname()[1]
    with (root / 'server.log').open('w') as log:
        server = subprocess.Popen(['dotnet', str(DLL), 'serve', '--host', '127.0.0.1',
                                   '--port', str(port), '--no-auth', '--allow-tools'],
                                  cwd=work, env=env, stdout=log, stderr=log)
        try:
            url = f'http://127.0.0.1:{port}'
            for _ in range(100):
                if server.poll() is not None:
                    raise AssertionError((root / 'server.log').read_text())
                try:
                    urllib.request.urlopen(url + '/v1.0/api/health', timeout=1).close()
                    break
                except OSError:
                    time.sleep(0.1)
            else:
                raise AssertionError('Mux server did not become ready')

            def turn(messages):
                request = urllib.request.Request(url + '/v1.0/api/chat/stream', method='POST',
                    headers={'Content-Type': 'application/json'},
                    data=json.dumps({'endpoint': 'grok', 'id': 'xai-resume-test',
                                     'workingDirectory': str(work), 'messages': messages}).encode())
                return urllib.request.urlopen(request, timeout=30).read().decode()

            first = turn([{'role': 'user', 'content': 'mux-xai-smoke'}])
            assert 'MUX_XAI_TOOL_OK' in first and 'event: error' not in first, first
            # A display client need not send encrypted output or intermediate tool messages.
            second = turn([{'role': 'user', 'content': 'mux-xai-smoke'},
                           {'role': 'assistant', 'content': 'Actual file: MUX_XAI_TOOL_OK'},
                           {'role': 'user', 'content': 'mux-xai-reload'}])
            assert 'MUX_XAI_TOOL_OK' in second and 'event: error' not in second, second
            print('PASS: web/VS Code session resume restores authoritative native history')
        finally:
            server.terminate()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait()
