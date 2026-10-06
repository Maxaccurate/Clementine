"""Static pre-submission checks for the Microsoft Store package.

    ./outputs/ZestDrop/runtime/python/python.exe packaging/check-package.py [path-to.msix]

Reads the .msix as a zip (no install, no certificate needed) and checks the things Partner Center and the Store's
install test commonly reject: manifest problems, wrong logo sizes, file paths that are too long once installed,
names Windows cannot install, and files that should not ship. It does not replace running the Windows App
Certification Kit on a signed install, but it catches the usual problems in seconds.
Exit code 0 when nothing failed; warnings do not fail the run.
"""
import io
import json
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
NS = {
    'p': 'http://schemas.microsoft.com/appx/manifest/foundation/windows10',
    'uap': 'http://schemas.microsoft.com/appx/manifest/uap/windows10',
    'rescap': 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities',
}
# "C:\Program Files\WindowsApps\<Name>_<Version>_x64__<13-char publisher id>\" plus slack for a longer name/version.
INSTALL_PREFIX = len(r'C:\Program Files\WindowsApps\FA719600.ZestDrop_0.1.1.0_x64__0123456789abc') + 8
MAX_PATH = 260
RESERVED = {'con', 'prn', 'aux', 'nul', *(f'com{i}' for i in range(1, 10)), *(f'lpt{i}' for i in range(1, 10))}
results = []


def record(level, name, detail=''):
    results.append((level, name, detail))


def ok(name, detail=''): record('pass', name, detail)
def warn(name, detail=''): record('warn', name, detail)
def fail(name, detail=''): record('FAIL', name, detail)


def check(condition, name, detail=''):
    (ok if condition else fail)(name, '' if condition else detail)


def package_path():
    if len(sys.argv) > 1:
        return Path(sys.argv[1])
    found = sorted((ROOT / 'outputs' / 'msix').glob('ZestDrop_*_x64.msix'))
    if not found:
        sys.exit('No .msix found in outputs/msix. Run ./package-msix.ps1 first, or pass the path.')
    return found[-1]


def logo_expectations():
    """Pixel size each manifest logo must have, by qualifier. Sizes are from the Store asset guidelines."""
    return {
        'Square44x44Logo': (44, 44), 'Square150x150Logo': (150, 150), 'Wide310x150Logo': (310, 150),
        'SmallTile': (71, 71), 'LargeTile': (310, 310), 'StoreLogo': (50, 50),
    }


def check_logo(names, reference, label):
    """reference is a manifest path such as Assets\\Square150x150Logo.png; the package holds scaled variants."""
    stem = Path(reference.replace('\\', '/')).with_suffix('').as_posix()
    base = Path(stem).name
    exact = reference.replace('\\', '/') in names
    scaled = sorted(n for n in names if n.startswith(stem + '.scale-'))
    sized = sorted(n for n in names if n.startswith(stem + '.targetsize-'))
    if not (exact or scaled or sized):
        return fail(f'{label}: image files exist', f'no file matches {reference} (plain, .scale-NNN or .targetsize-NN)')
    ok(f'{label}: image files exist', f'{len(scaled)} scales, {len(sized)} target sizes' if (scaled or sized) else 'plain file')
    width, height = logo_expectations()[base]
    wrong = []
    for name in scaled:
        scale = int(re.search(r'scale-(\d+)', name).group(1))
        want = (round(width * scale / 100), round(height * scale / 100))
        if Image.open(io.BytesIO(zf.read(name))).size != want:
            wrong.append(f'{name} should be {want[0]}x{want[1]}')
    for name in sized:
        size = int(re.search(r'targetsize-(\d+)', name).group(1))
        if Image.open(io.BytesIO(zf.read(name))).size != (size, size):
            wrong.append(f'{name} should be {size}x{size}')
    if scaled and not any('scale-100' in n or 'scale-200' in n for n in scaled):
        wrong.append('no scale-100 or scale-200 variant')
    check(not wrong, f'{label}: pixel sizes match their qualifiers', '; '.join(wrong[:4]))


path = package_path()
zf = zipfile.ZipFile(path)
names = [i.filename for i in zf.infolist() if not i.filename.endswith('/')]
nameset = set(names)
print(f'Checking {path.name} ({path.stat().st_size / 1048576:.0f} MB, {len(names)} files)\n')

# ---- Manifest ------------------------------------------------------------------------------------------------------
check('AppxManifest.xml' in nameset, 'manifest is at the package root')
manifest_text = zf.read('AppxManifest.xml').decode('utf-8-sig')
check('__VERSION__' not in manifest_text, 'version placeholder was replaced')
try:
    manifest = ET.fromstring(manifest_text)
    ok('manifest is well-formed XML')
except ET.ParseError as error:
    fail('manifest is well-formed XML', str(error))
    sys.exit(1)

identity = manifest.find('p:Identity', NS)
version = identity.get('Version', '')
check(re.fullmatch(r'\d+\.\d+\.\d+\.0', version) is not None, 'version has four parts and ends in .0 (Store rule)', version)
check(identity.get('Name', '').count('.') >= 1 and not identity.get('Name', '').startswith('REPLACE'), 'package identity name is set', identity.get('Name'))
check(identity.get('Publisher', '').startswith('CN='), 'publisher is a CN= distinguished name', identity.get('Publisher'))
check(identity.get('ProcessorArchitecture') == 'x64', 'architecture is x64')
properties = manifest.find('p:Properties', NS)
display = properties.findtext('p:DisplayName', namespaces=NS) or ''
description = properties.findtext('p:Description', namespaces=NS) or ''
check(0 < len(display) <= 256, 'display name length is valid', f'{len(display)} characters')
check(0 < len(description) <= 2048, 'description length is valid', f'{len(description)} characters')
check(bool(properties.findtext('p:PublisherDisplayName', namespaces=NS)), 'publisher display name is set')

target = manifest.find('p:Dependencies/p:TargetDeviceFamily', NS)
check(target is not None and target.get('Name') == 'Windows.Desktop', 'targets Windows.Desktop')
check(target is not None and tuple(map(int, target.get('MinVersion').split('.'))) <= (10, 0, 17763, 0), 'minimum Windows version is 1809 or lower', target.get('MinVersion'))

capabilities = [c.get('Name') for c in manifest.findall('p:Capabilities/*', NS)]
check(capabilities == ['runFullTrust'], 'only the runFullTrust capability is declared', ', '.join(capabilities))

application = manifest.find('p:Applications/p:Application', NS)
executable = application.get('Executable')
check(executable in nameset, 'application executable is in the package', executable)
check(application.get('EntryPoint') == 'Windows.FullTrustApplication', 'entry point is Windows.FullTrustApplication')

languages = [r.get('Language') for r in manifest.findall('p:Resources/p:Resource', NS)]
check(len(languages) > 0, 'package declares its languages', ', '.join(languages))

# ---- Logos ---------------------------------------------------------------------------------------------------------
visual = application.find('uap:VisualElements', NS)
tile = visual.find('uap:DefaultTile', NS)
check_logo(nameset, properties.findtext('p:Logo', namespaces=NS), 'Store logo')
check_logo(nameset, visual.get('Square44x44Logo'), 'App list icon (44x44)')
check_logo(nameset, visual.get('Square150x150Logo'), 'Medium tile (150x150)')
if tile is not None:
    for attribute, label in (('Wide310x150Logo', 'Wide tile'), ('Square71x71Logo', 'Small tile'), ('Square310x310Logo', 'Large tile')):
        if tile.get(attribute):
            check_logo(nameset, tile.get(attribute), label)
check('resources.pri' in nameset, 'resources.pri (scaled-asset index) is present')

# ---- Package contents ----------------------------------------------------------------------------------------------
lowered = Counter(n.lower() for n in names)
duplicates = [n for n, c in lowered.items() if c > 1]
check(not duplicates, 'no two files differ only by letter case', ', '.join(duplicates[:3]))

longest = max(names, key=len)
budget = MAX_PATH - INSTALL_PREFIX
too_long = [n for n in names if len(n.replace('/', '\\')) > budget]
check(not too_long, f'every file path fits under the {MAX_PATH}-character limit once installed',
      f'{len(too_long)} files exceed {budget} characters; longest is {len(longest)}: {longest[:110]}')
if not too_long:
    ok('longest relative path', f'{len(longest)} characters (limit {budget})')

invalid = [n for n in names if any(part.rstrip() != part or part.endswith('.') or part.split('.')[0].lower() in RESERVED or re.search(r'[<>:"|?*]', part)
                                   for part in n.split('/'))]
check(not invalid, 'no file names Windows cannot create', ', '.join(invalid[:3]))

forbidden = [n for n in names if re.search(r'(^|/)(__pycache__|logs|\.zestdrop-[^/]*)(/|$)|\.pdb$|events\.jsonl$|settings\.json$|last-cli-result|\.pyc$', n)]
check(not forbidden, 'no logs, caches, debug symbols or personal data', ', '.join(forbidden[:4]))
for needed in ('THIRD-PARTY-NOTICES.txt', 'LICENSE', 'runtime/python/python.exe', 'runtime/ffmpeg/ffmpeg.exe', 'runtime/ffmpeg/ffprobe.exe', 'runtime/7zip/7z.exe', 'backend/worker.py'):
    check(needed in nameset, f'{needed} is included')

big = [n for n in names if zf.getinfo(n).file_size > 1_000_000_000]
check(not big, 'no single file over 1 GB', ', '.join(big))
lines_marker = zf.read('THIRD-PARTY-NOTICES.txt').decode('utf-8', 'replace') if 'THIRD-PARTY-NOTICES.txt' in nameset else ''
check('FFmpeg' in lines_marker and 'GPL' in lines_marker, 'third-party notices cover FFmpeg and its GPL license')

# Nothing in the package may reference the developer's machine.
leaks = []
for name in ('AppxManifest.xml', 'THIRD-PARTY-NOTICES.txt'):
    if name in nameset and re.search(r'C:\\Users\\', zf.read(name).decode('utf-8', 'replace')):
        leaks.append(name)
check(not leaks, 'metadata files do not mention local user folders', ', '.join(leaks))

# ---- Report --------------------------------------------------------------------------------------------------------
width = max(len(r[1]) for r in results)
for level, name, detail in results:
    print(f'  {level:<4}  {name:<{width}}  {detail}')
failed = sum(1 for r in results if r[0] == 'FAIL')
warned = sum(1 for r in results if r[0] == 'warn')
print(f'\n{len(results) - failed - warned} passed, {warned} warnings, {failed} failed')
sys.exit(1 if failed else 0)
