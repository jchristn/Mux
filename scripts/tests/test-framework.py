"""Exercise the Unix entry points in scripts/{linux,macos,wsl,wsl2} with fake platform and build tools.

Run with: python3 scripts/tests/test-framework.py

Covers framework selection and forwarding, the OS guards, the install/reinstall/remove tool flows,
kill-mux, publish-nuget, build-installers and the Linux start-at-login registration. Nothing real is
built or installed: dotnet, uname, pgrep, pkill and mux are fakes that log their arguments, and the
scripts run against a temporary copy of the repository. The WSL scripts run with WSL_SKIP_CHECK=1;
the Windows interop parts (Startup-folder launcher) are covered by shellcheck and bash -n only.
"""
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[2]
PLATFORMS = ['linux', 'macos', 'wsl', 'wsl2']

FAKE = '''#!/usr/bin/env python3
import json, os, pathlib, sys
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
if name == 'uname':
    print(os.environ['MOCK_UNAME'] if args == ['-s'] else 'x86_64'); sys.exit(0)
if name == 'pgrep':
    sys.exit(0 if os.environ.get('MOCK_RUNNING') else 1)
if name in ('launchctl', 'systemctl'):
    # Never touch the real launchd/systemd: report "not loaded / no user manager".
    with open(os.environ['CALL_LOG'], 'a') as f: f.write(json.dumps([name, *args]) + '\\n')
    sys.exit(1)
if name == 'cmd.exe':
    print('C:\\\\Users\\\\test\\\\AppData\\\\Roaming\\r'); sys.exit(0)
if name == 'wslpath':
    print(os.environ['MOCK_APPDATA'] if args[0] == '-u' else 'C:\\\\fake\\\\' + pathlib.Path(args[1]).name); sys.exit(0)
with open(os.environ['CALL_LOG'], 'a') as f: f.write(json.dumps([name, *args]) + '\\n')
if name == 'dotnet' and os.environ.get('PATH_LOG'):
    with open(os.environ['PATH_LOG'], 'a') as f: f.write(os.environ['PATH'] + '\\n')
if name == 'dotnet':
    if args == ['--list-sdks']:
        print(os.environ.get('MOCK_SDKS', '8.0.425 [mock]')); sys.exit(0)
    if args[:3] == ['tool', 'list', '-g']:
        print('Package Id      Version      Commands')
        print('-------------------------------------')
        if os.environ.get('MOCK_INSTALLED'): print('mux.cli         0.0.1        mux')
        sys.exit(0)
    if args[:1] == ['build'] and 'Mux.Agent.csproj' in args[1]:
        tfm = args[args.index('-f') + 1]
        out = pathlib.Path(args[1]).parent / 'bin' / 'Release' / tfm
        out.mkdir(parents=True, exist_ok=True)
        agent = out / 'Mux.Agent'
        agent.write_text('#!/bin/sh\\nexit 0\\n'); agent.chmod(0o755)
'''


def make_repo(temp):
    repo = temp / 'repo'
    shutil.copytree(root / 'scripts', repo / 'scripts', ignore=shutil.ignore_patterns('__pycache__'))
    for project in ['Mux.Cli', 'Mux.Agent', 'Mux.Desktop', 'Mux.Publisher', 'Mux.Search', 'Mux.Core',
                    'Mux.Server', 'Mux.Desktop.Core']:
        csproj = repo / 'src' / project / f'{project}.csproj'
        csproj.parent.mkdir(parents=True)
        source = root / 'src' / project / f'{project}.csproj'
        csproj.write_text(source.read_text() if source.exists() else '<Project><PropertyGroup><Version>0.0.1</Version></PropertyGroup></Project>')
    return repo


with tempfile.TemporaryDirectory(prefix='mux scripts ') as temp:
    temp = Path(temp)
    repo = make_repo(temp)
    bin_dir = temp / 'bin'
    bin_dir.mkdir()
    for name in ['dotnet', 'uname', 'pgrep', 'pkill', 'mux', 'cmd.exe', 'wslpath', 'wscript.exe', 'launchctl', 'systemctl']:
        pass
    (bin_dir / 'sudo').write_text('#!/bin/sh\nexec "$@"\n')
    (bin_dir / 'apt-cache').write_text('#!/bin/sh\nprintf "libicu-dev\\nlibicu74\\nlibicu72\\n"\n')
    (bin_dir / 'apt-get').write_text(
        '#!/bin/sh\n'
        'printf "apt-get %s\\n" "$*" >> "$APT_LOG"\n'
        'case "$1" in install) : > "$APT_DONE" ;; esac\n')
    for name in ['sudo', 'apt-cache', 'apt-get']:
        (bin_dir / name).chmod(0o755)
    for name in ['dotnet', 'uname', 'pgrep', 'pkill', 'mux', 'cmd.exe', 'wslpath', 'wscript.exe', 'launchctl', 'systemctl']:
        (bin_dir / name).write_text(FAKE)
        (bin_dir / name).chmod(0o755)
    (bin_dir / 'host/fxr').mkdir(parents=True)  # the fake dotnet looks like a real .NET install root
    log = temp / 'calls.jsonl'
    home = temp / 'home'
    home.mkdir()
    base_env = dict(os.environ, PATH=f'{bin_dir}:/usr/bin:/bin', CALL_LOG=str(log), HOME=str(home),
                    WSL_SKIP_CHECK='1', DISPLAY=':0', WSL_DISTRO_NAME='Test', MOCK_APPDATA=str(temp / 'appdata'),
                    APT_LOG=str(temp / 'apt.log'), APT_DONE=str(temp / 'apt.done'))
    for key in ['MUX_TFM', 'DOTNET_ROOT', 'WAYLAND_DISPLAY']:
        base_env.pop(key, None)

    def run(platform, script, *args, **env):
        log.unlink(missing_ok=True)
        e = dict(base_env, MOCK_UNAME='Darwin' if platform == 'macos' else 'Linux', **env)
        p = subprocess.run(['bash', str(repo / f'scripts/{platform}/{script}.sh'), *args], env=e,
                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        calls = [json.loads(x) for x in log.read_text().splitlines()] if log.exists() else []
        return p, [c for c in calls if c[0] == 'dotnet' and c[1:] != ['--list-sdks']]

    count = 0

    # Every platform has the same scripts.
    names = {p: sorted(f.stem for f in (root / 'scripts' / p).glob('*.sh') if not f.name.startswith('_') and f.name != 'app-bundle.sh')
             for p in PLATFORMS}
    for p in PLATFORMS:
        assert names[p] == names['linux'], (p, set(names[p]) ^ set(names['linux']))
    windows = sorted(f.stem for f in (root / 'scripts/windows').glob('*.bat') if f.stem != 'resolve-framework')
    assert windows == names['linux'], set(windows) ^ set(names['linux'])

    # framework.sh precedence and SDK fallback, including under pipefail.
    helper = repo / 'scripts/common/framework.sh'

    def resolve(args, updates):
        r = subprocess.run(['bash', '-c', 'set -euo pipefail; . "$1"; shift; resolve_framework "$@"; printf "%s" "$TFM"',
                            'test', str(helper), *args], env=dict(base_env, **updates), check=True,
                           capture_output=True, text=True)
        return r.stdout
    assert resolve([], {}) == 'net8.0'
    assert resolve([], {'MOCK_SDKS': '8.0.100 [mock]\n10.0.401 [mock]'}) == 'net10.0'
    assert resolve([], {'MUX_TFM': 'net8.0', 'MOCK_SDKS': '10.0.401 [mock]'}) == 'net8.0'
    assert resolve(['net10.0'], {'MUX_TFM': 'net8.0'}) == 'net10.0'

    # Framework forwarding for the build/run scripts (macOS GUI launchers are covered by test-macos-app.py).
    for platform in PLATFORMS:
        scripts = ['run-tui', 'run-mcp-server', 'worktrees', 'publish-desktop']
        if platform != 'macos':
            scripts += ['run-agent', 'run-desktop']
        for script in scripts:
            for selection in [[], ['net8.0'], ['--framework', 'net8.0'], ['-f', 'net8'], ['net10.0']]:
                app = ['--http', '4321', '--host', 'two words'] if script == 'run-mcp-server' else ['list'] if script == 'worktrees' else []
                p, calls = run(platform, script, *selection, *app)
                assert p.returncode == 0, (platform, script, selection, p.stderr)
                expected = 'net10.0' if selection == ['net10.0'] else 'net8.0'
                assert '-p:TargetFrameworks=' + expected in calls[0], (platform, script, selection, calls)
                if script == 'run-mcp-server':
                    assert calls[1][-len(app):] == app, calls
                    assert p.stdout == '', ('run-mcp-server wrote to stdout before exec', platform, p.stdout)
                count += 1
            for bad in [['--framework'], ['net9.0']]:
                p, calls = run(platform, script, *bad)
                assert p.returncode != 0 and not calls, (platform, script, bad, calls)

    # OS guards: each platform's scripts refuse to run on the other OS.
    for platform, wrong in [('linux', 'Darwin'), ('macos', 'Linux')]:
        log.unlink(missing_ok=True)
        e = dict(base_env, MOCK_UNAME=wrong)
        p = subprocess.run(['bash', str(repo / f'scripts/{platform}/run-tui.sh')], env=e, capture_output=True, text=True)
        assert p.returncode != 0 and 'This script is for' in p.stderr, (platform, p.stderr)

    # GUI launchers need a display on Linux and WSL.
    for platform in ['linux', 'wsl', 'wsl2']:
        for script in ['run-agent', 'run-desktop']:
            e = {k: v for k, v in base_env.items() if k != 'DISPLAY'}
            log.unlink(missing_ok=True)
            p = subprocess.run(['bash', str(repo / f'scripts/{platform}/{script}.sh')], env=dict(e, MOCK_UNAME='Linux'),
                               capture_output=True, text=True)
            ran = [json.loads(x) for x in log.read_text().splitlines()] if log.exists() else []
            assert p.returncode != 0 and not [c for c in ran if c[1:] != ['--list-sdks']], (platform, script, p.stderr)

    # install-tool / reinstall-tool / remove-tool (macOS app bundles are covered by test-macos-app.py).
    for platform in ['linux', 'wsl', 'wsl2']:
        p, calls = run(platform, 'install-tool', 'net10.0')
        assert p.returncode == 0, p.stderr
        pack = next(c for c in calls if c[1] == 'pack')
        assert '-p:TargetFrameworks=net10.0' in pack and pack[pack.index('--output') + 1].endswith('tool-packages/net10.0'), pack
        assert any(c[1:3] == ['tool', 'install'] for c in calls), calls
        p, calls = run(platform, 'install-tool', MOCK_INSTALLED='1')
        assert p.returncode != 0 and 'reinstall-tool' in p.stderr and not any(c[1] == 'pack' for c in calls), p.stderr
        p, calls = run(platform, 'install-tool', MOCK_RUNNING='1')
        assert p.returncode != 0 and 'kill-mux' in p.stderr and not any(c[1] == 'pack' for c in calls), p.stderr
        p, calls = run(platform, 'reinstall-tool', MOCK_INSTALLED='1')
        assert p.returncode == 0, p.stderr
        order = [c[1:3] for c in calls if c[1] in ('tool', 'pack')]
        assert order.index(['tool', 'uninstall']) < order.index(['pack', str(repo / 'src/Mux.Cli/Mux.Cli.csproj')]) < order.index(['tool', 'install']), order
        p, calls = run(platform, 'reinstall-tool')
        assert p.returncode == 0 and 'not installed; continuing' in p.stdout and not any(c[1:3] == ['tool', 'uninstall'] for c in calls)
        p, calls = run(platform, 'remove-tool', MOCK_INSTALLED='1')
        assert p.returncode == 0 and any(c[1:3] == ['tool', 'uninstall'] for c in calls), p.stderr
        p, calls = run(platform, 'remove-tool')
        assert p.returncode == 0 and 'nothing to remove' in p.stdout and not any(c[1:3] == ['tool', 'uninstall'] for c in calls)
        p, calls = run(platform, 'remove-tool', 'extra')
        assert p.returncode != 0 and not calls
        count += 8

    # kill-mux with nothing running.
    for platform in PLATFORMS:
        p, _ = run(platform, 'kill-mux')
        assert p.returncode == 0 and 'No mux processes are running' in p.stdout, (platform, p.stdout, p.stderr)

    # publish-nuget: usage errors, then packs every library in order for the chosen framework.
    for platform in PLATFORMS:
        p, calls = run(platform, 'publish-nuget')
        assert p.returncode != 0 and not calls
        p, calls = run(platform, 'publish-nuget', 'KEY', 'net10.0')
        packs = [c for c in calls if c[1] == 'pack']
        assert [Path(c[2]).stem for c in packs] == ['Mux.Search', 'Mux.Core', 'Mux.Server', 'Mux.Desktop.Core'], packs
        assert all('-p:TargetFrameworks=net10.0' in c for c in packs)
        p, calls = run(platform, 'publish-nuget', 'KEY')  # no framework: pack every target framework
        packs = [c for c in calls if c[1] == 'pack']
        assert len(packs) == 4 and not any(a.startswith('-p:TargetFrameworks') for c in packs for a in c), packs
        assert p.returncode != 0 and 'no .nupkg' in p.stderr  # the fake dotnet produces no packages

    # build-installers: builds the publisher, then asks for this OS's channels.
    for platform in PLATFORMS:
        p, calls = run(platform, 'build-installers', '1.2.3', '--dry-run', 'net8.0')
        assert p.returncode == 0, (platform, p.stderr)
        job = 'macos' if platform == 'macos' else 'linux'
        assert any(c[1:3] == ['build', str(repo / 'src/Mux.Publisher/Mux.Publisher.csproj')] for c in calls), calls
        assert any('--channels-for' in c and c[c.index('--channels-for') + 1] == job for c in calls), calls
        p, calls = run(platform, 'build-installers', '--bogus')
        assert p.returncode != 0

    # Linux run-at-startup without systemd: builds the agent and writes the autostart entry.
    p, calls = run('linux', 'run-at-startup', 'net8.0')
    assert p.returncode == 0, p.stderr
    desktop = (home / '.config/autostart/mux-agent.desktop').read_text()
    assert f'Exec=env "DOTNET_ROOT={bin_dir.resolve()}" MUX_AGENT_AUTOSTART=0 "{repo}/src/Mux.Agent/bin/Release/net8.0/Mux.Agent"' in desktop, desktop
    assert (home / '.mux/startup-registered').exists()
    (home / '.mux/startup-registered').unlink()
    p, _ = run('linux', 'remove-from-startup')
    assert p.returncode == 0 and not (home / '.config/autostart/mux-agent.desktop').exists()
    assert (home / '.mux/startup-registered').exists()  # the agent must not re-register itself afterwards
    p, _ = run('linux', 'remove-from-startup')
    assert p.returncode == 0 and 'nothing to do' in p.stdout

    # Source runs write the first-run marker so the agent they may start never registers itself at login.
    for platform in ['linux', 'wsl', 'wsl2']:
        for script in ['run-tui', 'run-desktop', 'run-agent']:
            marker = home / '.mux/startup-registered'
            marker.unlink(missing_ok=True)
            p, _ = run(platform, script)
            assert p.returncode == 0 and marker.exists(), (platform, script, p.stderr)

    # WSL run-at-startup: an older per-machine launcher for this distro is replaced, another distro's kept.
    startup_dir = temp / 'appdata/Microsoft/Windows/Start Menu/Programs/Startup'
    startup_dir.mkdir(parents=True, exist_ok=True)

    # WSL run-at-startup: writes a hidden Startup-folder launcher for this distro and starts it now.
    startup = temp / 'appdata/Microsoft/Windows/Start Menu/Programs/Startup/mux-agent-wsl-Test.vbs'
    for platform in ['wsl', 'wsl2']:
        log.unlink(missing_ok=True)
        e = dict(base_env, MOCK_UNAME='Linux', DOTNET_ROOT=str(bin_dir))
        legacy = startup_dir / 'mux-agent-wsl.vbs'
        legacy.write_bytes(b'CreateObject("WScript.Shell").Run "wsl.exe -d ""Test"" --exec x", 0, False\r\n')
        p = subprocess.run(['bash', str(repo / f'scripts/{platform}/run-at-startup.sh'), 'net10.0'], env=e,
                           capture_output=True, text=True)
        assert p.returncode == 0, (platform, p.stderr)
        assert not legacy.exists(), 'the older launcher for this distro must be removed'
        vbs = startup.read_bytes().decode()
        agent = f'{repo}/src/Mux.Agent/bin/Release/net10.0/Mux.Agent'
        assert vbs.startswith('CreateObject("WScript.Shell").Run "wsl.exe -d ""Test"" --cd ~ --exec /bin/sh -lc ""exec'), vbs
        assert f"'{agent}'" in vbs and f"'DOTNET_ROOT={bin_dir}'" in vbs and vbs.endswith(', 0, False\r\n'), vbs
        assert ("'DISPLAY=:0'" in vbs) == (platform == 'wsl'), vbs
        assert "'MUX_AGENT_AUTOSTART=0'" in vbs, vbs
        calls = [json.loads(x) for x in log.read_text().splitlines()]
        assert ['wscript.exe', 'C:\\fake\\mux-agent-wsl-Test.vbs'] in calls, calls
        p = subprocess.run(['bash', str(repo / f'scripts/{platform}/remove-tool.sh')], env=e, capture_output=True, text=True)
        assert p.returncode == 0 and 'still registered to start at Windows login' in p.stdout, p.stdout
        p = subprocess.run(['bash', str(repo / f'scripts/{platform}/remove-from-startup.sh')], env=e, capture_output=True, text=True)
        assert p.returncode == 0 and not startup.exists(), p.stderr
        p = subprocess.run(['bash', str(repo / f'scripts/{platform}/remove-from-startup.sh')], env=e, capture_output=True, text=True)
        assert p.returncode == 0 and 'nothing to do' in p.stdout

    # Process checks only look at this user's processes, and an exported CDPATH does not break SCRIPT_DIR.
    for path in root.glob('scripts/*/*.sh'):
        text = path.read_text()
        for line in text.splitlines():
            if ('pgrep ' in line or 'pkill ' in line) and not line.lstrip().startswith('#') and 'pgrep -fl' not in line:
                assert '-u "$(id -u)"' in line, (path, line)
    p, _ = run('linux', 'run-tui', CDPATH='.')
    assert p.returncode == 0, p.stderr

    # Native libraries (WSLg/minimal distros): GUI launchers stop before building when the Avalonia
    # libraries are missing, the TUI and install only warn, and a missing ICU stops every dotnet script.
    ICU = '\tlibicuuc.so.74 (libc6,x86-64) => /lib/x86_64-linux-gnu/libicuuc.so.74'
    AVALONIA = ['libX11.so.6', 'libICE.so.6', 'libSM.so.6', 'libXext.so.6', 'libXrandr.so.2', 'libXi.so.6',
                'libXcursor.so.1', 'libXfixes.so.3', 'libfontconfig.so.1']

    every = [ICU] + ['\t%s (libc6,x86-64) => /lib/x86_64-linux-gnu/%s' % (n, n) for n in AVALONIA]

    def ldconfig(tag, lines):
        f = temp / ('fake-ldconfig-' + tag)
        f.write_text('#!/bin/sh\nif [ -f "$APT_DONE" ]; then cat <<"EOF"\n' + '\n'.join(every) + '\nEOF\nexit 0\nfi\n'
                     'cat <<"EOF"\n' + '\n'.join(lines) + '\nEOF\n')
        f.chmod(0o755)
        return str(f)

    def apt_reset():
        (temp / 'apt.log').unlink(missing_ok=True)
        (temp / 'apt.done').unlink(missing_ok=True)

    def apt_log():
        return (temp / 'apt.log').read_text() if (temp / 'apt.log').exists() else ''
    full = ldconfig('full', [ICU] + ['\t%s (libc6,x86-64) => /lib/x86_64-linux-gnu/%s' % (n, n) for n in AVALONIA])
    no_gui = ldconfig('nogui', [ICU, '\tlibICE.so.6 (libc6) => /lib/i386-linux-gnu/libICE.so.6'])  # wrong arch only
    no_icu = ldconfig('noicu', ['\t%s (libc6,x86-64) => /lib/x86_64-linux-gnu/%s' % (n, n) for n in AVALONIA])
    for platform in ['linux', 'wsl', 'wsl2']:
        # Launchers only check: they stop before building and never install anything.
        for fake, script, ok, text in [
                (full, 'run-agent', True, None), (no_gui, 'run-agent', False, 'cannot start without'),
                (no_gui, 'run-desktop', False, 'libICE.so.6'),
                (no_gui, 'run-tui', True, 'cannot start until'), (no_icu, 'run-tui', False, 'needs ICU')]:
            apt_reset()
            p, calls = run(platform, script, MUX_LDCONFIG=fake, WSL_LDCONFIG=fake)
            assert (p.returncode == 0) == ok, (platform, script, fake, p.stderr)
            if text:
                assert text in p.stderr, (platform, script, p.stderr)
            if not ok:
                assert not [c for c in calls if c[1] in ('run', 'build', 'pack')], (platform, script, calls)
            assert 'install' not in apt_log(), (platform, script, apt_log())
            count += 1
        # Install-type scripts install what is missing, then carry on.
        for fake, script, want in [(no_gui, 'install-tool', ['libice6', 'libfontconfig1']),
                                   (no_icu, 'reinstall-tool', ['libicu74']),
                                   (no_gui, 'run-at-startup', ['libice6', 'libx11-6'])]:
            apt_reset()
            p, calls = run(platform, script, MUX_LDCONFIG=fake, WSL_LDCONFIG=fake)
            assert p.returncode == 0, (platform, script, p.stderr)
            log_text = apt_log()
            assert 'apt-get update -qq' in log_text, log_text
            for pkg in want:
                assert pkg in log_text, (platform, script, pkg, log_text)
            optional = 'xclip' in log_text, 'wslu' in log_text
            if script != 'run-at-startup':
                assert optional[0], log_text                      # clipboard for the TUI
                assert optional[1] == (platform != 'linux'), log_text  # wslu only under WSL
            count += 1
        # Nothing missing: no package manager call at all.
        apt_reset()
        (temp / 'apt.done').write_text('')
        p, _ = run(platform, 'run-at-startup', MUX_LDCONFIG=full, WSL_LDCONFIG=full)
        assert p.returncode == 0 and apt_log() == '', apt_log()
        # INSTALL_SYSTEM_PACKAGES=0: report and stop when required packages are missing.
        apt_reset()
        p, calls = run(platform, 'install-tool', MUX_LDCONFIG=no_gui, WSL_LDCONFIG=no_gui, INSTALL_SYSTEM_PACKAGES='0')
        assert p.returncode != 0 and 'sudo apt install' in p.stderr and apt_log() == '', p.stderr
        assert not [c for c in calls if c[1] == 'pack'], calls
        # -h/--help never installs anything.
        apt_reset()
        p, _ = run(platform, 'install-tool', '--help', MUX_LDCONFIG=no_gui, WSL_LDCONFIG=no_gui)
        assert p.returncode == 0 and apt_log() == '', apt_log()

    # WSL apps get the Windows PATH entries back (clip.exe, explorer.exe, wslview).
    path_log = temp / 'path.log'
    for platform in ['wsl', 'wsl2']:
        for script in ['run-tui', 'run-mcp-server']:
            path_log.unlink(missing_ok=True)
            p, _ = run(platform, script, PATH=base_env['PATH'] + ':/mnt/c/Windows/System32', PATH_LOG=str(path_log))
            assert p.returncode == 0, p.stderr
            assert '/mnt/c/Windows/System32' in path_log.read_text().splitlines()[-1], (platform, script)
            if script == 'run-mcp-server':
                assert p.stdout == '', 'run-mcp-server must not write to stdout'

    # Shell syntax for every script.
    for path in root.glob('scripts/**/*.sh'):
        subprocess.run(['bash', '-n', str(path)], check=True)
    print(f'{count} script invocations passed; OS guards, tool install/reinstall/remove, kill-mux, '
          'publish-nuget, build-installers, startup registration and shell syntax passed.')
