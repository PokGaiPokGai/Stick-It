# Stick It

A Windows desktop sticky-note application forked from Microsoft's MIT-licensed WPF-Samples. The application project is `Sample Applications/StickyNotesDemo`.

## Usage

- One instance per Windows user/session, regardless of the executable's folder. A second launch signals the existing instance to open the library and exits before loading note files.
- **Shift + Alt + W** creates a new, pinned note centered on the mouse pointer. The position is constrained to that monitor's work area. The shortcut does not repeat while held; registration failures appear in the library.
- Individual notes never appear on the taskbar. The library has a taskbar entry only while it is open; closing the library hides it, leaving the tray and notes running.
- Click the tray icon to open the library. Its menu includes new note and save/exit.
- New notes start as 320 × 320 squares and open on top. Click the body to edit and reveal the close button. The body keeps the same position and size as the button appears or disappears. Drag the top strip or the paper margin to move a note. Right-click the paper to change its color or delete it.
- Closing a note hides it; click its library card to reopen it. Deletion requires confirmation.
- Click the note body to edit the formatted content in place. The bottom toolbar offers heading, bold, italic, underline, strikethrough, bulleted list, numbered list, task, and inline code. A filled button indicates an active format and an outlined button indicates a format applied to part of the selection. Click a task checkbox to change its state.
- Press **Ctrl + Shift + 7 / 8 / 9** for numbered, bulleted, or task lists. Use **Tab** and **Shift + Tab** (or **Ctrl + ] / [**) to change the indentation of the current item or selection. **Enter** continues a list, an empty item moves out one level, and **Shift + Enter** adds a line break within an item. Backspace at the start of an item also moves it out one level.
- Use **Ctrl + Alt + 1–6** for headings and **Ctrl + Alt + 0** for body text. Enter at the end of a heading starts a body paragraph. Deleting the entire heading text or pressing Backspace at the start of a heading resets that paragraph to body text.
- Use Markdown shortcuts while typing. For example, type `##` followed by a space for a heading or `**text**` followed by a space for bold. Pasting plain-text Markdown inserts formatted content at the selection.
- Notes save to Markdown and can reopen with headings, emphasis, lists, task states, quotes, inline and fenced code, links, and tables. Images are represented by their labels.
- Edits are saved after a 600 ms pause. Notes and open/closed state live in `%LOCALAPPDATA%\Stick It\notes.json`; the previous save is retained as `notes.json.bak`.
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

The checks use isolated temporary files and a unique mutex/event namespace. They cover process exclusion, activation signals, owner exit/crash, cursor placement calculations, persistence, legacy import, Markdown, search, and close-to-hide. Editor checks send WPF events within the test process and verify rapid typing, heading resets, list selections, nesting, undo/redo, and note storage. They also measure list alignment and spacing in offscreen WPF renders. The tests run without showing windows, starting a tray icon, registering a hotkey, or using desktop input.
