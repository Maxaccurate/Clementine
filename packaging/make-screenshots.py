"""Compose 1920x1080 Store screenshots from renders made by the app's debug modes.

Usage: python packaging/make-screenshots.py <renders folder>
The folder must contain, for each language L in (en, zh):
  wheel-L-holiday.png.png   ZestDrop.exe --debug-wheel-render formats <holiday.png> convert:jpg <out>
  wheel-L-trip.mp4.png      ZestDrop.exe --debug-wheel-render tools <trip.mp4> trimVideo <out>
  tool-L.png                ZestDrop.exe --debug-tool-render trimVideo <video> <out>
  stack-L-expanded.png      ZestDrop.exe --debug-indicator <folder>/stack-L
(set ZESTDROP_LANG=en or zh when rendering). Output: packaging/screenshots/<lang>-<n>.png
"""
from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageFilter

SRC = Path(sys.argv[1])
OUT = Path(__file__).resolve().parent / 'screenshots'
OUT.mkdir(exist_ok=True)
W, H = 1920, 1080


def wallpaper():
    """A soft desktop-like backdrop: slate gradient with warm, blurred light."""
    base = Image.new('RGB', (W, H))
    draw = ImageDraw.Draw(base)
    for y in range(H):
        t = y / H
        draw.line([(0, y), (W, y)], fill=(int(46 + 30 * t), int(52 + 26 * t), int(66 + 18 * t)))
    glow = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    g = ImageDraw.Draw(glow)
    g.ellipse((1100, -300, 2300, 700), fill=(252, 128, 60, 120))
    g.ellipse((-400, 500, 900, 1500), fill=(120, 150, 210, 90))
    g.ellipse((600, 650, 1500, 1350), fill=(255, 190, 140, 60))
    glow = glow.filter(ImageFilter.GaussianBlur(160))
    out = base.convert('RGBA')
    out.alpha_composite(glow)
    return out


def shadowed(canvas, image, x, y, radius=0, blur=28, opacity=110):
    shadow = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    mask = Image.new('L', image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, *image.size), radius=radius, fill=opacity)
    shadow.paste((0, 0, 0, 255), (x + 6, y + 18), mask)
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(blur)))
    if radius:
        clip = Image.new('L', image.size, 0)
        ImageDraw.Draw(clip).rounded_rectangle((0, 0, *image.size), radius=radius, fill=255)
        image = image.copy()
        image.putalpha(Image.composite(image.getchannel('A'), Image.new('L', image.size, 0), clip))
    canvas.alpha_composite(image, (x, y))


def cursor(canvas, x, y, scale=1.6):
    """A Windows-style arrow pointer with its tip at (x, y)."""
    pts = [(0, 0), (0, 17), (4, 13), (7, 20), (10, 19), (7, 12), (12, 12)]
    pts = [(x + px * scale, y + py * scale) for px, py in pts]
    d = ImageDraw.Draw(canvas)
    d.polygon(pts, fill=(255, 255, 255, 255), outline=(20, 20, 20, 255))
    d.line(pts + [pts[0]], fill=(20, 20, 20, 255), width=2)


def file_icon(canvas, x, y, label, color, scale=1.4):
    """A desktop file icon being dragged (semi-transparent, like Windows)."""
    icon = Image.new('RGBA', (int(46 * scale), int(56 * scale)), (0, 0, 0, 0))
    d = ImageDraw.Draw(icon)
    w, h = icon.size
    d.rounded_rectangle((0, 0, w - 1, h - 1), radius=int(6 * scale), fill=(255, 255, 255, 235))
    d.polygon([(w - 14 * scale, 0), (w, 14 * scale), (w - 14 * scale, 14 * scale)], fill=(220, 223, 228, 235))
    bw, bh = 30 * scale, 13 * scale
    d.rounded_rectangle(((w - bw) / 2, h - bh - 8 * scale, (w + bw) / 2, h - 8 * scale), radius=int(3 * scale), fill=color)
    try:
        from PIL import ImageFont
        font = ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf', int(9 * scale))
        tw = d.textlength(label, font=font)
        d.text(((w - tw) / 2, h - bh - 8 * scale + 1 * scale), label, font=font, fill='white')
    except OSError:
        pass
    faded = icon.copy()
    faded.putalpha(icon.getchannel('A').point(lambda a: int(a * .85)))
    canvas.alpha_composite(faded.rotate(-6, expand=True, resample=Image.BICUBIC), (x, y))


def wheel_shot(lang, name, tag, color, petal_angle_deg):
    canvas = wallpaper()
    wheel = Image.open(SRC / f'wheel-{lang}-{name}.png').convert('RGBA')
    size = 820
    wheel = wheel.resize((size, size), Image.LANCZOS)
    x, y = (W - size) // 2, (H - size) // 2
    shadowed(canvas, wheel, x, y, radius=size // 2, blur=40, opacity=120)
    # pointer resting on the highlighted petal, with the dragged file just below it
    import math
    cx, cy, r = x + size / 2, y + size / 2, size * 0.47  # near the rim, clear of the petal's label
    px, py = cx + r * math.cos(math.radians(petal_angle_deg)), cy + r * math.sin(math.radians(petal_angle_deg))
    file_icon(canvas, int(px + 18), int(py + 22), tag, color)
    cursor(canvas, int(px), int(py))
    return canvas


def window_shot(lang):
    canvas = wallpaper()
    tool = Image.open(SRC / f'tool-{lang}.png').convert('RGBA')
    scale = 1.25
    tool = tool.resize((int(tool.width * scale), int(tool.height * scale)), Image.LANCZOS)
    shadowed(canvas, tool, (W - tool.width) // 2, (H - tool.height) // 2, radius=10)
    return canvas


def stack_shot(lang):
    canvas = wallpaper()
    stack = Image.open(SRC / f'stack-{lang}-expanded.png').convert('RGBA')
    # The render has a grey backdrop; key it out so the cards sit on the wallpaper.
    px = stack.load()
    bg = px[0, 0]
    for yy in range(stack.height):
        for xx in range(stack.width):
            r, g, b, a = px[xx, yy]
            if abs(r - bg[0]) < 4 and abs(g - bg[1]) < 4 and abs(b - bg[2]) < 4:
                px[xx, yy] = (0, 0, 0, 0)
    scale = min(0.92, (H - 56 - 24) / stack.height)
    stack = stack.resize((int(stack.width * scale), int(stack.height * scale)), Image.LANCZOS)
    canvas.alpha_composite(stack, (W - stack.width - 24, H - 56 - stack.height - 8))
    # a Windows-like taskbar strip for context
    bar = Image.new('RGBA', (W, 56), (32, 34, 40, 235))
    canvas.alpha_composite(bar, (0, H - 56))
    return canvas


for lang in ('en', 'zh'):
    shots = [
        wheel_shot(lang, 'holiday.png', 'PNG', (47, 143, 91), -90),
        wheel_shot(lang, 'trip.mp4', 'MP4', (109, 79, 209), 15),
        window_shot(lang),
        stack_shot(lang),
    ]
    for i, shot in enumerate(shots, 1):
        shot.convert('RGB').save(OUT / f'{lang}-{i}.png', optimize=True)
print('wrote', sorted(p.name for p in OUT.glob('*.png')))
