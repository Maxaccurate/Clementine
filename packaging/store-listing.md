# Microsoft Store update: ZestDrop 1.0.0

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

## How to submit this update in Partner Center

1. Open **Apps and games**, click ZestDrop, and in the **Product release** section click **Start update**. Partner Center creates a new submission that copies everything from the previous one (properties, pricing, age rating, listings).
2. **Packages:** upload `outputs/msix/ZestDrop_1.0.0.0_x64.msix`. The version must be higher than the one already in the Store (1.0.0.0 is higher than every earlier ZestDrop package). If the old package is still listed, you may keep it or remove it; the Store gives each PC the highest version that applies. If Partner Center shows a message about this, send it to me.
3. **Store listings:** open each language (English, Chinese) and update the text and images from [store-listing-fields.md](store-listing-fields.md). The part that changes in an update: fill **What's new in this version**, replace the description and features, and replace the screenshots (the old ones show the previous look). The logos, tiles and hero image from the first submission can stay.
4. **Properties, Pricing and availability, Age ratings:** nothing changes. The first-submission answers still apply (Utilities & tools, free, all markets, every age-rating question "No"). If Partner Center asks you to re-confirm the age rating, answer "No" to every content question again.
5. **Submission options:** replace the certification notes with the text at the end of this file.
6. If the **Restricted capabilities** page appears again, paste the runFullTrust answer below.
7. Click **Submit for certification** on the overview page.

Screenshots: PNG, 1366 x 768 or larger (ours are 1920 x 1080), up to 10, each 50 MB or less. Upload `screenshots/en-1..4.png` to the English listing and `screenshots/zh-1..4.png` to the Chinese one, in that order, with the captions from `store-listing-fields.md`.

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
3. Drag again and press Ctrl+Shift to open the tools menu (e.g. Compress, Resize, Trim & split). Tools with settings open their own window with a preview.
4. Start a long video conversion to see Pause, Cancel and Hide on the progress card.
No account, sign-in or internet connection is needed.

New in this version, for the tester:
- Start with Windows is OFF by default. It can be switched on from the tray menu or the welcome window, using the app's startup task (windows.startupTask), and switched off again there or in Windows Settings > Apps > Startup.
- Watched folders (tray menu) watch only folders the user adds. New files in them are processed locally.
- Clipboard: the app reads the clipboard only when the user chooses "process clipboard" in the tray menu, and writes to it only when the user clicks "Copy result".
- Text recognition (OCR) uses the recogniser built into Windows (Windows.Media.Ocr); nothing is downloaded or uploaded.
- The Explorer right-click menu option is not available in the Store version.
- Presets, flows and settings are saved in the app's own local data folder.

runFullTrust: ZestDrop is a classic desktop (WPF) app. It needs full trust to read the files the user drags, write converted files next to them, run its bundled engines (FFmpeg, 7-Zip, Python) as child processes, and watch the folders the user adds.

Keyboard: to open its menu during a drag, ZestDrop polls the state of Shift, Ctrl, F8, F9 and Esc with GetAsyncKeyState only. It does not install keyboard hooks, register global hotkeys, or record keystrokes.

Network: the app makes no network connections.
