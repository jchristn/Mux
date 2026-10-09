#!/usr/bin/env python3
"""Regenerate BUILTIN_SKILLS.md from the default skill library and the bundled skill packs.

Builds the mux CLI, seeds a throwaway config directory, reads every default skill and pack through
`mux skill list`, `mux skill pack list`, and `mux skill pack show`, and writes BUILTIN_SKILLS.md at the
repository root. Run it whenever a skill is added, removed, renamed, or re-described.

Usage: python3 scripts/common/generate-builtin-skills.py [--check] [--no-build] [--configuration Debug|Release]
  --check          Do not write; exit 1 when BUILTIN_SKILLS.md differs from what would be generated.
  --no-build       Use the existing net10.0 build of Mux.Cli instead of building it first.
  --configuration  The build configuration to use (default Debug).
"""
import collections, json, os, re, shutil, subprocess, sys, tempfile

args = sys.argv[1:]
configuration = 'Debug'
if '--configuration' in args:
    at = args.index('--configuration')
    if at + 1 >= len(args) or args[at + 1] not in ('Debug', 'Release'):
        print('--configuration needs Debug or Release.', file=sys.stderr)
        sys.exit(2)
    configuration = args[at + 1]
    del args[at:at + 2]
check = '--check' in args
no_build = '--no-build' in args
unknown = [a for a in args if a not in ('--check', '--no-build')]
if unknown:
    print('Unknown argument(s): ' + ' '.join(unknown) + '. Use --check, --no-build, and --configuration.', file=sys.stderr)
    sys.exit(2)

repo = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
project = os.path.join(repo, 'src', 'Mux.Cli', 'Mux.Cli.csproj')
dll = os.path.join(repo, 'src', 'Mux.Cli', 'bin', configuration, 'net10.0', 'Mux.Cli.dll')
if not no_build:
    built = subprocess.run(['dotnet', 'build', project, '-f', 'net10.0', '-c', configuration, '-v', 'quiet', '-nologo'], capture_output=True, text=True)
    if built.returncode != 0:
        print(built.stdout + built.stderr, file=sys.stderr)
        sys.exit(built.returncode)
if not os.path.exists(dll):
    print('Mux.Cli is not built at ' + dll + '; run without --no-build or pick the configuration you built.', file=sys.stderr)
    sys.exit(2)

cli = ['dotnet', dll]
cfg = tempfile.mkdtemp(prefix='mux-builtin-skills-')
env = dict(os.environ, MUX_CONFIG_DIR=cfg)


def run(args):
    out = subprocess.run(cli + args, env=env, capture_output=True, text=True, timeout=300)
    if out.returncode != 0:
        print('mux ' + ' '.join(args) + ' failed:\n' + out.stdout + out.stderr, file=sys.stderr)
        sys.exit(1)
    return out.stdout


try:
    listing = json.loads(run(['skill', 'list', '--output-format', 'json']))
    items = listing if isinstance(listing, list) else listing.get('skills', listing.get('Skills', []))
    skills_dir = os.path.join(cfg, 'skills')

    def fm_value(text, key):
        m = re.match(r'---\n(.*?)\n---', text.replace('\r\n', '\n'), re.S)
        if not m: return ''
        lines = m.group(1).split('\n')
        for i, l in enumerate(lines):
            if re.match(r'^' + re.escape(key) + r'\s*:', l, re.I):
                raw = l.split(':', 1)[1].strip()
                if raw[:1] in ('>', '|') and len(raw.rstrip('-+')) == 1:
                    parts = []
                    for n in lines[i + 1:]:
                        if n.strip() and not n[:1].isspace(): break
                        parts.append(n.strip())
                    return ' '.join(p for p in parts if p)
                return raw.strip('"\'')
        return ''

    def short(text, limit=170):
        text = re.sub(r'\s+', ' ', text).strip()
        text = text.replace('|', '\\|')
        if len(text) <= limit: return text
        cut = text[:limit].rsplit(' ', 1)[0].rstrip(',;:.')
        return cut + '...'

    by_cat = collections.defaultdict(list)
    for it in items:
        name = it['name']
        path = os.path.join(skills_dir, name, 'SKILL.md')
        text = open(path, encoding='utf-8').read() if os.path.exists(path) else ''
        desc = fm_value(text, 'description')
        src = fm_value(text, 'source')
        cmds = it.get('commands', 0)
        cmdcount = cmds if isinstance(cmds, int) else len(cmds)
        by_cat[it.get('category') or 'general'].append((name, desc, cmdcount, 'claude-skills' if src else 'mux'))

    packs_out = run(['skill', 'pack', 'list', '--output-format', 'json'])
    try:
        packs_json = json.loads(packs_out)
        pack_ids = [p['id'] for p in packs_json.get('packs', packs_json.get('Packs', []))]
    except Exception:
        pack_ids = ['business','compliance','data','docs','engineering','marketing','product','productivity','research','security']
    packs = []
    for pid in pack_ids:
        pj = json.loads(run(['skill', 'pack', 'show', pid, '--output-format', 'json']))
        packs.append(pj)

    total_default = sum(len(v) for v in by_cat.values())
    total_pack = sum(len(p['skills']) for p in packs)
    o = []
    o.append('# Built-in Skills')
    o.append('')
    o.append('_Generated from mux\'s default skill library and its bundled skill packs by `scripts/common/generate-builtin-skills.py` (or the `generate-builtin-skills` script for your OS). Regenerate it when skills change; `--check` reports whether it is current._')
    o.append('')
    o.append('mux ships with **%d default skills**, seeded into `~/.mux/skills` on first run and topped up on upgrade without overwriting your edits, and **%d more in %d opt-in packs** that you install when you want them. Every skill has a category; you can override it on any surface (`mux skill category <name> <category>`, `/skills`, the web dashboard, the desktop app, or VS Code) without editing its SKILL.md.' % (total_default, total_pack, len(packs)))
    o.append('')
    o.append('Most skills are listed to the model only where they apply: a skill can declare `appliesTo` file globs (for example `package.json` or `.github/workflows/*.yml`) and `requiresTools` (for example `gh` or `kubectl`), so a Python project never sees the Java skills. Run any skill by name with `/<skill> [arguments]`, list them with `mux skill list` (add `--category <name>` to filter), and read one with `mux skill show <name>`. **Commands** is the number of deterministic commands a skill offers through `run_skill`; a skill with 0 commands is a playbook the model follows with its normal tools. **Origin** is `mux` for skills written for mux and `claude-skills` for skills adapted from [alirezarezvani/claude-skills](https://github.com/alirezarezvani/claude-skills) (MIT; see THIRD_PARTY_NOTICES.md).')
    o.append('')
    o.append('## Contents')
    o.append('')
    order = sorted(by_cat.keys())
    for c in order:
        o.append('- [%s](#%s) (%d)' % (c, c, len(by_cat[c])))
    o.append('- [Optional packs](#optional-packs) (%d)' % total_pack)
    o.append('')
    o.append('## Default skills by category')
    o.append('')
    for c in order:
        o.append('### %s' % c)
        o.append('')
        o.append('| Skill | What it does | Commands | Origin |')
        o.append('|---|---|---:|---|')
        for name, desc, n, origin in sorted(by_cat[c]):
            o.append('| `%s` | %s | %d | %s |' % (name, short(desc), n, origin))
        o.append('')
    o.append('## Optional packs')
    o.append('')
    o.append('Packs are not seeded. Install one with `mux skill pack install <pack>` (or a single skill with `--skill <id>`), `/packs` in the terminal, the Packs panel in the web dashboard or desktop app, or `POST /v1.0/api/skills/packs/install`. Removing a pack never touches skills you wrote or edited. Pack skills keep their pack name as their category unless you change it.')
    o.append('')
    for p in sorted(packs, key=lambda p: p['id']):
        o.append('### %s pack (%d skills)' % (p['id'], len(p['skills'])))
        o.append('')
        o.append(short(p.get('description', ''), 400))
        o.append('')
        o.append('| Skill | What it does |')
        o.append('|---|---|')
        for s in sorted(p['skills'], key=lambda s: s['id']):
            o.append('| `%s` | %s |' % (s['id'], short(s.get('description', ''))))
        o.append('')
    text = '\n'.join(o).replace('\u2014', ', ').replace('\u2013', '-')
    target = os.path.join(repo, 'BUILTIN_SKILLS.md')
    if check:
        current = open(target, encoding='utf-8').read() if os.path.exists(target) else ''
        if current != text:
            print('BUILTIN_SKILLS.md is out of date; run this script without --check to regenerate it.', file=sys.stderr)
            sys.exit(1)
        print('BUILTIN_SKILLS.md is up to date (%d defaults in %d categories, %d pack skills in %d packs).' % (total_default, len(order), total_pack, len(packs)))
    else:
        open(target, 'w', encoding='utf-8', newline='\n').write(text)
        print('Wrote BUILTIN_SKILLS.md: %d defaults in %d categories, %d pack skills in %d packs.' % (total_default, len(order), total_pack, len(packs)))

finally:
    shutil.rmtree(cfg, ignore_errors=True)
