"""Exercise macOS entry points with isolated apps and fake platform/build tools."""
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
with tempfile.TemporaryDirectory(prefix='mux macos ') as temp:
    temp = Path(temp)
    repo = temp / 'repo'
    shutil.copytree(root / 'scripts', repo / 'scripts')
    for component in ['Desktop', 'Agent']:
        project = repo / f'src/Mux.{component}/Mux.{component}.csproj'
        project.parent.mkdir(parents=True)
        shutil.copy(root / project.relative_to(repo), project)
    icon = repo / 'assets/icons/icon-macos.icns'
    icon.parent.mkdir(parents=True)
    shutil.copy(root / 'assets/icons/icon-macos.icns', icon)
    bin_dir = temp / 'bin'
    bin_dir.mkdir()
    log = temp / 'calls.jsonl'
    tool = '''#!/usr/bin/env python3
import json, os, pathlib, sys
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
with open(os.environ['CALL_LOG'], 'a') as f: f.write(json.dumps([name, *args]) + '\\n')
if name == 'uname': print('Darwin' if args == ['-s'] else os.environ.get('TEST_ARCH', 'arm64'))
if name == 'codesign' and os.environ.get('FAIL_SIGN'): sys.exit(8)
if name == 'dotnet' and args == ['--list-sdks']: print('10.0.401 [mock]')
if name == 'dotnet' and args[0] == 'publish':
    if os.environ.get('FAIL_PUBLISH'): sys.exit(7)
    out = pathlib.Path(args[args.index('--output') + 1])
    out.mkdir(parents=True, exist_ok=True)
    (out / pathlib.Path(args[1]).stem).write_text('fake executable')
    (out / '.playwright').mkdir()
    (out / '.playwright/driver').write_text('driver')
'''
    for name in ['dotnet', 'uname', 'codesign', 'open', 'mux', 'launchctl']:
        path = bin_dir / name
        path.write_text(tool)
        path.chmod(0o755)
    env = dict(os.environ, PATH=f'{bin_dir}:{os.environ["PATH"]}',
               HOME=str(temp / 'home'), CALL_LOG=str(log))
    env.pop('MUX_TFM', None)

    def run(script, tfm='net10.0', **updates):
        log.unlink(missing_ok=True)
        result = subprocess.run(['bash', str(repo / f'scripts/macos/{script}.sh'), tfm],
                                env=dict(env, **updates), capture_output=True, text=True)
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        return result, calls

    for script, components in [('install-tool', ['desktop', 'agent']),
                                ('reinstall-tool', ['desktop', 'agent']),
                                ('run-desktop', ['desktop']), ('run-agent', ['agent'])]:
        for tfm, arch in [('net10.0', 'arm64'), ('net8.0', 'x86_64')]:
            result, calls = run(script, tfm, TEST_ARCH=arch)
            assert result.returncode == 0, result.stderr
            publishes = [c for c in calls if c[:2] == ['dotnet', 'publish']]
            assert len(publishes) == len(components), calls
            for component, publish in zip(components, publishes):
                name = 'Mux' if component == 'desktop' else 'Mux Agent'
                executable = 'Mux.Desktop' if component == 'desktop' else 'Mux.Agent'
                app = temp / f'home/Applications/{name}.app'
                with (app / 'Contents/Info.plist').open('rb') as f:
                    plist = plistlib.load(f)
                assert plist['CFBundleExecutable'] == executable
                assert plist['CFBundlePackageType'] == 'APPL'
                assert plist.get('LSUIElement', False) == (component == 'agent')
                assert (app / f'Contents/MacOS/{executable}').is_file()
                assert (app / 'Contents/Resources/mux.icns').is_file()
                assert (app / 'Contents/MacOS/.playwright').is_symlink()
                assert (app / 'Contents/MacOS/.playwright/driver').read_text() == 'driver'
                assert publish[publish.index('--framework') + 1] == tfm
                assert publish[publish.index('--runtime') + 1] == ('osx-arm64' if arch == 'arm64' else 'osx-x64')
                assert publish[publish.index('--self-contained') + 1] == 'true'
            opens = [c for c in calls if c[0] == 'open']
            assert len(opens) == int(script.startswith('run-')), calls
            if opens:
                assert opens[0] == ['open', '-a', str(app)]
    result, calls = run('run-at-startup')
    assert result.returncode == 0, result.stderr
    with (temp / 'home/Library/LaunchAgents/com.jchristn.mux.agent.plist').open('rb') as f:
        assert plistlib.load(f)['ProgramArguments'] == [str(temp / 'home/Applications/Mux Agent.app/Contents/MacOS/Mux.Agent')]
    # A failed publish must preserve the previous installation and never launch it.
    marker = temp / 'home/Applications/Mux.app/keep-me'
    marker.write_text('installed version')
    for failure in ['FAIL_PUBLISH', 'FAIL_SIGN']:
        result, calls = run('run-desktop', **{failure: '1'})
        assert result.returncode != 0 and marker.exists()
        assert not any(c[0] == 'open' for c in calls)
    print('macOS install/reinstall/run: both architectures and frameworks passed; failed publish preserves installed app.')
