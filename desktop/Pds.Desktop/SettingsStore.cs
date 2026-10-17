using System.IO;
using System.Text.Json;
using Pds.Core;

namespace Pds.Desktop;

public sealed class UserSettings
{
    // Legacy fields remain in backups for compatibility; the UI always uses the fixed window.
    public bool Compact { get; set; }
    public bool Dark { get; set; }
    public bool Pinned { get; set; }
    public bool MeditationEnabled { get; set; } = true;
    public string Accent { get; set; } = "#E3BACA";
    public string? MeditationDate { get; set; }
    public HashSet<string> MeditationShownDates { get; set; } = [];
    public bool TrayHintShown { get; set; }
    public bool AutoStart { get; set; } = true;
    public double GeneralWidth { get; set; } = 900;
    public double GeneralHeight { get; set; } = 880;
    public double CompactWidth { get; set; } = 600;
    public double CompactHeight { get; set; } = 450;
    public double? Left { get; set; }
    public double? Top { get; set; }

    public void Validate()
    {
        if (Accent is null || !System.Text.RegularExpressions.Regex.IsMatch(Accent, "^#[0-9a-fA-F]{6}$") ||
            new[] { GeneralWidth, GeneralHeight, CompactWidth, CompactHeight }.Any(v => !double.IsFinite(v) || v < 100 || v > 50000) ||
            Left is double left && !double.IsFinite(left) || Top is double top && !double.IsFinite(top) ||
            MeditationDate is not null && !ValidDate(MeditationDate) || MeditationShownDates is null || MeditationShownDates.Count > 36600 || MeditationShownDates.Any(d => !ValidDate(d)))
            throw new InvalidDataException("설정 값이 올바르지 않아요.");
    }
    private static bool ValidDate(string text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
    public void CopyFrom(UserSettings source)
    { foreach (var property in typeof(UserSettings).GetProperties()) property.SetValue(this, property.GetValue(source)); }
}

public sealed class SettingsStore(string path, PlanRepository? repository = null)
{
    public UserSettings Load()
    {
        try
        {
            var json = repository?.LoadPreferencesJson() ?? (File.Exists(path) ? File.ReadAllText(path) : null);
            var result = json is null ? new() : JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings(); result.Validate(); return result;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(UserSettings value)
    {
        value.Validate();
        if (repository is not null) { repository.SavePreferencesJson(JsonSerializer.Serialize(value)); return; }
        var temp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
