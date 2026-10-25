using System.IO;
using System.Text.Json;

namespace Serial2CNC.Presentation;

public sealed record DesktopSettings
{
    public TransferFormValues? Transfer { get; init; }
    public ThemePreference Theme { get; init; }
    public bool WebEnabled { get; init; }
    public int WebPort { get; init; } = 62899;

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Serial2CNC");
    private static string SettingsPath => Path.Combine(DataDirectory, "desktop-settings.json");

    public static DesktopSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new DesktopSettings();
        }
        var settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(SettingsPath))
            ?? throw new JsonException("The desktop settings file is empty.");
        if (settings.WebPort is < 1 or > 65535 || !Enum.IsDefined(settings.Theme))
        {
            throw new JsonException("The desktop settings contain an invalid port or theme.");
        }
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
