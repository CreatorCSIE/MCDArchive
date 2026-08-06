using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Svg.Skia;
using MCDArchive.ViewModels;
using MCDArchive.Views;

namespace MCDArchive;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 保持 SVG 程序集存活，确保 Svg 控件在运行时正常加载
        GC.KeepAlive(typeof(SvgImage).Assembly);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 这里将你的 MainWindow 和 MainViewModel 绑定起来
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}