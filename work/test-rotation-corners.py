"""Regression checks for rotation defaults, blank corners and preview/export agreement."""
from pathlib import Path
import hashlib, json, sys
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / 'outputs/ZestDrop'
sys.path.insert(0, str(ROOT / 'work/ZestDrop/backend'))
import common, images, worker
common.FFMPEG = APP / 'runtime/ffmpeg/ffmpeg.exe'
common.FFPROBE = APP / 'runtime/ffmpeg/ffprobe.exe'
OUT = ROOT / 'outputs/rotation-corners'
OUT.mkdir(parents=True, exist_ok=True)
checks = []

def check(name, fn):
    try:
        fn()
        checks.append({'test': name, 'passed': True})
    except Exception as exc:
        checks.append({'test': name, 'passed': False, 'error': str(exc)})

def opaque_edges():
    for size in [(401, 401), (160, 120), (120, 317), (17, 9), (3, 5), (1, 7)]:
        source = Image.new('RGB', size, (20, 40, 60))
        for angle in [.1, 27.6, 45, 89.9, 123.4, 180.2, 270.7, 359.5]:
            result = images.rotate(source, {'angle': str(angle)})
            assert result.getchannel('A').getextrema() == (255, 255), (size, angle, result.size)
            assert ImageChops.difference(result.convert('RGB'), Image.new('RGB', result.size, (20, 40, 60))).getbbox() is None

def jpg_corners():
    path = OUT / 'uniform.jpg'
    Image.new('RGB', (401, 401), (20, 40, 60)).save(path)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    result = Image.open(images.tool(path, 'rotateImage', {'angle': '28'}))
    assert result.size == (293, 293)
    assert all(sum(result.getpixel(p)) < 200 for p in [(0, 0), (292, 0), (0, 292), (292, 292)])
    assert hashlib.sha256(path.read_bytes()).hexdigest() == digest

def preserved_modes():
    source = Image.new('RGB', (160, 120), (20, 40, 60))
    expanded = images.rotate(source, {'angle': '45', 'expand': 'expand'})
    assert expanded.width > 160 and expanded.getpixel((0, 0))[3] == 0
    kept = images.rotate(source, {'angle': '30', 'expand': 'keep', 'background': '#ff0000'})
    assert kept.size == source.size and kept.getpixel((0, 0))[:3] == (255, 0, 0)
    assert images.rotate(source, {'angle': '90'}).size == (120, 160)

def preview_export():
    path = OUT / 'pattern.png'
    source = Image.new('RGB', (160, 120), (20, 40, 60))
    source.paste((180, 100, 30), (40, 30, 120, 90)); source.save(path)
    params = {'angle': '330', 'flip': 'horizontal'}
    exported = Image.open(images.tool(path, 'rotateImage', params)).convert('RGBA')
    previewed = Image.open(worker.preview(path, 'rotateImage', params, OUT / 'preview')['Preview']).convert('RGBA')
    assert exported.size == previewed.size and ImageChops.difference(exported, previewed).getbbox() is None

def video_canvas():
    video = OUT / 'video.mp4'
    common.ffmpeg(['-f', 'lavfi', '-i', 'color=c=blue:s=128x96:d=1:r=10', '-pix_fmt', 'yuv420p', video])
    default = Image.open(worker.preview(video, 'rotateVideo', {'angle': '45'}, OUT / 'video-default')['Preview'])
    explicit = Image.open(worker.preview(video, 'rotateVideo', {'angle': '45', 'expand': 'expand'}, OUT / 'video-expanded')['Preview'])
    assert default.size == explicit.size and default.width > 128

check('all arbitrary angles crop out empty pixels across aspect ratios and small images', opaque_edges)
check('default JPEG rotation has no white corners and preserves the source file', jpg_corners)
check('expanded and original-size canvases remain available; quarter turns remain lossless', preserved_modes)
check('signed rotation preview and export have identical dimensions and pixels', preview_export)
check('video preview retains the video expanded-canvas default', video_canvas)
(OUT / 'checks.json').write_text(json.dumps(checks, indent=2), encoding='utf-8')
print('passed=' + str(sum(c['passed'] for c in checks)) + ', failed=' + str(sum(not c['passed'] for c in checks)))
for item in checks:
    if not item['passed']: print(item)
raise SystemExit(any(not c['passed'] for c in checks))
