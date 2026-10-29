"""Test release installation in an isolated user profile; never touches real settings."""
import argparse
import hashlib
import json
import os
import pathlib
import sqlite3
import subprocess
import tempfile
import tomllib
import uuid
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--package', type=pathlib.Path, default=ROOT / '.local/releases/DeskDots-v0.1.0-windows-x64.zip')
parser.add_argument('--codex', default=os.environ.get('CODEX_APP_RUNTIME_CLI_PATH'))
args = parser.parse_args()
assert args.codex and pathlib.Path(args.codex).is_file(), 'Pass --codex with a local Codex CLI.'
work = ROOT / '.local' / ('installer-tests-' + uuid.uuid4().hex)
work.mkdir(parents=True)
package = work / 'package'
with zipfile.ZipFile(args.package) as archive:
    names = archive.namelist()
    assert not any('connection.json' in n or n.startswith(('.local/', '.generated/')) or n.endswith(('settings.json', 'auth.json')) for n in names)
    archive.extractall(package)
manifest = json.loads((package / 'release.json').read_text(encoding='utf-8-sig'))
for name, sha in manifest['files'].items():
    assert hashlib.sha256((package / name).read_bytes()).hexdigest() == sha
print('PASS: portable package hashes, complete payload, no developer connection data')

data = work / '组件 data'
home = work / 'user codex'
skill = work / 'skills/deskdots'
installed = work / 'installed app'
for directory in (data, home, work / 'temp'):
    directory.mkdir()
(data / 'settings.json').write_text(json.dumps({'left': 42, 'top': 28, 'customSetting': 'preserved'}), encoding='utf-8')
(home / 'config.toml').write_text('model = "gpt-6.1-sol"\n\n[mcp_servers.keep]\ncommand = "cmd.exe"\nargs = ["/c", "echo", "keep"]\n', encoding='utf-8')
with sqlite3.connect(home / 'state_5.sqlite') as db:
    db.execute('CREATE TABLE threads(id TEXT, name TEXT, title TEXT, updated_at INTEGER, source TEXT, archived INTEGER)')
(home / '.codex-global-state.json').write_text('{}', encoding='utf-8')
env = dict(os.environ, TEMP=str(work / 'temp'), TMP=str(work / 'temp'), CODEX_HOME=str(home))
base = ['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(package / 'install.ps1'),
        '-PackagePath', str(package), '-InstallDir', str(installed), '-DataDir', str(data),
        '-CodexHome', str(home), '-CodexExe', args.codex, '-SkillDirectory', str(skill),
        '-NoStart', '-NoShortcut', '-LocalOnly']

def run_install(success=True):
    result = subprocess.run(base, env=env, capture_output=True, timeout=90)
    assert (result.returncode == 0) == success, (result.returncode, result.stdout.decode(errors='replace'), result.stderr.decode(errors='replace'))
    if success:
        return json.loads((data / 'installation.json').read_text(encoding='utf-8-sig'))

report = run_install()
exe = pathlib.Path(report['installedExe'])
assert exe.is_file() and (exe.parent / 'assets/fonts/Geist-Regular.ttf').is_file()
assert (skill / 'SKILL.md').is_file() and (skill / 'scripts/install.ps1').is_file()
assert (skill / 'scripts/start.ps1').is_file() and (exe.parent / 'start.ps1').is_file()
assert (skill / 'scripts/start.ps1').read_bytes() == (exe.parent / 'start.ps1').read_bytes()
assert pathlib.Path(report['launcher']) == exe.parent / 'start.ps1' and not report['startRequested']
assert report['mcp'] == 'configured'
settings = json.loads((data / 'settings.json').read_text(encoding='utf-8-sig'))
assert settings['left'] == 42 and settings['customSetting'] == 'preserved'
assert settings['codexHome'] == str(home)
config = tomllib.loads((home / 'config.toml').read_text(encoding='utf-8-sig'))
assert config['model'] == 'gpt-6.1-sol'
assert config['mcp_servers']['keep']['command'] == 'cmd.exe'
assert config['mcp_servers']['deskdots']['command'] == str(exe)
assert config['mcp_servers']['deskdots']['args'] == ['--mcp', '--data-dir', str(data)]
assert pathlib.Path(report['configBackup']).is_file()
diagnostics = json.loads((data / 'diagnostics.json').read_text(encoding='utf-8-sig'))
assert diagnostics['taskError'] == '' and diagnostics['taskCount'] == 0
print('PASS: Windows PowerShell 5.1 install, Unicode/spaced paths, CLI MCP registration, skill, settings and config preservation')

second = run_install()
assert second['mcp'] == 'configured' and pathlib.Path(second['settingsBackup']).is_file()
config = tomllib.loads((home / 'config.toml').read_text(encoding='utf-8-sig'))
assert set(config['mcp_servers']) == {'keep', 'deskdots'}
assert list((data / 'skill-backups').iterdir())
print('PASS: repeated installation, backups, no duplicate MCP entries')

conflict = 'model = "gpt-6.1-sol"\n[mcp_servers.deskdots]\ncommand = "other.exe"\nargs = []\n'
(home / 'config.toml').write_text(conflict, encoding='utf-8')
report = run_install()
assert report['mcp'] == 'name-conflict'
assert (home / 'config.toml').read_text(encoding='utf-8') == conflict
print('PASS: unrelated MCP name conflict is preserved and reported')

before_settings = (data / 'settings.json').read_bytes()
before_exe = exe.read_bytes()
asset = package / 'assets/icons.json'
original_asset = asset.read_bytes()
asset.write_bytes(b'tampered')
run_install(False)
assert (data / 'settings.json').read_bytes() == before_settings and exe.read_bytes() == before_exe
asset.write_bytes(original_asset)
malicious = dict(manifest)
malicious['files'] = dict(manifest['files'], **{'../outside.txt': '0' * 64})
(package / 'release.json').write_text(json.dumps(malicious), encoding='utf-8')
run_install(False)
assert not (work / 'outside.txt').exists() and (data / 'settings.json').read_bytes() == before_settings
print('PASS: tampered payload and directory traversal rejected before configuration changes')
print('All installer checks passed. Isolated test artifacts:', work)
