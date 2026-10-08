"""Generate the Partner Center store art into packaging/store-art/.

Usage: python packaging/make-store-art.py
Slots on the Store listing page:
  tile-300.png, tile-150.png, tile-71.png        -> Store display images (1:1 App tile icon and the two smaller squares)
  box-art-1080-<lang>.png                        -> Store logos, 1:1 Box art (1080 x 1080)
  poster-720x1080-<lang>.png                     -> Store logos, Poster art (720 x 1080)
  hero-1920x1080.png                             -> Windows and Xbox image, 16:9 Super hero art (no text, no UI)
"""
from pathlib import Path
import shutil
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'store-art'
OUT.mkdir(exist_ok=True)
for old in OUT.glob('*.png'):
    old.unlink()

logo = Image.open(ROOT.parent / 'work' / 'ZestDrop' / 'assets' / 'zestdrop.png').convert('RGBA')
logo = logo.crop(logo.getchannel('A').getbbox())

BG, INK, MUTED, ACCENT = (250, 248, 245), (31, 34, 38), (95, 101, 109), (213, 84, 24)
FONTS = {'en': ('C:/Windows/Fonts/segoeuib.ttf', 'C:/Windows/Fonts/segoeui.ttf'),
         'zh': ('C:/Windows/Fonts/msyhbd.ttc', 'C:/Windows/Fonts/msyh.ttc')}
TAGLINE = {'en': 'Drag. Drop. Convert.', 'zh': '拖，松，转！'}


def mark(width):
    return logo.resize((width, round(width * logo.height / logo.width)), Image.LANCZOS)


def centered(draw, text, font, y, fill, width):
    w = draw.textlength(text, font=font)
    draw.text(((width - w) / 2, y), text, font=font, fill=fill)


def card(size, lang, logo_w, logo_y, name_size, name_y, tag_size, tag_y):
    """Brand card with the key content kept in the top two thirds (overlays may cover the bottom third)."""
    w, h = size
    img = Image.new('RGBA', size, BG + (255,))
    glow = Image.new('RGBA', size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse((w * .1, h * -.2, w * 1.1, h * .5), fill=(252, 128, 60, 70))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(w * .12)))
    m = mark(logo_w)
    img.alpha_composite(m, ((w - m.width) // 2, logo_y))
    d = ImageDraw.Draw(img)
    bold, regular = (ImageFont.truetype(f, s) for f, s in ((FONTS[lang][0], name_size), (FONTS[lang][1], tag_size)))
    centered(d, 'ZestDrop', bold, name_y, INK, w)
    centered(d, TAGLINE[lang], regular, tag_y, ACCENT if lang == 'en' else MUTED, w)
    return img


for lang in ('en', 'zh'):
    card((1080, 1080), lang, 400, 150, 132, 580, 56, 740).convert('RGB').save(OUT / f'box-art-1080-{lang}.png', optimize=True)
    card((720, 1080), lang, 300, 110, 96, 450, 42, 580).convert('RGB').save(OUT / f'poster-720x1080-{lang}.png', optimize=True)

# Super hero art: no title, no text, no app UI, with the main shape in the centre and nothing important low down.
W, H = 1920, 1080
hero = Image.new('RGB', (W, H))
px = ImageDraw.Draw(hero)
for x in range(W):
    t = x / W
    px.line([(x, 0), (x, H)], fill=(int(255 - 70 * t), int(150 - 78 * t), int(70 - 40 * t)))
hero = hero.convert('RGBA')
glow = Image.new('RGBA', (W, H), (0, 0, 0, 0))
g = ImageDraw.Draw(glow)
g.ellipse((560, 60, 1360, 860), fill=(255, 220, 170, 150))
g.ellipse((-300, 500, 700, 1500), fill=(120, 40, 10, 130))
hero.alpha_composite(glow.filter(ImageFilter.GaussianBlur(170)))
white = Image.new('RGBA', logo.size, (255, 255, 255, 255))
white.putalpha(logo.getchannel('A'))
shadow = Image.new('RGBA', (W, H), (0, 0, 0, 0))
m = white.resize((620, round(620 * logo.height / logo.width)), Image.LANCZOS)
pos = ((W - m.width) // 2, 200)
shadow.paste((90, 30, 5, 140), (pos[0] + 8, pos[1] + 14), m.getchannel('A'))
hero.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(22)))
hero.alpha_composite(m, pos)
hero.convert('RGB').save(OUT / 'hero-1920x1080.png', optimize=True)

# Store display images come straight from the package logos so the listing matches the app.
for name, src in (('tile-300', 'Square150x150Logo.scale-200.png'), ('tile-150', 'Square150x150Logo.scale-100.png'), ('tile-71', 'SmallTile.scale-100.png')):
    shutil.copy(ROOT / 'Assets' / src, OUT / f'{name}.png')

for f in sorted(OUT.glob('*.png')):
    with Image.open(f) as im:
        print(f'{f.name:28} {im.size[0]}x{im.size[1]}  {f.stat().st_size // 1024} KB')
