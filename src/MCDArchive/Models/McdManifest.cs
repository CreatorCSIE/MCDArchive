using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MCDArchive.Models;

public class McdManifest
{
    [JsonPropertyName("files")]
    public Dictionary<string, ManifestFile>? Files { get; set; }
}

public class ManifestFile
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("downloads")]
    public FileDownloads? Downloads { get; set; }
}

public class FileDownloads
{
    [JsonPropertyName("raw")]
    public DownloadInfo? Raw { get; set; }

    [JsonPropertyName("lzma")]
    public DownloadInfo? Lzma { get; set; }
}

public class DownloadInfo
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }
}