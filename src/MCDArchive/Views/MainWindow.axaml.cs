using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using FluentAvalonia.UI.Controls;
using MCDArchive.ViewModels;

namespace MCDArchive.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 设置窗口图标（左上角），来自嵌入的 favicon.ico
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://MCDArchive/favicon.ico")));

        // 如果需要，可以在这里通过代码设置窗口在不同平台下的特殊属性
        #if DEBUG
                this.AttachDevTools(); // 仅在调试模式下允许按 F12 打开调试工具
        #endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // 侧边栏导航切换：根据选中的菜单项切换到对应页面
    private void NavView_SelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // 侧边栏文本已本地化，改用 Tag 判断当前菜单项
        switch ((e.SelectedItem as NavigationViewItem)?.Tag?.ToString())
        {
            case "Home": vm.CurrentPage = PageKind.Home; break;
            case "Versions": vm.CurrentPage = PageKind.Versions; break;
            case "Settings": vm.CurrentPage = PageKind.Settings; break;
            case "About": vm.CurrentPage = PageKind.About; break;
        }
    }

    // 打开超链接
    private void OpenLink_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string url } && !string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}