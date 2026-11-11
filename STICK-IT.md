# Stick It

A desktop sticky-note app based on Microsoft's WPF StickyNotesDemo (MIT license).

Source: `Sample Applications/StickyNotesDemo`.

- Each note is an independent window, with AlwaysOnTop enabled for new notes.
- First launch opens a blank note. The app lives in the Windows notification area (system tray), with no launcher window. Right-click the icon to create a note, show all notes, or save and exit. Double-click it to show notes (or create one if none exist).
- Note data is stored in `%LOCALAPPDATA%\Stick It`.
- The note UI and periodic saves every 30 minutes are inherited from the sample. Use the tray menu to save and exit; closing an individual note removes it.
- The executable, tray and note windows use the supplied Stick It artwork.
- Stored dates are read using the same current culture used by the original sample to write them.

Build with the .NET 10 SDK:

```powershell
dotnet publish "Sample Applications/StickyNotesDemo/StickyNotesDemo.csproj" -c Release -r win-x64 --self-contained true --source https://api.nuget.org/v3/index.json -o publish/Stick-It
```

The rest of the upstream WPF-Samples repository is retained as part of the fork.
