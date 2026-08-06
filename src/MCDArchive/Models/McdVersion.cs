using System.Text.Json.Serialization;

namespace MCDArchive.Models;

public class McdVersion
{
    [JsonPropertyName("VersionName")]
    public string VersionName { get; set; } = string.Empty;

    [JsonPropertyName("VersionLink")]
    public string VersionLink { get; set; } = string.Empty;
}