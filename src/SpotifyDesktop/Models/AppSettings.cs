using System.Text.Json.Serialization;

namespace SpotifyDesktop.Models;

public sealed class AppSettings
{
    [JsonPropertyName("dolbyEnabled")]
    public bool DolbyEnabled { get; set; } = false;

    [JsonPropertyName("dolbyDeviceMode")]
    public string DolbyDeviceMode { get; set; } = "Headphones";

    [JsonPropertyName("dolbyPreset")]
    public string DolbyPreset { get; set; } = "Music";

    [JsonPropertyName("surroundLevel")]
    public int SurroundLevel { get; set; } = 70;

    [JsonPropertyName("bassLevel")]
    public int BassLevel { get; set; } = 68;

    [JsonPropertyName("clarityLevel")]
    public int ClarityLevel { get; set; } = 65;

    [JsonPropertyName("dynamicIslandEnabled")]
    public bool DynamicIslandEnabled { get; set; } = true;

    public void Normalize()
    {
        SurroundLevel = Math.Clamp(SurroundLevel, 0, 100);
        BassLevel = Math.Clamp(BassLevel, 0, 100);
        ClarityLevel = Math.Clamp(ClarityLevel, 0, 100);

        if (!string.Equals(DolbyDeviceMode, "LaptopSpeakers", StringComparison.OrdinalIgnoreCase))
        {
            DolbyDeviceMode = "Headphones";
        }

        if (string.IsNullOrWhiteSpace(DolbyPreset))
        {
            DolbyPreset = "Music";
        }
        else if (!string.Equals(DolbyPreset, "Music", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(DolbyPreset, "Cinema", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(DolbyPreset, "BassBoost", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(DolbyPreset, "Vocal", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(DolbyPreset, "Dynamic", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(DolbyPreset, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            DolbyPreset = "Music";
        }
    }

    public AppSettings Clone() => new()
    {
        DolbyEnabled = DolbyEnabled,
        DolbyDeviceMode = DolbyDeviceMode,
        DolbyPreset = DolbyPreset,
        SurroundLevel = SurroundLevel,
        BassLevel = BassLevel,
        ClarityLevel = ClarityLevel,
        DynamicIslandEnabled = DynamicIslandEnabled
    };
}
