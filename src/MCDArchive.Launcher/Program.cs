using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace MCDArchive.Launcher;

/// <summary>
/// 发布根目录的入口程序（entry.exe）。
/// 结构：根目录放本入口，游戏本体在根目录下的 app/ 文件夹。
/// 双击本入口 → 启动 app/MCDArchive.exe。
/// </summary>
internal static class Program
{
    // 单实例互斥锁：防止用户重复双击启动多个实例
    private static readonly Mutex SingleInstance = new(true, "MCDArchive.Launcher.SingleInstance");

    [STAThread]
    private static void Main()
    {
        if (!SingleInstance.WaitOne(0, false))
            return; // 已有实例在运行，立即退出

        try
        {
            Launch();
        }
        catch (Exception ex)
        {
            // 启动失败时给出提示，避免静默退出导致用户困惑
            MessageBoxW(IntPtr.Zero,
                "无法启动 Minecraft Dungeons Archive 启动器。\n\n" + ex.Message,
                "MCDArchive",
                0x10 /* MB_ICONERROR */);
        }
        finally
        {
            SingleInstance.ReleaseMutex();
        }
    }

    private static void Launch()
    {
        // 入口位于发布根目录，游戏本体在根目录下的 app/ 中
        string exe = Path.Combine(AppContext.BaseDirectory, "app", "MCDArchive.exe");

        // 开发环境直接运行本入口时，app/ 不存在，回退到入口同目录
        if (!File.Exists(exe))
            exe = Path.Combine(AppContext.BaseDirectory, "MCDArchive.exe");

        if (!File.Exists(exe))
            throw new FileNotFoundException("未找到 MCDArchive.exe：\n" + exe);

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = true
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}