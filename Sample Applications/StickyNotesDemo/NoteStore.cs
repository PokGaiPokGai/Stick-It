using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;

namespace StickyNotesDemo;

public sealed class NoteData
{
    internal const string DefaultColor = "#FFF7C2";
    internal static readonly string[] Colors = [DefaultColor, "#E2F1FF", "#F5E0F0", "#E4F2D5", "#EEE8FB", "#F0F0F0"];

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Markdown { get; set; } = "";
    public string Color { get; set; } = DefaultColor;
    public DateTime Modified { get; set; } = DateTime.Now;
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 320;
    // Cached reading height. Older files may contain the entire window height;
    // opening always measures the document at its current width.
    public double Height { get; set; } = 320;
    public bool IsOpen { get; set; } = true;
}

public sealed record NoteSettings
{
    internal static readonly int[] FontPercentages = Enumerable.Range(75, 200 - 75 + 1)
        .Where(percent => percent % 5 == 0).ToArray();
    public int FontSizePercent { get; init; } = 100;
    public bool RandomNewNoteColor { get; init; } = true;

    internal NoteData CreateNote(Random random) => new()
    {
        Color = RandomNewNoteColor ? NoteData.Colors[random.Next(NoteData.Colors.Length)] : NoteData.DefaultColor
    };
}

public sealed class NoteStore
{
    private readonly string directory;
    public NoteStore(string directory) { this.directory = directory; }
    public NoteSettings LoadSettings()
    {
        var path = Path.Combine(directory, "settings.json");
        if (!File.Exists(path)) return new NoteSettings();
        var settings = JsonSerializer.Deserialize<NoteSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("设置数据为空。");
        if (!NoteSettings.FontPercentages.Contains(settings.FontSizePercent))
            throw new InvalidDataException("字体大小设置无效。");
        return settings;
    }

    public void SaveSettings(NoteSettings settings)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public List<NoteData> Load()
    {
        var path = Path.Combine(directory, "notes.json");
        if (File.Exists(path))
        {
            var notes = JsonSerializer.Deserialize<List<NoteData>>(File.ReadAllText(path))
                ?? throw new InvalidDataException("便签数据为空。");
            var ids = new HashSet<Guid>();
            foreach (var note in notes)
                if (note == null || note.Markdown == null || !ids.Add(note.Id) ||
                    !double.IsFinite(note.Left) || !double.IsFinite(note.Top) ||
                    !double.IsFinite(note.Width) || !double.IsFinite(note.Height) || note.Width < 160 || note.Height <= 0 ||
                    note.Color == null || note.Color.Length != 7 || note.Color[0] != '#' ||
                    !int.TryParse(note.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException("便签数据格式不正确。");
            return notes;
        }
        return ImportLegacy();
    }

    public void Save(List<NoteData> notes)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "notes.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(notes, new JsonSerializerOptions { WriteIndented=true }));
        if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak");
        else File.Move(path + ".tmp", path);
    }

    // Original sample files are left untouched so their rich-text data remains available.
    private List<NoteData> ImportLegacy()
    {
        var result = new List<NoteData>();
        var index = Path.Combine(directory, "NotesFile");
        if (!File.Exists(index)) return result;
        var lines = File.ReadAllLines(index);
        var count = int.Parse(lines[0].Split('|')[0]);
        var colors = new[] { "#EEEEEE", "#F2DEFA", "#E2F1FF", "#E4F2D5", "#FFF7C2", "#FFDCCB", "#E8DDF4", "#FADDDD" };
        for (int i=0; i<count; i++)
        {
            var fields = lines[i+1].Split('|');
            var path = Path.Combine(directory, i.ToString());
            var doc = new FlowDocument();
            var text = new TextRange(doc.ContentStart, doc.ContentEnd);
            using (var stream = File.OpenRead(path)) text.Load(stream, fields[0].Trim() == "0" ? DataFormats.XamlPackage : DataFormats.Xaml);
            var color = int.Parse(fields[6]);
            result.Add(new NoteData {
                Markdown = text.Text.TrimEnd('\r', '\n'), Modified = File.GetLastWriteTime(path),
                Left = double.Parse(fields[2]), Top = double.Parse(fields[1]),
                Width = Math.Max(200, double.Parse(fields[4])), Height = Math.Max(160, double.Parse(fields[3])),
                Color = color == -1 ? "#FFF7C2" : colors[color], IsOpen=true
            });
        }
        return result;
    }
}
