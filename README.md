# EdgeShelf

A thin, see-through shelf hidden on the edge of your screen (right or left). Drag a file, folder or piece of text onto it, keep working, and drag it back out wherever you need it.

## How it works

1. Run **EdgeShelf-Setup.exe** and finish the installer. A small translucent handle appears on the right edge of the screen, and an icon appears next to the clock.
2. **Open the shelf:** rest the mouse on the handle for a moment, or drag any file, folder or selected text towards the edge and it opens by itself.
3. **Drop things on it.** They're kept as icons.
4. **Move the mouse away** and it shrinks back to the handle.
5. **To take something out,** open the shelf and drag it into any folder, chat or app.

### Small details

- **Text cards:** one click copies the text to the clipboard (Ctrl+V anywhere). Arabic text is laid out right-to-left.
- **Files and folders:** double-click opens them. Dragging out makes a **copy**, the original stays where it is.
- **Clipboard button:** adds whatever you last copied. **Trash button:** empties the shelf. The small **x** on a card (shows on hover) removes just that one.
- Items are remembered after you close and reopen the app.
- The shelf keeps the path of each file, so if you delete or move the original it can't be dragged out any more.

### Tray icon (right-click the icon next to the clock)

- Move the shelf to the left or right edge
- **Look:** Clear, Glass or Dark (how see-through the shelf is)
- Pause (if the shelf opens when you don't want it to)
- Start with Windows
- Exit

## Download

Open the **Releases** tab, download `EdgeShelf-Setup.exe` and run it. The installer lets you choose the install folder, a desktop shortcut and Start with Windows.

If Windows SmartScreen shows a blue warning, click **More info** then **Run anyway**. It appears for any new program that isn't digitally signed.

## Notes

- Windows 10 and Windows 11 (64-bit).
- The shelf appears on the main display.
- If something goes wrong, details are written to `%AppData%\EdgeShelf\crash.log`.

## For developers

C# / WPF on .NET 8. Builds automatically on GitHub Actions (`.github/workflows/build.yml`) into a single self-contained exe, then wraps it in an Inno Setup installer (`installer/EdgeShelf.iss`).
