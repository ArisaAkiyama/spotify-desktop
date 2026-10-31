using System.IO;
using System.Text.Json;
using SpotifyDesktop.Models;

namespace SpotifyDesktop.Services;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public AppSettings Current { get; private set; }

    public AppSettingsService()
    {
        string appFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpotifyDesktop");

        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "settings.json");
        Current = Load();
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    Current = loaded;
                    return Current;
                }
            }
        }
        catch
        {
            // Fallback to default settings if file is corrupted
        }

        Current = new AppSettings();
        Current.Normalize();
        return Current;
    }

    public void Save(AppSettings settings)
    {
        Current = settings.Clone();
        Current.Normalize();
        try
        {
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            string tempPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }
        catch
        {
            // Ignore transient IO lock errors
        }
    }

    public string ToJson() => JsonSerializer.Serialize(Current);
}
