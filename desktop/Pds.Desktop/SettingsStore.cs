using System.IO;
using System.Text.Json;

namespace Pds.Desktop;

public sealed class UserSettings
{
    public bool Compact { get; set; }
    public bool Dark { get; set; }
    public bool Pinned { get; set; }
    public bool MeditationEnabled { get; set; } = true;
    public string Accent { get; set; } = "#FFEEA0";
    public string? MeditationDate { get; set; }
    public double GeneralWidth { get; set; } = 940;
    public double GeneralHeight { get; set; } = 700;
    public double CompactWidth { get; set; } = 600;
    public double CompactHeight { get; set; } = 450;
    public double? Left { get; set; }
    public double? Top { get; set; }
}

public sealed class SettingsStore(string path)
{
    public UserSettings Load()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(UserSettings value)
    {
        var temp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
