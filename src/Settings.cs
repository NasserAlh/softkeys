using System.IO;
using System.Text.Json;

namespace Softkeys;

/// <summary>
/// Persisted appearance state (F-10): background color, opacity, window size,
/// capture-exclusion toggle. JSON via System.Text.Json (D-07) under
/// %APPDATA%\softkeys\settings.json. Corrupt or missing files fall back to
/// defaults silently — no dialogs, ever; saving is best-effort the same way.
/// </summary>
public sealed class Settings
{
    public string BackgroundColor { get; set; } = "Black";
    public double Opacity { get; set; } = 0.90;
    public double WindowWidth { get; set; } = 900;
    public double WindowHeight { get; set; } = 414; // six rows per D-13 + F-22 strip (min 300)
    public bool CaptureExcluded { get; set; } = true;

    /// <summary>
    /// F-22 (A4/D-31): English word suggestions. Defaults on, matching the
    /// Approver's brief. Files written before A4 lack the key and therefore
    /// deserialize to true, which is the intended upgrade behaviour.
    /// </summary>
    public bool Prediction { get; set; } = true;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "softkeys", "settings.json");

    public static Settings Load() => Load(DefaultPath);

    internal static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new Settings();

            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    public void Save() => Save(DefaultPath);

    internal void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(
                this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort per F-10
        }
    }
}
