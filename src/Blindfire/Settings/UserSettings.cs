using System.IO;
using System.Text.Json;

namespace Blindfire.Settings;

public sealed record UserSettings(double FovDegrees, double MouseDpi, bool RandomClickSoundsEnabled = false)
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Blindfire", "settings.json");

    public static UserSettings? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<UserSettings>(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persistence is a convenience, not a requirement - a failed save
            // just means settings won't be remembered next launch.
        }
    }
}
