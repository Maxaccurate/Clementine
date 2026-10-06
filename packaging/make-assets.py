"""Generate the Store/MSIX logo assets from work/ZestDrop/assets/zestdrop.png.

Usage: python packaging/make-assets.py
Writes packaging/Assets/*.png at every scale and target size Windows asks for.
"""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT.parent / 'work' / 'ZestDrop' / 'assets' / 'zestdrop.png'
OUT = ROOT / 'Assets'
OUT.mkdir(exist_ok=True)
for old in OUT.glob('*.png'):
    old.unlink()

logo = Image.open(SOURCE).convert('RGBA')
logo = logo.crop(logo.getchannel('A').getbbox())


def render(width, height, fill):
    """The logo centred on a transparent canvas, occupying `fill` of the shorter side."""
    canvas = Image.new('RGBA', (width, height), (0, 0, 0, 0))
    side = round(min(width, height) * fill)
    scale = side / max(logo.size)
    mark = logo.resize((max(1, round(logo.width * scale)), max(1, round(logo.height * scale))), Image.LANCZOS)
    canvas.alpha_composite(mark, ((width - mark.width) // 2, (height - mark.height) // 2))
    return canvas


# name: (base width, base height, share of the shorter side the logo fills)
tiles = {
    'Square44x44Logo': (44, 44, .86),
    'Square150x150Logo': (150, 150, .56),
    'Wide310x150Logo': (310, 150, .56),
    'SmallTile': (71, 71, .62),
    'LargeTile': (310, 310, .5),
    'StoreLogo': (50, 50, .86),
}
count = 0
for name, (w, h, fill) in tiles.items():
    for scale in (100, 125, 150, 200, 400):
        render(round(w * scale / 100), round(h * scale / 100), fill).save(OUT / f'{name}.scale-{scale}.png', optimize=True)
        count += 1
# Taskbar, Start list and Explorer icons pick these exact pixel sizes.
for size in (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256):
    icon = render(size, size, .92)
    for suffix in ('', '_altform-unplated', '_altform-lightunplated'):
        icon.save(OUT / f'Square44x44Logo.targetsize-{size}{suffix}.png', optimize=True)
        count += 1
print(f'wrote {count} images to {OUT}')
