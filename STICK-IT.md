# Stick It

A Windows desktop sticky-note application forked from Microsoft's MIT-licensed WPF-Samples. The application project is `Sample Applications/StickyNotesDemo`.

## Usage

- One instance per Windows user/session, regardless of the executable's folder. A second launch signals the existing instance to open the library and exits before loading note files.
- **Shift + Alt + W** creates a new, pinned note centered on the mouse pointer. The position is constrained to that monitor's work area. The shortcut does not repeat while held; registration failures appear in the library.
- Individual notes never appear on the taskbar. The library has a taskbar entry only while it is open; closing the library hides it, leaving the tray and notes running.
- Click the tray icon to open the library. Its menu includes new note and save/exit.
- Notes are solid-color rounded rectangles. When not editing, only the paper and content remain: the toolbar and its reserved height are removed. Clicking the body reveals editing controls; moving focus away hides them. The paper margin can be dragged to move a note. Their menu changes the paper color or explicitly deletes the note. The pin button controls always-on-top.
- Closing a note hides it; click its library card to reopen it. Deletion requires confirmation.
- Click the note body to edit Markdown. Moving focus away renders it automatically. There is no edit/preview mode button.
- Markdown supports headings, emphasis, strikethrough, lists, task-list marks, quotes, inline/fenced code, links and tables. Task marks are edited as Markdown text. Remote images and embedded HTML are not rendered.
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

The checks use isolated temporary files and a unique mutex/event namespace. They cover concurrent process exclusion, activation signals, owner exit/crash, cursor placement calculations, persistence/legacy import, Markdown rendering, search and close-to-hide logic. WPF visuals are rendered to images without showing windows. They do not start a tray icon, register a hotkey or use desktop input. Actual Windows taskbar/tray interaction, foreground activation and mixed-DPI pointer placement still require an interactive check.
