using System;
using Avalonia;

namespace MCDArchive;

sealed class Program
{
    // [STAThread] 是 Windows UI 应用程序必须的，确保 COM 线程模型正确
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // 生成 AppBuilder 的方法，也是预览器（Previewer）寻找的入口
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect() // 自动检测操作系统（Windows, Linux, macOS）
            .WithInterFont()    // 使用我们在 .csproj 中引用的 Inter 字体
            .LogToTrace();      // 将调试信息输出到 IDE 的输出窗口
}