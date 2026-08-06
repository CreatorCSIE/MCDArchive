namespace MCDArchive.Models;

/// <summary>持久化到 config/settings.json 的用户设置。</summary>
public class AppSettings
{
    public string VersionName { get; set; } = string.Empty;
    public string Language { get; set; } = "zh-CN";
    public string InstallPath { get; set; } = string.Empty;
}