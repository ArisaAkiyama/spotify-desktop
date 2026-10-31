using System.Text.Json.Serialization;

namespace SpotifyDesktop.Models;

public sealed record MediaPlaybackState
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; init; } = string.Empty;

    [JsonPropertyName("albumArt")]
    public string AlbumArtUrl { get; init; } = string.Empty;

    [JsonPropertyName("isPlaying")]
    public bool IsPlaying { get; init; }

    [JsonPropertyName("currentTime")]
    public double CurrentSeconds { get; init; }

    [JsonPropertyName("duration")]
    public double DurationSeconds { get; init; }
}
