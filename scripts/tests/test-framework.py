import os, tempfile, subprocess, json
from pathlib import Path
# Run with: python3 scripts/tests/test-framework.py
root=Path(__file__).resolve().parents[2]
with tempfile.TemporaryDirectory() as d:
 d=Path(d); log=d/'calls'; fake=d/'dotnet'
 fake.write_text('''#!/usr/bin/env python3
import sys,os,json
if sys.argv[1:]==['--list-sdks']: print(os.environ.get('MOCK_SDKS','8.0.425 [mock]')); sys.exit(0)
with open(os.environ['CALL_LOG'],'a') as f: f.write(json.dumps(sys.argv[1:])+'\\n')
''');fake.chmod(0o755)
 # Exercise framework forwarding without invoking native macOS packaging.
 uname=d/'uname'; uname.write_text('#!/bin/sh\nprintf \"Linux\\n\"\n'); uname.chmod(0o755)
 env=dict(os.environ,PATH=str(d)+':'+os.environ['PATH'],CALL_LOG=str(log));env.pop('MUX_TFM',None)
 # Check precedence and SDK fallback without launching applications.
 helper=root/'scripts/common/framework.sh'
 def resolve(args, updates):
  result=subprocess.run(['sh','-c', '. "$1"; shift; resolve_framework "$@"; printf "%s" "$TFM"', 'test', str(helper), *args], env=dict(env, **updates), check=True, capture_output=True, text=True)
  return result.stdout
 assert resolve([], {})=='net8.0'
 assert resolve([], {'MOCK_SDKS':'10.0.401 [mock]'})=='net10.0'
 assert resolve([], {'MUX_TFM':'net8.0','MOCK_SDKS':'10.0.401 [mock]'})=='net8.0'
 assert resolve(['net10.0'], {'MUX_TFM':'net8.0'})=='net10.0'
 count=0
 for platform in ['linux','macos']:
  for script in ['run-agent','run-desktop','run-tui','run-mcp-server','worktrees','publish-desktop']:
   for selection in [[],['net8.0'],['--framework','net8.0'],['-f','net8'],['net10.0']]:
    log.unlink(missing_ok=True)
    app=['--http','4321','--host','two words'] if script=='run-mcp-server' else ['list'] if script=='worktrees' else []
    subprocess.run(['bash',str(root/f'scripts/{platform}/{script}.sh'),*selection,*app],env=env,check=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    calls=[json.loads(x) for x in log.read_text().splitlines()]; expected='net10.0' if selection==['net10.0'] else 'net8.0'
    assert '-p:TargetFrameworks='+expected in calls[0],(script,selection,calls)
    if script=='run-mcp-server': assert calls[1][-len(app):]==app,calls
    count+=1
   for bad in [['--framework'],['net9.0']]:
    log.unlink(missing_ok=True)
    p=subprocess.run(['bash',str(root/f'scripts/{platform}/{script}.sh'),*bad],env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    assert p.returncode!=0 and not log.exists(),(script,bad,p)
 for path in root.glob('scripts/**/*.sh'): subprocess.run(['bash','-n',str(path)],check=True)
 print(f'{count} script invocations passed; missing/invalid framework checks and shell syntax passed.')
