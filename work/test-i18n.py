"""Checks the backend's English messages.

Run with the portable Python: ./outputs/ZestDrop/runtime/python/python.exe work/test-i18n.py
"""
from pathlib import Path
import json, os, re, subprocess, sys, tempfile

ROOT = Path(__file__).resolve().parent
BACKEND = ROOT / 'ZestDrop' / 'backend'
sys.path.insert(0, str(BACKEND))
from translations import EN

checks = []


def check(name, ok, detail=''):
    checks.append({'test': name, 'passed': bool(ok), **({'detail': detail} if not ok else {})})


keys = {}
for file in BACKEND.glob('*.py'):
    for m in re.finditer(r"""\bT\((['"])((?:(?!\1)[^\\]|\\.)*)\1""", file.read_text(encoding='utf8')):
        keys.setdefault(m.group(2), file.name)
missing = sorted(k for k in keys if k not in EN)
check('every backend message has an English translation', not missing, missing)
unused = sorted(k for k in EN if k not in keys)
check('no stale English entries', not unused, unused)


def run(language):
    with tempfile.TemporaryDirectory() as temp:
        source = Path(temp) / 'notes.txt'
        source.write_text('hello', encoding='utf8')
        request, response = Path(temp) / 'request.json', Path(temp) / 'result.json'
        request.write_text(json.dumps({'Paths': [str(source)], 'Action': 'extractArchive'}), encoding='utf8')
        env = {**os.environ, 'ZESTDROP_LANG': language}
        subprocess.run([sys.executable, str(BACKEND / 'worker.py'), '--job', str(request), str(response)], env=env, check=True, timeout=120)
        return json.loads(response.read_text(encoding='utf8'))['Files'][0]['Error']


english, chinese = run('en'), run('zh')
check('errors come back in English when the app is set to English', english == 'Unsupported archive format', english)
check('errors stay in Chinese otherwise', chinese == '不支持的压缩包格式', chinese)

failed = [c for c in checks if not c['passed']]
print(json.dumps({'passed': len(checks) - len(failed), 'failed': len(failed), 'checks': checks}, ensure_ascii=False, indent=1))
sys.exit(1 if failed else 0)
