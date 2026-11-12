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
if name == 'pgrep': sys.exit(0 if os.environ.get('MOCK_RUNNING') else 1)
if name == 'launchctl' and args[:1] == ['print']: sys.exit(1)
if name == 'codesign' and os.environ.get('FAIL_SIGN'): sys.exit(8)
if name == 'sudo':
    # A fake sudo: run the command when SUDO_OK is set (like an admin who entered a password), else refuse.
    if not os.environ.get('SUDO_OK'): sys.exit(1)
    import subprocess as sp
    sys.exit(sp.call(args))
if name == 'dotnet' and args == ['--list-sdks']: print('10.0.401 [mock]')
if name == 'dotnet' and args[0] == 'publish':
    if os.environ.get('FAIL_PUBLISH'): sys.exit(7)
    out = pathlib.Path(args[args.index('--output') + 1])
    out.mkdir(parents=True, exist_ok=True)
    (out / pathlib.Path(args[1]).stem).write_text('fake executable')
    (out / '.playwright').mkdir()
    (out / '.playwright/driver').write_text('driver')
'''
    for name in ['dotnet', 'uname', 'codesign', 'open', 'mux', 'launchctl', 'pgrep', 'sudo']:
        path = bin_dir / name
        path.write_text(tool)
        path.chmod(0o755)
    system = temp / 'system-apps'
    system.mkdir()
    env = dict(os.environ, PATH=f'{bin_dir}:{os.environ["PATH"]}',
               HOME=str(temp / 'home'), CALL_LOG=str(log), MUX_SYSTEM_APPLICATIONS_DIR=str(system))
    env.pop('MUX_SYSTEM_INSTALL', None)
    env.pop('MUX_TFM', None)

    def run(script, tfm='net10.0', **updates):
        log.unlink(missing_ok=True)
        result = subprocess.run(['bash', str(repo / f'scripts/macos/{script}.sh'), *([tfm] if tfm else [])],
                                env=dict(env, **updates), capture_output=True, text=True)
        calls = [json.loads(line) for line in log.read_text().splitlines()] if log.exists() else []
        return result, calls

    for script, components in [('install-tool', ['desktop', 'agent']),
                                ('reinstall-tool', ['desktop', 'agent']),
                                ('run-desktop', ['desktop']), ('run-agent', ['agent'])]:
        for tfm, arch in [('net10.0', 'arm64'), ('net8.0', 'x86_64')]:
            for leftover in system.iterdir():
                shutil.rmtree(leftover)
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
            # install and reinstall also put a copy in the system folder; the run scripts never do.
            for component in components:
                name = 'Mux' if component == 'desktop' else 'Mux Agent'
                executable = 'Mux.Desktop' if component == 'desktop' else 'Mux.Agent'
                installed = (system / f'{name}.app/Contents/MacOS/{executable}').is_file()
                assert installed == script.endswith('-tool'), (script, name, installed)
            opens = [c for c in calls if c[0] == 'open']
            assert len(opens) == int(script.startswith('run-')), calls
            if opens:
                env_args = ['--env', 'MUX_AGENT_AUTOSTART=0'] if script == 'run-agent' else []
                assert opens[0] == ['open', *env_args, '-a', str(app)], opens
    # A system folder the user cannot write: sudo is used; when it fails the per-user install still succeeds
    # with a warning, and MUX_SYSTEM_INSTALL=0 skips the system folder without one.
    for leftover in system.iterdir():
        shutil.rmtree(leftover)
    system.chmod(0o555)
    try:
        result, calls = run('reinstall-tool', SUDO_OK='1')
        assert result.returncode == 0, result.stderr
        assert any(c[0] == 'sudo' for c in calls), calls
        result, calls = run('reinstall-tool')
        assert result.returncode == 0, result.stderr
        assert 'skipped the system-wide install' in result.stderr, result.stderr
        assert (temp / 'home/Applications/Mux.app/Contents/MacOS/Mux.Desktop').is_file()
        result, calls = run('reinstall-tool', MUX_SYSTEM_INSTALL='0')
        assert result.returncode == 0, result.stderr
        assert 'skipped' not in result.stderr and not any(c[0] == 'sudo' for c in calls), calls
    finally:
        system.chmod(0o755)
    # run-at-startup installs or refreshes only the per-user agent (never /Applications, never sudo) and
    # registers that copy; remove-from-startup unregisters it and leaves the app alone.
    for leftover in system.iterdir():
        shutil.rmtree(leftover)
    plist_path = temp / 'home/Library/LaunchAgents/com.jchristn.mux.agent.plist'
    agent_bin = temp / 'home/Applications/Mux Agent.app/Contents/MacOS/Mux.Agent'
    result, calls = run('run-at-startup', 'net8.0', SUDO_OK='1')
    assert result.returncode == 0, result.stderr
    publishes = [c for c in calls if c[:2] == ['dotnet', 'publish']]
    assert len(publishes) == 1 and publishes[0][2].endswith('Mux.Agent.csproj'), publishes
    assert publishes[0][publishes[0].index('--framework') + 1] == 'net8.0', publishes
    assert not any(c[0] == 'sudo' for c in calls) and not any(system.iterdir()), calls
    with plist_path.open('rb') as f:
        plist = plistlib.load(f)
        assert plist['ProgramArguments'] == [str(agent_bin)]
        assert plist['EnvironmentVariables'] == {'MUX_AGENT_AUTOSTART': '0'}
    assert any(c[:2] == ['launchctl', 'bootstrap'] for c in calls), calls
    assert (temp / 'home/.mux/startup-registered').exists()
    result, calls = run('run-at-startup', MOCK_RUNNING='1')
    assert result.returncode != 0 and 'Another mux agent is running' in result.stderr, result.stderr
    assert not any(c[:2] == ['dotnet', 'publish'] for c in calls) and agent_bin.is_file()
    result, calls = run('remove-from-startup', tfm=None)
    assert result.returncode == 0 and not plist_path.exists() and agent_bin.is_file(), result.stderr
    assert any(c[:2] == ['launchctl', 'bootout'] for c in calls), calls
    result, calls = run('remove-from-startup', tfm=None)
    assert result.returncode == 0 and 'nothing to do' in result.stdout, result.stdout
    result, calls = run('run-at-startup')  # registered again for the remove-tool check below
    assert result.returncode == 0 and plist_path.exists(), result.stderr
    # A running app is never replaced: the publish is skipped and the installed copy kept.
    result, calls = run('run-desktop', MOCK_RUNNING='1')
    assert result.returncode != 0 and 'is running' in result.stderr
    assert not any(c[:2] == ['dotnet', 'publish'] for c in calls)
    # A failed publish must preserve the previous installation and never launch it.
    marker = temp / 'home/Applications/Mux.app/keep-me'
    marker.write_text('installed version')
    for failure in ['FAIL_PUBLISH', 'FAIL_SIGN']:
        result, calls = run('run-desktop', **{failure: '1'})
        assert result.returncode != 0 and marker.exists()
        assert not any(c[0] == 'open' for c in calls)
    # remove-tool removes both apps (the fake dotnet reports the tool as not installed).
    result, calls = run('remove-tool', tfm=None)
    assert result.returncode == 0, result.stderr
    assert not (temp / 'home/Applications/Mux.app').exists()
    assert not (temp / 'home/Applications/Mux Agent.app').exists()
    assert not (system / 'Mux.app').exists() and not (system / 'Mux Agent.app').exists()
    assert 'nothing to remove' in result.stdout and 'still registered to start at login' in result.stdout
    print('macOS install/reinstall/run: both architectures and frameworks passed; per-user and system installs; sudo fallback and opt-out; run-at-startup per-user agent; failed publish preserves installed app.')
