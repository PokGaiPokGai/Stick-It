# Stick It

A Windows desktop sticky-note application forked from Microsoft's MIT-licensed WPF-Samples. The application project is `Sample Applications/StickyNotesDemo`.

## Usage

- One instance per Windows user/session, regardless of the executable's folder. A second manual launch opens the existing instance's library.
- A Windows sign-in entry can launch `"D:\Stick It\Stick It.exe" --startup` using the installed executable's path. Stick It starts in the tray and restores the notes that were left open in their saved positions without bringing the library to the foreground.
- **Shift + Alt + W** creates a new, pinned note centered on the mouse pointer. The position is constrained to that monitor's work area. The shortcut does not repeat while held; registration failures appear in the library.
- Individual notes use native tool windows, excluding them from the taskbar and Alt+Tab while retaining normal click-to-edit activation. The library has a taskbar entry only while it is open; closing the library hides it, leaving the tray and notes running.
- The game mouse preview automatically makes notes mouse-transparent while another foreground window locks its pointer, or hides it while confined/fullscreen. A visible movable pointer, releasing a previously observed confinement, or activating Stick It restores interaction. The tray and library settings have a **游戏鼠标自动穿透** toggle; this preview enables it on each launch.
- Click the tray icon to open the library. Its menu includes new note and save/exit.
- The library gear opens an in-window settings page with switches, shortcut help, save and exit actions. Use the back arrow or Esc to return to the existing note list and search; opening the library from the tray returns to the list.
- The library settings include **新建便签随机颜色**, enabled by default. New notes randomly use one of the existing six paper colors; disabling it restores yellow for new notes. This preference is saved in `settings.json` beside the note store and never recolors existing notes.
- Notes open on top at a default width of 320 DIPs. Drag a side to change the width. The height follows the rendered text as you type, delete, paste, or change the wrapping. A note near the bottom grows upward to use the current monitor's work area and returns toward its chosen position when shortened.
- Click the body to edit. The note is one solid rounded rectangle. Its upper and lower edges expand outward with the close button and formatting controls, keeping the text and its scroll position fixed. Both edges retract when editing ends. The transparent area beyond the paper lets the pointer reach the application underneath. The slide follows the Windows animation setting. Drag the empty top area or the paper margin to move a note. Right-click the paper to change its color or delete it.
- The editing scrollbar appears when the text and toolbars exceed the monitor's usable height. Long reading notes support wheel scrolling with their scrollbar hidden. Notes and the library use slim gray scrollbars with transparent tracks.
- Hover over a pinned note to scroll its content with the wheel without entering edit mode or showing its scrollbar. Reading mode also accepts native wheel messages for inactive notes; game mouse passthrough leaves wheel input to the game. Wheel distance follows the Windows line/page setting.
- An editing note resumes editing when its window regains activation, preserving its document and caret after input-language popups or application switches. Reading notes stay read-only, and closing a note or entering game passthrough cancels that restoration.
- **Ctrl + Space** is reserved for the input method rather than the native rich-text reset-format command, so switching input mode does not clear character formatting.
- Closing a note hides it; click its library card to reopen it. Deletion requires confirmation.
- Restoring or reopening a note checks the current monitor work area and moves a completely offscreen note back into view, preserving positions that are still visible.
- Click the note body to edit the formatted content in place. The bottom toolbar offers heading, bold, italic, underline, strikethrough, bulleted list, numbered list, task, and inline code. Each button keeps a fixed 32-DIP width; on narrow notes, hover over the toolbar and use the wheel to scroll horizontally (down/right, up/left). A filled button indicates an active format and an outlined button indicates a format applied to part of the selection. Click a task checkbox to change its state.
- Press **Ctrl + Shift + 7 / 8 / 9** for numbered, bulleted, or task lists. Use **Tab** and **Shift + Tab** (or **Ctrl + ] / [**) to change the indentation of the current item or selection. **Enter** continues a list, an empty item moves out one level, and **Shift + Enter** adds a line break within an item. Backspace at the start of an item also moves it out one level.
- Task checkboxes stay outside the editable label: Home, gutter clicks and label selections cannot insert text before the checkbox. Left/Right and word-selection shortcuts can still cross task rows, while Home on a wrapped line stays on that visual line.
- Use **Ctrl + Alt + 1–6** for headings and **Ctrl + Alt + 0** for body text. Enter at the end of a heading starts a body paragraph. Deleting the entire heading text or pressing Backspace at the start of a heading resets that paragraph to body text.
- Use Markdown shortcuts while typing. For example, type `##` followed by a space for a heading or `**text**` followed by a space for bold. Pasting plain-text Markdown inserts formatted content at the selection.
- Copy/paste within Stick It carries the selected Markdown semantics, preserving headings, fenced code, task states and nested lists. Partial inline selections remain inline. Task completion participates in undo/redo alongside typing and formatting changes.
- Notes save to Markdown and can reopen with headings, emphasis, lists, task states, quotes, inline and fenced code, links, and tables. Images are represented by their labels.
- Edits are saved after a 600 ms pause and when Windows signs out or shuts down. Notes and open/closed state live in `%LOCALAPPDATA%\Stick It\notes.json`; the previous save is retained as `notes.json.bak`.
- If saving fails during sign-out/shutdown, Stick It cancels its session-ending request and opens the library with the save error, retaining unsaved content. Invalid settings are reported through the normal startup error path without constructing a partial application window.
- Original sample `NotesFile` data is imported on first launch when no JSON store exists. Text, position and color are imported; original rich-text files are retained unchanged. The old sample's email/alarm demonstration UI is not part of this Markdown version.

## Upgrading from the original demo / tray preview

Exit **each old instance** using its save-and-exit menu before starting this build. Those old executables predate the singleton gate and cannot be made exclusive by the new executable. Do not run the old executables alongside the new build, since their note storage format is different.

## Build

Requires the .NET 10 SDK; the published Windows x64 build includes its runtime.

```powershell
dotnet publish "Sample Applications/StickyNotesDemo/StickyNotesDemo.csproj" -c Release -r win-x64 --self-contained true --source https://api.nuget.org/v3/index.json -o publish/Stick-It
```

Markdown parsing uses [Markdig](https://github.com/xoofx/markdig), BSD-2-Clause, with a native WPF renderer. The supplied icon is retained in `Assets`.

## Background checks

```powershell
dotnet restore "Sample Applications/StickyNotesDemo/Checks/Checks.csproj" --source https://api.nuget.org/v3/index.json
dotnet run --no-restore --project "Sample Applications/StickyNotesDemo/Checks/Checks.csproj" -- "path/to/preview-output"
```

The checks use isolated temporary files and a unique mutex/event namespace. They cover process exclusion, activation signals, owner exit/crash, cursor placement calculations, persistence, legacy import, Markdown, search, and close-to-hide. Editor checks send WPF events within the test process and verify rapid typing, heading resets, list selections, nesting, undo/redo, and note storage. They also measure list alignment and spacing in offscreen WPF renders. Native-window checks create hidden handles to verify tool-window classification, transparency, topmost state and activation flags. The tests run without showing windows, starting a tray icon, registering a hotkey, or using desktop input. Alt+Tab and Win+Tab visibility still require checking in the target Windows shell.

To check the actual note height, paste and undo behavior, and the scrollbar threshold in native windows, run the following on Windows. It creates a separate desktop and verifies that the desktop does not receive user input before opening its test windows.

```powershell
dotnet run --no-restore --project "Sample Applications/StickyNotesDemo/Checks/Checks.csproj" -- fit-window
```

## Game mouse preview validation

The detector observes Windows cursor/foreground events and samples confinement every 50 ms. It never hides, moves or clips the user's cursor and never activates the game. Its policy is tested separately from its native window changes, including stale cursor events, a note supplying its own cursor, foreground changes, touch/pen suppression, multiple monitor coordinates and turning automatic mode off. Native hook start/stop and hidden note handles are checked without desktop input.

Real games still require user verification. The preview feature is available in the installed application at `D:\Stick It`; use its tray/settings toggle to enable or disable automatic passthrough. All builds share the same single-instance gate and note store.

For windowed, borderless fullscreen and exclusive fullscreen modes, verify the following with an open note over the game:

1. Enter view control: moving and clicking over the note must leave game input active.
2. Open a menu/backpack with a free visible pointer: the note must become clickable and editable.
3. Return to view control and repeat, including when the pointer is already over a note.
4. Switch to another application, use the new-note shortcut, and toggle automatic mode off: normal note interaction must be available.

This is an external detector, not a game-state API. A windowed game that uses only relative input/recentering without Windows confinement can be indistinguishable from ordinary cursor hiding. A software-cursor menu that keeps the Windows cursor hidden and never releases confinement can also be ambiguous. These cases are not claimed as verified; use the toggle to disable the preview if its detection does not match a game's input mode.
