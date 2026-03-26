using System.Text.Json.Serialization;

namespace VideoWallpaper.Models;

public class AppConfig
{
    [JsonPropertyName("videoPath")]
    public string? VideoPath { get; set; }

    [JsonPropertyName("monitorDevice")]
    public string? MonitorDevice { get; set; }

    [JsonPropertyName("isPlaying")]
    public bool IsPlaying { get; set; }

    [JsonPropertyName("autostart")]
    public bool Autostart { get; set; }
}
