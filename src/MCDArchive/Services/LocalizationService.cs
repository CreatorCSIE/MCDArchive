using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MCDArchive.Services;

/// <summary>
/// 本地化服务：从 lang 目录加载指定语言的 json 翻译表，并按 key 提供文案。
/// 提供 INotifyPropertyChanged，切换语言后触发通知以便 XAML 中的索引器绑定刷新。
/// </summary>
public partial class LocalizationService : ObservableObject
{
    public static LocalizationService Instance { get; } = new();

    private Dictionary<string, string> _table = new();

    /// <summary>主动调用以确保静态初始化（内置英文回退表）在使用前就绪。</summary>
    private static readonly Dictionary<string, string> EnglishFallback = new()
    {
        ["_LanguageName"] = "English",
        ["App_Title"] = "Minecraft Dungeons Archive Launcher",
        ["Nav_Home"] = "Home",
        ["Nav_Versions"] = "Versions",
        ["Nav_Settings"] = "Settings",
        ["Nav_About"] = "About",
        ["Home_Title"] = "Minecraft Dungeons Archive Launcher",
        ["Home_Ready"] = "Ready",
        ["Home_Launch"] = "Launch Game",
        ["Home_Download"] = "Download {0}",
        ["Home_Repair"] = "Repair Game",
        ["Home_Repair_Status"] = "Repairing {0}, verifying and re-downloading damaged or missing files...",
        ["Home_Resume"] = "Resume Download",
        ["Home_Resume_Status"] = "Resuming download of {0}, continuing unfinished parts...",
        ["Versions_Title"] = "Versions",
        ["Versions_Subtitle"] = "Select a game version to download or play.",
        ["Versions_Loaded"] = "{0} versions loaded locally",
        ["Versions_Loaded_Network"] = "{0} versions loaded",
        ["Settings_Title"] = "Settings",
        ["Settings_InstallPath"] = "Game Install Path:",
        ["Settings_Watermark"] = "Select the game install or completion directory",
        ["Settings_Browse"] = "Browse...",
        ["Settings_FixGameFiles"] = "Repair Game Files",
        ["Settings_FixRuntime"] = "Repair Runtime",
        ["Settings_Language"] = "Language:",
        ["About_Title"] = "About Minecraft Dungeons Archive Launcher",
        ["About_ViewLicense"] = "View License",
        ["About_GplNotice"] = "This project is open source under the GNU General Public License v3.0",
        ["About_Links"] = "Links",
        ["About_ProjectHome"] = "Project Home",
        ["About_ExpanderIssues"] = "Having issues?",
        ["About_Feedback"] = "Feedback",
        ["About_SubmitIssues"] = "Submit Issues",
        ["Dialog_OK"] = "OK",
        ["Dialog_Runtime_Title"] = "Runtime Check",
        ["Runtime_AlreadyInstalled"] = "VC++ runtime detected, no installation needed.",
        ["Runtime_InstallSuccess"] = "VC++ runtime installed successfully.",
        ["Runtime_InstallFailed"] = "VC++ runtime installation failed or was cancelled. Please install the runtime manually and then launch the game.",
        ["Runtime_Installing"] = "Installing VC++ runtime automatically, please watch for the administrator authorization prompt...",
        ["Runtime_Ready"] = "Installation complete. Click Launch Game to start.",
        ["Dialog_Repair_Title"] = "Repair Game Files",
        ["Repair_NotNeeded"] = "Game files are complete, no repair needed.",
        ["Repair_Interrupted"] = "There is an unfinished download. Please resume it first.",
        ["Repair_NotInstalled"] = "No game files detected. Please download the game first.",
        ["Repair_Complete"] = "Game files repaired successfully.",
        ["Dialog_LinkError_Title"] = "Failed to Open Link",
        ["Dialog_LinkError_Content"] = "Unable to open the link: {0}. Please check your default browser settings and try again.",
    };

    /// <summary>按 key 取翻译文案；key 不存在时原样返回 key。</summary>
    public string this[string key] =>
        _table.TryGetValue(key, out var value) ? value : key;

    /// <summary>加载指定语言（langCode 对应 lang\{langCode}.json）。</summary>
    public void Load(string langDir, string langCode)
    {
        var path = Path.Combine(langDir, $"{langCode}.json");
        var table = new Dictionary<string, string>();
        if (File.Exists(path))
        {
            try
            {
                table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                        ?? new Dictionary<string, string>();
            }
            catch
            {
                // 解析失败时保持空表，界面会回退到 key
            }
        }

        // 最后防御：语言目录被误删/文件缺失或解析失败时，回退到内置英文表，保证界面可读
        if (table.Count == 0)
            table = EnglishFallback;

        _table = table;
        // 触发所有索引器绑定重新求值
        OnPropertyChanged("");
    }
}