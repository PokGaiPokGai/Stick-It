# Stick It

A desktop sticky-note app based on Microsoft's WPF StickyNotesDemo (MIT license).

Source: `Sample Applications/StickyNotesDemo`.

- Each note is an independent window, with AlwaysOnTop enabled for new notes.
- First launch opens a blank note; use the small + New note window to add more.
- Note data is stored in `%LOCALAPPDATA%\Stick It`.
- This initial build preserves the upstream sample UI and save behavior: periodic saves every 30 minutes and saving when the small main window is closed. Close that main window to exit and save; closing an individual note removes it.

Build with the .NET 10 SDK:

```powershell
dotnet publish "Sample Applications/StickyNotesDemo/StickyNotesDemo.csproj" -c Release -r win-x64 --self-contained true --source https://api.nuget.org/v3/index.json -o publish/Stick-It
```

The rest of the upstream WPF-Samples repository is retained as part of the fork.
