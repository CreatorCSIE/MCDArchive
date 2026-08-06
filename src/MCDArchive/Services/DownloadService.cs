using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MCDArchive.Models;

namespace MCDArchive.Services;

public class DownloadService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// 断点续传式下载。
    /// 先写入 "{destinationPath}.part"，完成后经 SHA-1 校验再原子重命名为最终文件。
    /// 若 .part 已存在则通过 HTTP Range 从断点处续传。
    /// </summary>
    /// <returns>true 表示本次会话新下载完成了该文件；false 表示文件原本已完整（跳过）。</returns>
    public async Task<bool> DownloadFileResumableAsync(
        string url,
        string destinationPath,
        string? expectedSha1,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var partPath = destinationPath + ".part";
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // 1. 最终文件已存在且校验通过 → 直接跳过（不重复计入本次进度）
        if (File.Exists(destinationPath))
        {
            if (string.IsNullOrEmpty(expectedSha1) || await VerifySha1Async(destinationPath, expectedSha1))
                return false;
            // 最终文件损坏 → 删除，走重下流程
            File.Delete(destinationPath);
        }

        // 2. 尝试断点续传
        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        // 服务器不支持 Range（返回 200 而非 206）时从头下载
        if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            existing = 0;

        await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(
            partPath, existing > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            progress?.Report(bytesRead);
        }

        // 3. 校验并转正
        if (!string.IsNullOrEmpty(expectedSha1) && !await VerifySha1Async(partPath, expectedSha1))
        {
            File.Delete(partPath);
            throw new InvalidDataException($"SHA-1 校验失败: {Path.GetFileName(destinationPath)}");
        }

        File.Move(partPath, destinationPath, true);
        return true;
    }

    /// <summary>供外部预扫描已存在文件是否完整。</summary>
    public async Task<bool> VerifyFileAsync(string filePath, string? expectedSha1)
    {
        if (!File.Exists(filePath)) return false;
        if (string.IsNullOrEmpty(expectedSha1)) return true;
        return await VerifySha1Async(filePath, expectedSha1);
    }

    private static async Task<bool> VerifySha1Async(string filePath, string expectedSha1)
    {
        try
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = await sha1.ComputeHashAsync(stream);
            var hashString = Convert.ToHexString(hashBytes).ToLower();
            return hashString.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}