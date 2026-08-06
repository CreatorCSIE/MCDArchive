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
        _table = table;
        // 触发所有索引器绑定重新求值
        OnPropertyChanged("");
    }
}