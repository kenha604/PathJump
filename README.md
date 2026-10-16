<p align="center"><img src="Assets/pathjump.png" width="128" alt="PathJump icon"></p>

# PathJump

A small Windows tray app that remembers the file and folder paths you copy, and lets you jump any **Open/Save dialog** or **Explorer window** straight to them, without clicking through folders.

- **Collects paths automatically** whenever you copy one
- **Alt+~** opens a searchable panel of open Explorer folders, ★ Favorites and Recent paths
- **One click** jumps the current Save/Open dialog or Explorer window there, or pastes the path
- **Portable**: one `.exe`, settings stored next to it, no install needed

**Requirements:** Windows 10 or 11 (64-bit).

## Download

Grab `PathJump.zip` from the [Releases](../../releases) page (or build it yourself, below), unzip it anywhere permanent (e.g. `C:\Tools\PathJump`), and double-click `PathJump.exe`. Nothing else needs installing.

If Windows shows "Windows protected your PC", click **More info → Run anyway** (only needed once, because the app isn't code-signed).

## Build from source

Needs the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
git clone https://github.com/kenha604/PathJump.git
cd PathJump
dotnet build -c Release   # quick build: bin\Release\net9.0-windows\PathJump.exe (needs .NET 9 to run)
pwsh -File publish.ps1    # standalone build: dist\PathJump.zip (PathJump.exe that runs anywhere + tutorial)
```

| File | What it does |
|---|---|
| `App.cs` | Tray icon, hotkeys, clipboard capture, actions |
| `PanelForm.cs` | The popup panel (groups, filtering, keys and clicks) |
| `Jumper.cs` | Detects Open/Save dialogs and Explorer windows and moves them to a folder |
| `HotkeyForm.cs`, `Hotkey.cs` | Change-hotkeys wizard and hotkey parsing |
| `Store.cs` | Saved paths and settings (`Data\paths.json`, `Data\settings.json`) |
| `Native.cs` | Win32 API declarations |

---

# How to use PathJump

---

## 1. Start PathJump

1. Double-click **`PathJump.exe`** in the folder where you unzipped it.
2. A **folder icon** appears in the system tray (bottom-right, near the clock; it may be under the **^** arrow).
3. A notification confirms it's running.

**Run it automatically at login:** right-click the tray icon and tick **Start with Windows**.

> **Portable:** PathJump keeps everything in its own folder: `PathJump.exe` plus a `Data` folder holding your saved paths (`paths.json`) and settings (`settings.json`). To move it to another folder or PC, just move or zip the whole folder; your list and hotkeys come with it. If you had **Start with Windows** on, it automatically updates to the new location the next time you start PathJump.

> Only one copy runs at a time. Double-clicking the .exe again does nothing if it's already running.

---

## 2. Collect paths (automatic)

You don't need to do anything special. **Whenever you copy a path, PathJump saves it.** All of these work:

| How you copy | Example |
|---|---|
| Select a file or folder in Explorer and press **Ctrl+C** | Copy a folder like you normally would |
| Right-click a file → **Copy as path** (or **Ctrl+Shift+C**) | `"D:\Projects\Client A\Renders"` |
| Click Explorer's address bar and press **Ctrl+C** | `D:\Archive\2026\Client B` |
| Copy a path from an email, chat, or document | `\\server\shared\Projects` |

Text that isn't a real, existing path is ignored, so normal copying won't clutter the list.

**Tip:** to save the folder you're currently in, click the Explorer address bar and press **Ctrl+C**.

---

## 3. Open the panel: `Alt+~`

Press **Alt+~** from anywhere (hold **Alt**, tap the **~** key left of **1**). The panel opens at your mouse cursor. You can change this key any time (section 8).

```
┌ PathJump ────────────────────────────────────────────┐
│ → Jump dialog: Save As          ← what will be used  │
│ [ type to filter…                                  ] │
│ Open in Explorer                                     │
│   Renders            D:\Projects\Client A\Renders    │
│ ★ Favorites                                          │
│   Client renders     D:\Projects\Client A\Renders    │
│ Recent                                               │
│   Client B           D:\Archive\2026\Client B       │
└──────────────────────────────────────────────────────┘
```

The panel has three groups:

- **Open in Explorer**: every folder you currently have open in an Explorer window or tab, most recently used window first. You don't have to copy anything for these to appear.
- **★ Favorites**: paths you've starred with **F**. This list stays put: it never reorders or drops entries on its own (see section 6).
- **Recent**: paths you've copied or used, newest first.

The **top line** tells you what will happen when you pick something:

| Top line says | What picking a path does |
|---|---|
| **→ Jump dialog: …** | The Open/Save dialog moves to that folder |
| **→ Jump Explorer: …** | The Explorer window moves to that folder |
| **→ Paste into: …** | The path is typed into that app |
| **→ Open in Explorer** | Shows it in Explorer (existing window if open, otherwise a new one) |

### Jump to the path on your clipboard

If your clipboard holds a file or folder path, a second line appears at the top:

```
→ Jump Explorer: C:\Projects - File Explorer
📋 Click here to jump to clipboard: D:\Projects\Client A\Renders
```

**Click that top area** to send the dialog or Explorer window straight to the copied path. Shift+click pastes it as text, and right-click shows it in Explorer (focusing the tab that already has it open, or opening a new tab).

> **Example:** someone sends you `\\server\jobs\2026\Final` in chat → copy it → go to Explorer → **Alt+~** → click the top line.

Press **Esc**, or click anywhere else, to close the panel.

---

## 4. Everyday use: jumping a Save or Open dialog

This is the main use.

1. In any program, choose **File → Save As** (or **Open**). The dialog appears.
2. Press **Alt+~**.
3. Type a few letters of the folder name, e.g. `renders`. The list filters as you type.
4. Press **Enter** (or click the folder).
5. The dialog is now in that folder. **Any filename you had already typed is kept**, so just click **Save**.

### Even faster: `Ctrl+Alt+G`

If the folder you want is **already open in Explorer**:

1. Open the Save/Open dialog.
2. Press **Ctrl+Alt+G**.

The dialog jumps straight to the Explorer window you used most recently, without opening the panel.

> **Typical workflow:** browse to a folder in Explorer → switch to your app → **Save As** → **Ctrl+Alt+G** → **Save**.

---

## 5. Picking a path: click options

| Action | Result |
|---|---|
| **Click** or **Enter** | Jump (or paste, if not in a dialog or Explorer) |
| **Shift**+click or **Shift+Enter** | Always paste the path as text |
| **Right-click** (or Ctrl+click / **Ctrl+Enter**) | **Show in Explorer**, from anywhere: if the folder is already open, that window comes to the front and switches to its tab, and nothing else changes. If it isn't open, it opens in a **new tab** of your most recent Explorer window (a new window only if no Explorer is open). Existing tabs are never redirected. |
| **↑ / ↓**, **Page Up / Page Down** | Move through the list |
| **F** (with a row highlighted) | Add it to **★ Favorites**, or remove it if it's already a favorite |
| **Right-click** a row in **★ Favorites** | Remove it from Favorites |
| **Shift+right-click** | Menu with every option below |

**Paste as text** is useful for the filename box, a terminal, a chat message, or any app whose save screen isn't a standard Windows dialog.

---

## 6. Organising your list

| Shortcut | Shift+right-click menu | What it does |
|---|---|---|
| **F** | Add to / Remove from Favorites | Stars a path so it stays in **★ Favorites** |
| **F2** | Rename… | Gives it a friendly name, e.g. "Client A – Final renders" |
| **Del** | Remove | Deletes it from the list (never touches the actual folder) |
| — | Copy path | Copies the path to the clipboard |

### Favorites with the F key

1. Open the panel (**Alt+~**).
2. **Hover your mouse over a row** (it highlights), or move to it with **↑ / ↓**.
3. Press **F**. The row jumps into **★ Favorites**.

To remove a favorite: **right-click it** in the Favorites section (or highlight it and press **F** again).

> **F and the filter box:** when a row is highlighted by hovering or the arrow keys, **F favorites it** instead of typing. Every other key still types into the filter box. To type an **f** into the filter, click the filter box first (or press **F** before moving the mouse onto the list).

- **Filtering searches both the name and the full path**, so a renamed folder can be found either way. You can type several words: `client final` matches entries containing both.
- **Recent** keeps the last 60 paths, and older ones drop off automatically. **Favorites never drop off** and stay in alphabetical order.
- Paths that no longer exist (e.g. a drive that isn't connected) show **greyed out**.

---

## 7. Tray menu

Right-click the tray icon:

- **Running from: …** (top line): which folder this copy of PathJump runs from. Hover it to see where its saved paths are stored.
- **Show paths**: opens the panel (left-clicking the icon does the same).
- **Change hotkeys…**: pick your own keys (see section 8).
- **Pause capturing**: stops collecting copied paths, e.g. while copying lots of files you don't want saved.
- **Start with Windows**: launch PathJump at login.
- **Open settings folder**: opens where your list and settings are stored.
- **Exit**: closes PathJump.

---

## 8. Changing hotkeys

1. Right-click the tray icon → **Change hotkeys…**
2. Click the **Open the panel** box and **press the keys you want**, e.g. hold **Alt** and tap **~**. The box shows what you pressed.
3. Next to the box you'll see:
   - **✔ Available**: good to go.
   - **✖ Used by another program**: something else already owns that combination; press a different one.
   - **✖ Add Ctrl, Alt or Win**: a plain letter would block normal typing, so add a modifier (or use an F-key).
4. Do the same for **Quick jump** if you like, then click **Save**. The new keys work immediately; no restart needed.

**Reset to defaults** puts back `Alt+~` / `Ctrl+Alt+G`.

If a hotkey stops working (e.g. another app grabbed it before PathJump started), PathJump shows a notification. **Click the notification** to open the wizard. It also keeps retrying in the background, so the key starts working again once the other app lets go. Hovering over the tray icon shows the current hotkey, or "hotkey not active".

> Advanced: the same settings live in `settings.json` (tray → **Open settings folder**). Editing the file by hand needs a PathJump restart.

Your saved paths are in **`paths.json`** in the `Data` folder next to `PathJump.exe`. Back up that folder to keep your list. (If PathJump's folder can't be written to, e.g. inside Program Files, it stores them in `%APPDATA%\PathJump` instead.)

---

## 9. Troubleshooting

| Problem | Fix |
|---|---|
| Nothing happens when I press the hotkey | Check that the tray icon is there (start PathJump). Hover over it: if it says "hotkey not active", right-click → **Change hotkeys…** and pick another key (section 8). |
| A path I copied isn't in the list | It must be a full path that exists, like `C:\...` or `\\server\share\...`. Check that **Pause capturing** is off. |
| The dialog didn't jump | Some apps use their own save screens instead of the Windows one (e.g. Office's full-page "Save As"; click **Browse** there to get the real dialog). Otherwise use **Shift**+click to paste the path into the filename box and press Enter. |
| Doesn't work in an app run as administrator | Windows blocks this. Either run PathJump as administrator too, or use **Shift**+click to paste. |
| My favorites / saved paths disappeared | You're probably running a **different copy** of `PathJump.exe` (e.g. a shortcut pointing at another folder). Each copy keeps its own `Data` folder. Right-click the tray icon: the top line shows which folder this copy runs from. Start the copy whose `Data` folder has your list, and point your shortcuts at it. |
| An "Open in Explorer" folder is missing | Special views like **Home** or **This PC** aren't real folders, so they're skipped. |

---

## Quick reference

| Keys | Action |
|---|---|
| **Alt+~** | Open the panel |
| **Ctrl+Alt+G** | (In a file dialog) jump to the latest Explorer folder |
| Click the top line | Jump to the path on the clipboard |
| type | Filter the list |
| **Enter** / click | Jump or paste |
| **Shift+Enter** / Shift+click | Paste as text |
| **Right-click** / Ctrl+Enter | Show in Explorer (focus the open tab, or open a new tab) |
| **Shift+right-click** | Menu: favorite, rename, copy, remove |
| **F** (row highlighted) | Add to / remove from Favorites |
| **Right-click** a favorite | Remove from Favorites |
| **F2** | Rename |
| **Del** | Remove from list |
| **Esc** | Close the panel |
