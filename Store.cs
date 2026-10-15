using System.Text.Json;

namespace PathJump;

public class PathEntry
{
    public string Path { get; set; }
    public string Label { get; set; }
    public bool Pinned { get; set; }
    public DateTime LastUsed { get; set; }
    public int Uses { get; set; }
}

public class Settings
{
    public string PanelHotkey { get; set; } = "Alt+~";
    public string QuickJumpHotkey { get; set; } = "Ctrl+Alt+G";
    public int MaxRecent { get; set; } = 60;
}

/// <summary>
/// Saved paths and settings, kept as JSON in a "Data" folder next to PathJump.exe so the whole
/// folder can be zipped and moved. Falls back to %APPDATA%\PathJump if that folder isn't writable.
/// </summary>
public class Store
{
    public static readonly string DataDir = ResolveDataDir();

    static string ResolveDataDir()
    {
        var appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PathJump");
        var portable = System.IO.Path.Combine(AppContext.BaseDirectory, "Data");
        try
        {
            Directory.CreateDirectory(portable);
            var probe = System.IO.Path.Combine(portable, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);

            // First portable run: bring over the list and settings from the old %APPDATA% location.
            if (!File.Exists(System.IO.Path.Combine(portable, "paths.json")))
                foreach (var name in new[] { "paths.json", "settings.json" })
                {
                    var old = System.IO.Path.Combine(appData, name);
                    var dest = System.IO.Path.Combine(portable, name);
                    if (File.Exists(old) && !File.Exists(dest)) File.Copy(old, dest);
                }
            return portable;
        }
        catch
        {
            return appData;
        }
    }
    static string PathsFile => System.IO.Path.Combine(DataDir, "paths.json");
    static string SettingsFile => System.IO.Path.Combine(DataDir, "settings.json");
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "Ctrl+Alt+V" readable
    };

    public List<PathEntry> Entries { get; private set; } = new();
    public Settings Settings { get; private set; } = new();

    public static Store Load()
    {
        var s = new Store();
        try { if (File.Exists(PathsFile)) s.Entries = JsonSerializer.Deserialize<List<PathEntry>>(File.ReadAllText(PathsFile)) ?? new(); } catch { }
        try { if (File.Exists(SettingsFile)) s.Settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new(); } catch { }
        s.Entries.RemoveAll(e => string.IsNullOrWhiteSpace(e.Path));
        s.Save(); // writes settings.json on first run so it can be edited
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(PathsFile, JsonSerializer.Serialize(Entries, Json));
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(Settings, Json));
        }
        catch { }
    }

    public static bool LooksLikePath(string p) =>
        p != null && p.Length >= 3 &&
        ((char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/')) || p.StartsWith(@"\\"));

    public static string Normalize(string p)
    {
        if (!LooksLikePath(p)) return null;
        try { p = System.IO.Path.GetFullPath(p); } catch { return null; }
        if (p.Length > 3) p = p.TrimEnd('\\');
        return p;
    }

    public static string DefaultLabel(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    public static string LabelOf(PathEntry e) => string.IsNullOrWhiteSpace(e.Label) ? DefaultLabel(e.Path) : e.Label;

    public PathEntry Find(string path) =>
        Entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the path, or bumps it to the top of Recent if it is already known.</summary>
    public PathEntry Touch(string path)
    {
        path = Normalize(path);
        if (path == null) return null;
        var e = Find(path);
        if (e == null) { e = new PathEntry { Path = path }; Entries.Add(e); }
        e.LastUsed = DateTime.Now;
        e.Uses++;
        TrimRecent();
        Save();
        return e;
    }

    public void SetPinned(string path, bool pinned)
    {
        var e = Find(path) ?? Touch(path);
        if (e == null) return;
        e.Pinned = pinned;
        Save();
    }

    public void Rename(string path, string label)
    {
        var e = Find(path) ?? Touch(path);
        if (e == null) return;
        e.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Save();
    }

    public void Remove(string path)
    {
        Entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    void TrimRecent()
    {
        var extra = Entries.Where(e => !e.Pinned).OrderByDescending(e => e.LastUsed).Skip(Math.Max(5, Settings.MaxRecent)).ToList();
        foreach (var e in extra) Entries.Remove(e);
    }
}
