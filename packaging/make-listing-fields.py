"""Write packaging/store-listing-fields.md: every Partner Center listing field, ready to paste, per language.

Usage: python packaging/make-listing-fields.py
The script also checks the Store's limits and fails if one is exceeded.
"""
from pathlib import Path

OUT = Path(__file__).resolve().parent / 'store-listing-fields.md'

L = {}
L['en'] = dict(
    heading='English (United States)',
    name='ZestDrop',
    short=("Convert files without opening an app. Hold Shift while you drag any file, and a format wheel opens under your "
           "cursor. Release on the format you want. Images, video, audio, PDF, Office and archives, all processed on your PC."),
    description="""ZestDrop turns the drag you already make into a file converter.

Select files on the desktop or in File Explorer and start dragging. Press Shift and a format wheel appears under your cursor; release on the format you want. Press Ctrl+Shift instead for tools such as compress, trim, crop or redact. The new file is saved next to the original, which is never overwritten.

WHAT IT HANDLES
• Images: JPG, PNG, WebP, HEIC, TIFF, AVIF, BMP, SVG. Convert, compress, crop to any ratio, pixelate or blur areas, adjust exposure and colour, add a background, strip metadata, or combine images into a PDF or collage.
• Video: MP4, MOV, MKV, WebM, AVI, WMV, GIF. Convert, extract MP3, make GIFs, compress to a target size, trim, crop, change speed, split, join, save frames, remove audio, redact regions over time.
• Audio: MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA. Convert, compress, normalize loudness, trim with silence removal, adjust channels, bleep out sections, make waveform videos.
• PDF and text: PDF to Word, images or text; text to PDF; merge, split, reorder and rotate pages; subtitles between SRT, VTT and text.
• Office: export Word, Excel and PowerPoint files to PDF or images with the exact layout (uses your installed Microsoft Office), and switch between Office and OpenDocument formats.
• Archives: pack and extract ZIP, TAR, GZIP and RAR.

STAYS OUT OF YOUR WAY
• Lives in the system tray. No main window.
• Each job gets a progress card with Pause, Cancel and Hide. Run several at once; stack the cards or hide them.
• English and Simplified Chinese interface, switchable from the tray menu.

PRIVATE BY DESIGN
• Everything runs on your PC with the engines included in the app (FFmpeg, 7-Zip, Python). Nothing is uploaded, and it works offline.
• No account, ads, analytics or telemetry.

Free and open source under the MIT License.""",
    features=[
        'Hold Shift while dragging a file to pick a new format from a wheel under your cursor',
        'Ctrl+Shift opens tools: compress, trim, crop, redact, change speed and more',
        'Images, video, audio, PDF, Office documents and archives',
        'Everything is processed locally: nothing is uploaded',
        'Pause, resume, cancel or hide any job',
        'Run several conversions at once',
        'Originals are never overwritten',
        'Built-in player with draggable trim handles for audio and video',
        'English and Simplified Chinese interface',
        'Free and open source',
    ],
    keywords=['file converter', 'video converter', 'image converter', 'audio converter', 'PDF converter', 'compress video', 'drag and drop'],
    copyright='© 2026 陈唐安',
    license=("ZestDrop is licensed under the MIT License. It includes third-party components under their own licenses, including "
             "FFmpeg (GPLv3), 7-Zip (LGPL) and PyMuPDF (AGPLv3). See \"Third-party notices\" in the tray menu."),
    developer='陈唐安',
    captions=[
        'Hold Shift while dragging a file and a format wheel opens under your cursor. Release on JPG, PDF, MP3 or any other format.',
        'Press Ctrl+Shift for tools: compress, trim, crop, change speed, redact and more.',
        'Tools with settings open their own window, with a live preview and draggable trim handles.',
        'Every job gets a progress card with Pause, Cancel and Hide. Run several at once.',
    ],
    shots='en',
)
L['zh'] = dict(
    heading='Chinese (China) 中文（中国）',
    name='ZestDrop',
    short=("无需打开应用即可转换文件。拖动任意文件时按住 Shift，光标下方会出现格式轮盘，在想要的格式上松手即可。"
           "图片、视频、音频、PDF、Office 和压缩包，全部在你的电脑上处理。"),
    description="""ZestDrop 把你本来就会做的“拖动文件”变成文件转换器。

在桌面或资源管理器中选中文件并开始拖动，按下 Shift，光标下方会出现格式轮盘，在想要的格式上松手即可。按 Ctrl+Shift 则打开工具，例如压缩、裁剪时段、裁剪画面或打码。新文件保存在原文件旁边，原文件永远不会被覆盖。

支持的文件
• 图片：JPG、PNG、WebP、HEIC、TIFF、AVIF、BMP、SVG。转换、压缩、按任意比例裁剪、马赛克或模糊打码、调整曝光和色彩、添加背景、清除元数据，或把多张图片合成 PDF 或拼图。
• 视频：MP4、MOV、MKV、WebM、AVI、WMV、GIF。转换、提取 MP3、制作 GIF、压缩到指定大小、裁剪时段、裁剪画面、变速、分段、拼接、保存帧、移除音频、按时间段打码。
• 音频：MP3、M4A、WAV、FLAC、OGG、Opus、AIFF、WMA。转换、压缩、标准化响度、裁剪并去除静音、调整声道、消音蜂鸣、生成波形视频。
• PDF 与文本：PDF 转 Word、图片或文本；文本转 PDF；合并、拆分、调整页面顺序和旋转；字幕在 SRT、VTT 和文本之间转换。
• Office：按原版式把 Word、Excel、PowerPoint 导出为 PDF 或图片（调用已安装的 Microsoft Office），并在 Office 与 OpenDocument 格式之间转换。
• 压缩包：打包和解压 ZIP、TAR、GZIP、RAR。

不打扰你
• 常驻系统托盘，没有主窗口。
• 每个任务都有进度卡片，可暂停、取消或隐藏；可同时运行多个任务，卡片可以叠起或隐藏。
• 支持简体中文和英文界面，可在托盘菜单中切换。

注重隐私
• 所有处理都由应用内置的引擎（FFmpeg、7-Zip、Python）在本机完成，不上传任何内容，断网也能使用。
• 无需账号，没有广告、统计或遥测。

以 MIT 许可证免费开源。""",
    features=[
        '拖动文件时按住 Shift，从光标下的轮盘选择新格式',
        'Ctrl+Shift 打开工具：压缩、裁剪、打码、变速等',
        '支持图片、视频、音频、PDF、Office 文档和压缩包',
        '全部在本机处理，不上传任何内容',
        '任务可暂停、继续、取消或隐藏',
        '可同时运行多个转换',
        '永不覆盖原文件',
        '音视频工具内置播放器和可拖动的裁剪手柄',
        '简体中文和英文界面',
        '免费开源',
    ],
    keywords=['文件转换', '视频转换', '图片转换', '音频转换', 'PDF 转换', '视频压缩', '格式转换'],
    copyright='© 2026 陈唐安',
    license=("ZestDrop 以 MIT 许可证授权，并包含遵循各自许可证的第三方组件，包括 FFmpeg（GPLv3）、7-Zip（LGPL）和 PyMuPDF（AGPLv3）。"
             "详见托盘菜单中的“第三方组件许可”。"),
    developer='陈唐安',
    captions=[
        '拖动文件时按住 Shift，光标下会出现格式轮盘，在 JPG、PDF、MP3 等格式上松手即可转换。',
        '按 Ctrl+Shift 打开工具：压缩、裁剪时段、裁剪画面、变速、打码等。',
        '需要设置的工具会打开独立窗口，带实时预览和可拖动的裁剪手柄。',
        '每个任务都有进度卡片，可暂停、取消或隐藏，可同时运行多个任务。',
    ],
    shots='zh',
)

# Limits from Partner Center and Microsoft's docs.
for code, d in L.items():
    assert len(d['short']) <= 270, (code, 'short description', len(d['short']))
    assert len(d['description']) <= 10000, (code, 'description')
    assert 'http' not in d['description'], (code, 'no URLs in the description')
    assert len(d['features']) <= 20 and all(len(f) <= 200 for f in d['features']), (code, 'features')
    assert len(d['keywords']) <= 7 and all(len(k) <= 40 for k in d['keywords']), (code, 'keywords')
    assert sum(len(k.split()) for k in d['keywords']) <= 21, (code, 'keyword words')
    assert all(len(c) <= 200 for c in d['captions']), (code, 'captions')


def block(text):
    return '```text\n' + text + '\n```\n'


lines = ['# Store listing fields: ready to paste',
         '',
         'Fill in each language on its own listing page (Store listings > click the language). Leave every field that is not listed here empty.',
         'Images are in `packaging/store-art/` (logos, hero) and `packaging/screenshots/` (screenshots). Regenerate with `make-store-art.py`, `make-screenshots.py` and this script.',
         '']
for code, d in L.items():
    n = d['shots']
    lines += [f"## {d['heading']}", '',
              '| Field on the page | What to enter |', '|---|---|',
              f"| Product name | {d['name']} (pick it in the drop-down) |",
              "| What's new in this version | **leave empty** (first submission) |",
              '| Short title, Voice title | **leave empty** (Xbox only) |',
              f"| Copyright and trademark info | {d['copyright']} |",
              f"| Developed by | {d['developer']} |",
              f"| Screenshots > Desktop | `screenshots/{n}-1.png` … `{n}-4.png`, in that order, with the captions below |",
              f"| Store logos > Poster art (720 x 1080) | `store-art/poster-720x1080-{n}.png` |",
              f"| Store logos > Box art (1080 x 1080) | `store-art/box-art-1080-{n}.png` |",
              '| Store display images > App tile icon 300 x 300 / 150 x 150 / 71 x 71 | `store-art/tile-300.png`, `tile-150.png`, `tile-71.png` |',
              '| Windows and Xbox image > Super hero art (1920 x 1080) | `store-art/hero-1920x1080.png` (the same for both languages) |',
              '| Xbox images, Trailers | **leave empty** (Windows only) |',
              '', f"### Description ({len(d['description'])} of 10,000 characters)", '', block(d['description']),
              f"### Short description ({len(d['short'])} of 270 recommended)", '', block(d['short']),
              f"### Product features (one per box, {len(d['features'])} of 20; press \"Add more\" for each)", '']
    lines += [f'{i}. `{f}`' for i, f in enumerate(d['features'], 1)]
    lines += ['', '### Keywords (press Enter after each; ' + f"{len(d['keywords'])} of 7, {sum(len(k.split()) for k in d['keywords'])} of 21 words)", '']
    lines += [f'- `{k}`' for k in d['keywords']]
    lines += ['', '### Additional license terms', '', block(d['license']), '### Screenshot captions (200 characters or fewer)', '']
    lines += [f'{i}. {c}' for i, c in enumerate(d['captions'], 1)]
    lines += ['', '---', '']
OUT.write_text('\n'.join(lines), encoding='utf8')
print('wrote', OUT.name, '| short descriptions:', {c: len(d['short']) for c, d in L.items()},
      '| descriptions:', {c: len(d['description']) for c, d in L.items()})
