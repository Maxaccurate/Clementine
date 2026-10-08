# Microsoft Store submission — ZestDrop 0.1.1

Copy each field into Partner Center. Limits noted in brackets are the Store's.

## Properties
- **Category:** Utilities & tools
- **Privacy policy URL:** https://zestdrop.org/privacy
- **Website:** https://zestdrop.org
- **Support contact:** https://github.com/Maxaccurate/ZestDrop/issues
- **System requirements:** Windows 10 version 1809 or later, x64. Optional: Microsoft Word, Excel or PowerPoint (desktop) for layout-exact Office exports.
- **Product declarations:** leave all unchecked (no in-app purchases, no ads, does not access the user's personal info).

## Pricing and availability
- **Price:** Free
- **Markets:** all

## Age ratings
Answer "No" to every content question (no violence, user communication, purchases, location sharing or personal data collection). Expected result: suitable for all ages.

## Where each part goes in Partner Center

1. **Packages** step: upload `outputs/msix/ZestDrop_0.1.1.0_x64.msix`. Once it is uploaded, Partner Center lists its languages (English, Chinese).
2. **Store listings** step, on the submission overview page: click a language name (for example "English (United States)"). That opens the listing page for that language. Everything below (description, features, **Screenshots**, store logos) is on that page, and you fill in each language separately. Add Chinese with **Add/remove languages**, or from the packages' languages after upload.
3. **Screenshots** (inside each language's listing page): PNG, 1366 x 768 or larger (ours are 1920 x 1080), up to 10, 50 MB or less each, optional caption of 200 characters or less. Upload `screenshots/en-1..4.png` to the English listing and `screenshots/zh-1..4.png` to the Chinese one.
4. **Store logos** on the same page (optional, recommended): 1:1 App tile icon, 300 x 300, use `Assets/Square150x150Logo.scale-200.png`.
5. **Properties**, **Pricing and availability** and **Age ratings** steps: the answers are in the sections below.
6. **Submission options** step: paste the certification notes and, if asked, the runFullTrust justification below.

## Screenshot captions (200 characters or less)

| # | English | 中文 |
|---|---|---|
| 1 | Hold Shift while dragging a file and a format wheel opens under your cursor. Release on JPG, PDF, MP3 or any other format. | 拖动文件时按住 Shift，光标下会出现格式轮盘，在 JPG、PDF、MP3 等格式上松手即可转换。 |
| 2 | Press Ctrl+Shift for tools: compress, trim, crop, change speed, redact and more. | 按 Ctrl+Shift 打开工具：压缩、裁剪时段、裁剪画面、变速、打码等。 |
| 3 | Tools with settings open their own window, with a live preview and draggable trim handles. | 需要设置的工具会打开独立窗口，带实时预览和可拖动的裁剪手柄。 |
| 4 | Every job gets a progress card with Pause, Cancel and Hide. Run several at once. | 每个任务都有进度卡片，可暂停、取消或隐藏，可同时运行多个任务。 |

## Restricted capability: runFullTrust (paste into "Why do you need the runFullTrust capability?")

Use the medium version (479 characters). If the box still rejects it, use the short one (281 characters).

**Medium:**

ZestDrop is a classic WPF desktop app packaged as MSIX, so it can't run in the UWP sandbox. It needs full trust to: read the files a user drags from any folder and save converted files next to them; run its bundled FFmpeg, 7-Zip and embedded Python as child processes; check Shift/Ctrl/F8/F9/Esc state during a drag (GetAsyncKeyState, no hooks, no keystroke logging); and automate the user's installed Office through COM. It makes no network connections; all processing is local.

**Short:**

Classic WPF desktop app packaged as MSIX. Full trust is needed to read the files a user drags from any folder, write converted files next to them, run bundled FFmpeg/7-Zip/Python helper programs, and automate the user's installed Office. No network access; everything runs locally.

---

## Store listing text, images and keywords

Everything for the two listing pages (description, short description, features, keywords, captions, images) is in [store-listing-fields.md](store-listing-fields.md).

---

## Notes for certification (Partner Center → Submission options)

ZestDrop is a tray utility with no main window. When it starts, a "Welcome to ZestDrop" window explains how to use it (close it with "Got it"; launching the app again from the Start menu shows it again). The app keeps running in the notification area: right-click its icon for the menu, including Language / 语言, and double-click it to see the welcome window again.

How to test:
1. Select any image, video, audio, PDF or archive file on the desktop or in File Explorer and start dragging it.
2. While dragging, press Shift: a round format menu opens under the cursor. Release the mouse on a format (e.g. JPG). A progress card appears at the bottom right, and the converted file is saved next to the original.
3. Drag again and press Ctrl+Shift to open the tools menu (e.g. Compress, Trim). Tools with settings open their own window.
4. Start a long video conversion to see Pause, Cancel and Hide on the progress card.
No account, sign-in or internet connection is needed.

runFullTrust: ZestDrop is a classic desktop (WPF) app. It needs full trust to read the files the user drags, write converted files next to them, and run its bundled engines (FFmpeg, 7-Zip, Python) as child processes.

Keyboard: to open its menu during a drag, ZestDrop polls the state of Shift, Ctrl, F8, F9 and Esc with GetAsyncKeyState only. It does not install keyboard hooks, register global hotkeys, or record keystrokes.

Network: the app makes no network connections.
