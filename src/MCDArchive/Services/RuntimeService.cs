using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MCDArchive.Services;

public class RuntimeService
{
    // 微软官方 VC++ 2015-2022 x64 Redistributable 下载直链
    private const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");

    /// <summary>
    /// 检测系统是否已安装 VC++ 2015-2022 (x64)
    /// </summary>
    public bool IsVcRedistInstalled()
    {
        try
        {
            // 检查 2015-2022 的标准注册表项
            // 14.0 代表 2015 之后的统一版本系列
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            if (key != null)
            {
                var installed = key.GetValue("Installed");
                return installed != null && (int)installed == 1;
            }
        }
        catch { /* 处理权限不足或注册表不存在的情况 */ }
        return false;
    }

    /// <summary>
    /// 静默下载并安装运行库
    /// </summary>
    public async Task<bool> InstallVcRedistAsync(Action<string>? statusCallback = null)
    {
        try
        {
            statusCallback?.Invoke("正在下载 VC++ 运行库 (微软官方)...");
            
            using (var client = new HttpClient())
            {
                var data = await client.GetByteArrayAsync(VcRedistUrl);
                await File.WriteAllBytesAsync(_tempPath, data);
            }

            statusCallback?.Invoke("正在静默安装运行库，请稍候...");

            var startInfo = new ProcessStartInfo
            {
                FileName = _tempPath,
                // /install: 安装; /quiet: 无需用户交互; /norestart: 不自动重启电脑
                Arguments = "/install /quiet /norestart",
                UseShellExecute = true, // 提升权限需要
                Verb = "runas"           // 申请管理员权限执行安装程序
            };

            var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                // 0 = 安装成功; 3010 = 安装成功但需重启; 1638 = 已存在另一版本（可视为已安装成功）
                bool ok = process.ExitCode == 0 || process.ExitCode == 3010 || process.ExitCode == 1638;
                if (!ok) statusCallback?.Invoke($"运行库安装失败 (退出码 {process.ExitCode})，请手动安装。");
                else statusCallback?.Invoke("运行库安装完成。");
                return ok;
            }

            statusCallback?.Invoke("无法启动运行库安装程序。");
            return false;
        }
        // 用户点了 UAC 弹窗的“否”，错误码 1223
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            statusCallback?.Invoke("已取消安装 VC++ 运行库（未授予管理员权限）。");
            return false;
        }
        catch (Exception ex)
        {
            // 把错误信息回传到 UI，避免静默退出
            statusCallback?.Invoke($"安装 VC++ 运行库失败: {ex.Message}");
            return false;
        }
        finally
        {
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
        }
    }
}