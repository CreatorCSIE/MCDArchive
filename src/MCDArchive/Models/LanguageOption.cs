namespace MCDArchive.Models;

/// <summary>语言选项：语言代码 + 用于下拉菜单显示的本地名称。</summary>
public record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}