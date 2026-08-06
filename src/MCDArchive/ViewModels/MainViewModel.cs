using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using MCDArchive.Models;
using MCDArchive.Services;

namespace MCDArchive.ViewModels;

public enum PageKind { Home, Versions, Settings, About }

/// <summary>游戏安装状态：未安装 / 已安装且完整 / 已安装但不完整（需修复） / 有未完成下载（可恢复）。</summary>
public enum GameStatus { NotInstalled, Installed, NeedsRepair, Interrupted }

public partial class MainViewModel : ObservableObject
{
    private readonly DownloadService _downloadService = new();
    private readonly RuntimeService _runtimeService = new();
    private CancellationTokenSource? _cts;

    // 断点续传 / 取消清理所需状态
    private sealed record DownloadPlanItem(string RelPath, string Url, string? Sha1, long Size);
    private List<DownloadPlanItem>? _downloadPlan;
    private long _totalBytes;
    private long _preCountedBytes;
    private int _preCountedCount;
    private DateTime _startTime;
    private bool _cleanupOnCancel;
    private readonly object _partLock = new();
    private readonly HashSet<string> _partFiles = new();

    [ObservableProperty] private ObservableCollection<McdVersion> _versions = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    private McdVersion? _selectedVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameInstalled), nameof(PrimaryActionText))]
    private string _installPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Minecraft Dungeons"));

    // --- 设置持久化与游戏状态 ---
    private string SettingsPath => Path.Combine(ConfigDir, "settings.json");
    private bool _settingsLoaded;
    private string _savedVersionName = string.Empty;

    private GameStatus _gameStatus = GameStatus.NotInstalled;
    private int _statusCheckSeq; // 用于丢弃过期的检测结果

    // 监听安装目录变化，让“修复/下载/启动”按钮随目录删除/清空自动刷新
    private FileSystemWatcher? _watcher;
    private DateTime _lastStatusCheckTime = DateTime.MinValue;
    private readonly object _statusLock = new();

    /// <summary>游戏是否已安装且完整（exe 校验通过）。</summary>
    public bool IsGameInstalled => _gameStatus == GameStatus.Installed;

    /// <summary>游戏不完整（存在文件但 exe 校验失败/缺失），需要修复。</summary>
    public bool NeedsRepair => _gameStatus == GameStatus.NeedsRepair;

    // --- 侧边栏导航 ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome), nameof(IsVersions), nameof(IsSettings), nameof(IsAbout))]
    private PageKind _currentPage = PageKind.Home;

    public bool IsHome => CurrentPage == PageKind.Home;
    public bool IsVersions => CurrentPage == PageKind.Versions;
    public bool IsSettings => CurrentPage == PageKind.Settings;
    public bool IsAbout => CurrentPage == PageKind.About;

    /// <summary>侧边栏宽度：展开 220，收缩 64。</summary>
    public double SidebarWidth => IsSidebarCollapsed ? 64 : 220;

    /// <summary>首页主按钮文案：已安装则“启动游戏”，不完整则“修复游戏”，否则“下载游戏 {版本}”。</summary>
    public string PrimaryActionText => _gameStatus switch
    {
        GameStatus.Installed => Loc["Home_Launch"],
        GameStatus.NeedsRepair => Loc["Home_Repair"],
        GameStatus.Interrupted => Loc["Home_Resume"],
        _ => string.Format(Loc["Home_Download"], SelectedVersion?.VersionName ?? "..."),
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand), nameof(ResumeCommand), nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand), nameof(ResumeCommand), nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    private bool _isPaused;

    /// <summary>是否正在执行下载（闲置/加载/暂停时均为 false，用于控制进度文字的显隐）。</summary>
    public bool IsDownloading => IsBusy && !IsPaused;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _versionLoadInfo = "正在加载版本...";
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private string _progressDetailText = "0/0 项 (0 MB / 0 MB)";
    [ObservableProperty] private string _speedText = "0.00 MB/s";

    // 已加载的版本数量，用于语言切换时重新格式化“已加载 N 个版本”提示
    private int _versionsLoadedCount;

    private string ConfigDir => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "config"));

    // 语言目录与 config 同级（无需再进入上一级）
    private string LangDir => Path.GetFullPath(Path.Combine(ConfigDir, "..", "lang"));

    /// <summary>本地化服务单例，供 XAML 索引器绑定使用。</summary>
    public LocalizationService Loc => LocalizationService.Instance;

    /// <summary>可用语言集合（自动扫描 lang 目录，下拉菜单显示各语言原生名称）。</summary>
    public ObservableCollection<LanguageOption> Languages { get; } = new();

    /// <summary>
    /// 扫描 lang 目录下的 *.json，为每个语言文件创建 LanguageOption。
    /// 显示名取自文件内的 "_LanguageName" 键（缺失时回退为语言代码），按代码字母序排列。
    /// </summary>
    private void DiscoverLanguages()
    {
        Languages.Clear();
        try
        {
            if (!Directory.Exists(LangDir)) return;
            foreach (var file in Directory.EnumerateFiles(LangDir, "*.json").OrderBy(f => Path.GetFileName(f)))
            {
                string code = Path.GetFileNameWithoutExtension(file);
                string displayName = code; // 默认用代码，读取失败时兜底
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (doc.RootElement.TryGetProperty("_LanguageName", out var name) && name.ValueKind == JsonValueKind.String)
                        displayName = name.GetString() ?? code;
                }
                catch { /* 读取失败则用语言代码作为显示名 */ }
                Languages.Add(new LanguageOption(code, displayName));
            }
        }
        catch { /* 目录不可读时保持空列表 */ }
    }

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    public MainViewModel()
    {
        // 先扫描 lang 目录得到可用语言列表，再加载设置
        DiscoverLanguages();

        _settingsLoaded = false;
        LoadSettings();

        // 语言已从设置读取（或默认简体中文），加载对应语言文件
        Loc.Load(LangDir, SelectedLanguage!.Code);

        // 加载语言后再设置初始提示文本
        StatusText = Loc["Home_Ready"];

        // 设置已完成加载，之后属性变化会触发保存
        _settingsLoaded = true;

        // 监听安装目录，目录被删除/清空时自动刷新状态按钮
        StartWatchingInstallPath();

        // 初始化时异步加载版本
        _ = LoadVersionsAsync();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null) return;
        Loc.Load(LangDir, value.Code);
        if (_settingsLoaded) SaveSettings();
        RefreshLocalizedUi();
    }

    /// <summary>从 config/settings.json 读取上次的版本 / 语言 / 安装路径。</summary>
    private void LoadSettings()
    {
        bool hasSettings = false;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (s != null)
                {
                    hasSettings = true;
                    if (!string.IsNullOrWhiteSpace(s.InstallPath)) _installPath = s.InstallPath;
                    // 仅当设置中指定的语言存在时采用，否则下面的默认回退会接管
                    _selectedLanguage = Languages.FirstOrDefault(l => l.Code == s.Language);
                    _savedVersionName = s.VersionName;
                }
            }
        }
        catch { /* 设置损坏时使用默认值 */ }

        // 首次运行（无 settings.json）默认英文；已有设置则按设置语言，缺失/无效时回退英文
        if (!hasSettings || _selectedLanguage == null)
            _selectedLanguage ??= Languages.FirstOrDefault(l => l.Code == "en-US")
                                  ?? Languages.FirstOrDefault();
    }

    /// <summary>将当前版本 / 语言 / 安装路径写入 config/settings.json。</summary>
    private void SaveSettings()
    {
        if (!_settingsLoaded) return;
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var s = new AppSettings
            {
                VersionName = SelectedVersion?.VersionName ?? _savedVersionName,
                Language = SelectedLanguage?.Code ?? "zh-CN",
                InstallPath = InstallPath,
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 写失败不影响使用 */ }
    }

    /// <summary>语言切换后刷新受影响的动态文本。</summary>
    private void RefreshLocalizedUi()
    {
        // 未在下载时刷新初始提示文本
        if (!IsBusy) StatusText = Loc["Home_Ready"];

        // 刷新版本加载信息（若已加载过）
        if (_versionsLoadedCount > 0)
            VersionLoadInfo = string.Format(Loc["Versions_Loaded"], _versionsLoadedCount);

        // 刷新首页主按钮文案
        NotifyGameState();
    }

    // --- 导航命令 ---

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    [RelayCommand]
    private void ShowHome() => CurrentPage = PageKind.Home;

    [RelayCommand]
    private void ShowVersions() => CurrentPage = PageKind.Versions;

    [RelayCommand]
    private void ShowSettings() => CurrentPage = PageKind.Settings;

    [RelayCommand]
    private void ShowAbout() => CurrentPage = PageKind.About;

    // --- 首页主操作：启动游戏 / 下载游戏 ---

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        switch (_gameStatus)
        {
            case GameStatus.Installed:
                Launch();
                return;
            case GameStatus.NeedsRepair:
                await StartDownloadAsync(string.Format(Loc["Home_Repair_Status"], SelectedVersion?.VersionName ?? ""));
                return;
            case GameStatus.Interrupted:
                // 恢复未完成的下载：断点续传 .part，并跳过已下载完整的文件
                await StartDownloadAsync(string.Format(Loc["Home_Resume_Status"], SelectedVersion?.VersionName ?? ""));
                return;
            default:
                await StartDownloadAsync($"正在解析 {SelectedVersion?.VersionName ?? ""}...");
                break;
        }
    }

    // --- 设置页：修复游戏文件 / 修复运行库 ---

    [RelayCommand]
    private async Task FixGameFilesAsync()
    {
        // 仅当游戏处于“需修复”状态才允许执行修复逻辑（与主页“修复游戏”按钮一致的检测逻辑）
        if (_gameStatus != GameStatus.NeedsRepair)
        {
            string msg = _gameStatus switch
            {
                GameStatus.Installed => Loc["Repair_NotNeeded"],
                GameStatus.Interrupted => Loc["Repair_Interrupted"],
                _ => Loc["Repair_NotInstalled"],
            };
            await ShowInfoDialogAsync(Loc["Dialog_Repair_Title"], msg);
            return;
        }

        // 修复流程不重复触发运行库安装，完成后弹窗确认
        if (await StartDownloadAsync(string.Format(Loc["Home_Repair_Status"], SelectedVersion?.VersionName ?? ""), runRuntimeCheck: false))
            await ShowInfoDialogAsync(Loc["Dialog_Repair_Title"], Loc["Repair_Complete"]);
    }

    [RelayCommand]
    public async Task FixRuntimeAsync()
    {
        IsBusy = true;
        bool success = await _runtimeService.InstallVcRedistAsync(s => StatusText = s);
        IsBusy = false;
        if (success)
            await ShowInfoDialogAsync(Loc["Dialog_Runtime_Title"], Loc["Runtime_InstallSuccess"]);
        else
            await ShowInfoDialogAsync(Loc["Dialog_Runtime_Title"], Loc["Runtime_InstallFailed"]);
    }

    /// <summary>检查并安装运行库，用弹窗向用户反馈结果。</summary>
    private async Task EnsureRuntimeInstalledAsync()
    {
        if (_runtimeService.IsVcRedistInstalled())
        {
            await ShowInfoDialogAsync(Loc["Dialog_Runtime_Title"], Loc["Runtime_AlreadyInstalled"]);
            return;
        }

        StatusText = Loc["Runtime_Installing"];
        bool success = await _runtimeService.InstallVcRedistAsync(s => StatusText = s);
        if (success)
            await ShowInfoDialogAsync(Loc["Dialog_Runtime_Title"], Loc["Runtime_InstallSuccess"]);
        else
            await ShowInfoDialogAsync(Loc["Dialog_Runtime_Title"], Loc["Runtime_InstallFailed"]);
    }

    /// <summary>显示一个信息提示弹窗（FluentAvalonia ContentDialog）。</summary>
    private async Task ShowInfoDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = Loc["Dialog_OK"],
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    // --- 下载控制 ---

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        IsPaused = true;
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        if (_downloadPlan == null) return;
        await ExecuteDownloadPlanAsync();
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _cleanupOnCancel = true;
        IsPaused = false;

        // 暂停后 finally 已执行并置空 _cts，下载任务已结束，需直接清理并复位
        if (_cts == null)
        {
            CleanupDownloadArtifacts();
            IsBusy = false;
            SpeedText = "0.00 MB/s";
            ProgressPercentage = 0;
            _downloadPlan = null;
            StatusText = "已取消下载，不完整文件已清理。";
            NotifyGameState();
            _ = CheckGameStatusAsync();
            return;
        }

        _cts.Cancel();
    }

    [RelayCommand]
    private void Launch()
    {
        string? exe = FindGameExe();
        if (exe != null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            });
        }
        else
        {
            StatusText = "未找到游戏程序！";
        }
    }

    [RelayCommand]
    private async Task SelectFolderAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var folders = await Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow)!.StorageProvider.OpenFolderPickerAsync(new() { Title = "选择安装目录" });
            if (folders.Any()) InstallPath = folders[0].Path.LocalPath;
        }
    }

    // --- 下载计划构建与执行 ---

    private async Task<bool> StartDownloadAsync(string initialStatus, bool runRuntimeCheck = true)
    {
        if (SelectedVersion == null)
        {
            StatusText = "请先在【版本列表】中选择一个版本。";
            return false;
        }

        StatusText = initialStatus;

        try
        {
            // 1. 加载 Manifest，构建下载计划
            string manifestPath = Path.Combine(ConfigDir, "manifest", $"{SelectedVersion.VersionName}.json");
            if (!File.Exists(manifestPath)) { StatusText = "错误: 找不到版本清单"; return false; }

            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<McdManifest>(json);
            var filesToDownload = manifest?.Files?.Where(x => x.Value.Type != "directory")
                .Select(x =>
                {
                    var dl = x.Value.Downloads?.Raw ?? x.Value.Downloads?.Lzma;
                    return dl == null ? null : new DownloadPlanItem(x.Key.Replace('/', Path.DirectorySeparatorChar), dl.Url!, dl.Sha1, dl.Size);
                })
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();

            if (filesToDownload == null || filesToDownload.Count == 0) { StatusText = "清单无效"; return false; }

            _downloadPlan = filesToDownload;
            _totalBytes = filesToDownload.Sum(f => f.Size);
            return await ExecuteDownloadPlanAsync(runRuntimeCheck);
        }
        catch (Exception ex) { StatusText = $"错误: {ex.Message}"; return false; }
    }

    private async Task<bool> ExecuteDownloadPlanAsync(bool runRuntimeCheck = true)
    {
        if (_downloadPlan == null) return false;

        bool success = false;
        IsBusy = true;
        IsPaused = false;
        _cleanupOnCancel = false;
        _startTime = DateTime.Now;
        _cts = new CancellationTokenSource();

        try
        {
            // 预扫描已存在的完整文件，避免进度重复/回退
            var (preBytes, preCount) = await PreCountExistingAsync(_downloadPlan, _cts.Token);
            _preCountedBytes = preBytes;
            _preCountedCount = preCount;

            await RunDownloadAsync(_cts.Token);
            success = true;

            // 运行库检查（弹窗提示）：仅全新下载时需要，修复流程不重复安装运行库
            if (runRuntimeCheck)
            {
                await EnsureRuntimeInstalledAsync();
                StatusText = Loc["Runtime_Ready"];
            }
        }
        catch (OperationCanceledException)
        {
            // 状态在 finally 中根据暂停/取消决定
        }
        catch (Exception ex)
        {
            StatusText = $"错误: {ex.Message}";
        }
        finally
        {
            if (IsPaused)
            {
                StatusText = "已暂停，可点击【恢复下载】继续。";
            }
            else
            {
                if (_cleanupOnCancel)
                {
                    CleanupDownloadArtifacts();
                    StatusText = "已取消下载，不完整文件已清理。";
                    ProgressPercentage = 0;
                }
                IsBusy = false;
                SpeedText = "0.00 MB/s";
                _downloadPlan = null;
                NotifyGameState();
                // 清理后重新检测，让按钮从“修复”正确回到“下载”
                _ = CheckGameStatusAsync();
            }

            _cts?.Dispose();
            _cts = null;
        }

        return success;
    }

    private async Task RunDownloadAsync(CancellationToken token)
    {
        var plan = _downloadPlan!;
        long totalBytes = _totalBytes;
        long downloadedBytes = _preCountedBytes;
        int completedCount = _preCountedCount;
        var startTime = _startTime;

        var semaphore = new SemaphoreSlim(8);
        var tasks = plan.Select(async file =>
        {
            await semaphore.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;
                RegisterPartFile(file.RelPath);
                var progress = new Progress<long>(bytes =>
                {
                    var total = Interlocked.Add(ref downloadedBytes, bytes);
                    UpdateUIProgress(total, totalBytes, completedCount, plan.Count, startTime);
                });
                bool completed = await _downloadService.DownloadFileResumableAsync(
                    file.Url, Path.Combine(InstallPath, file.RelPath), file.Sha1, progress, token);
                if (completed) Interlocked.Increment(ref completedCount);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
    }

    private async Task<(long Bytes, int Count)> PreCountExistingAsync(List<DownloadPlanItem> plan, CancellationToken token)
    {
        long bytes = 0;
        int count = 0;
        foreach (var item in plan)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.Combine(InstallPath, item.RelPath);
            if (await _downloadService.VerifyFileAsync(path, item.Sha1))
            {
                bytes += new FileInfo(path).Length;
                count++;
            }
        }
        return (bytes, count);
    }

    private void RegisterPartFile(string relPath)
    {
        var part = Path.GetFullPath(Path.Combine(InstallPath, relPath)) + ".part";
        lock (_partLock) _partFiles.Add(part);
    }

    private void CleanupPartFiles()
    {
        lock (_partLock)
        {
            foreach (var f in _partFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
            _partFiles.Clear();
        }
    }

    /// <summary>
    /// 取消下载后的彻底清理：删除 .part 临时文件，并清空安装目录下所有残留
    /// （含子文件夹）。避免残留空文件夹导致安装目录被误判为“需修复”。
    /// </summary>
    private void CleanupDownloadArtifacts()
    {
        CleanupPartFiles();
        try
        {
            if (Directory.Exists(InstallPath))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(InstallPath))
                {
                    if (Directory.Exists(entry)) Directory.Delete(entry, true);
                    else File.Delete(entry);
                }
            }
        }
        catch { /* 个别文件被占用时忽略，其余照常清理 */ }
    }

    // --- 辅助方法与命令可用性 ---

    private string? FindGameExe()
    {
        string exe = Path.Combine(InstallPath, "Dungeons.exe");
        if (File.Exists(exe)) return exe;
        exe = Path.Combine(InstallPath, "Dungeons", "Binaries", "Win64", "Dungeons-Win64-Shipping.exe");
        return File.Exists(exe) ? exe : null;
    }

    private void NotifyGameState()
    {
        OnPropertyChanged(nameof(IsGameInstalled));
        OnPropertyChanged(nameof(NeedsRepair));
        OnPropertyChanged(nameof(PrimaryActionText));
    }

    private bool CanPause() => IsBusy && !IsPaused;
    private bool CanResume() => IsBusy && IsPaused;

    partial void OnSelectedVersionChanged(McdVersion? value)
    {
        _savedVersionName = value?.VersionName ?? string.Empty;
        if (_settingsLoaded) SaveSettings();
        NotifyGameState();
        _ = CheckGameStatusAsync();
    }

    partial void OnInstallPathChanged(string value)
    {
        if (_settingsLoaded) SaveSettings();
        StartWatchingInstallPath();
        _ = CheckGameStatusAsync();
    }

    /// <summary>开始监听安装目录，目录被删除/清空时自动刷新状态按钮。</summary>
    private void StartWatchingInstallPath()
    {
        StopWatchingInstallPath();
        try
        {
            if (!Directory.Exists(InstallPath)) return;
            _watcher = new FileSystemWatcher(InstallPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            _watcher.Created += OnInstallDirectoryChanged;
            _watcher.Deleted += OnInstallDirectoryChanged;
            _watcher.Renamed += OnInstallDirectoryChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch { /* 路径不可监听时忽略 */ }
    }

    private void StopWatchingInstallPath()
    {
        try { _watcher?.Dispose(); } catch { }
        _watcher = null;
    }

    private void OnInstallDirectoryChanged(object? sender, FileSystemEventArgs e)
    {
        // 简单防抖：避免一次删除触发多次检测
        lock (_statusLock)
        {
            if ((DateTime.Now - _lastStatusCheckTime).TotalMilliseconds < 500) return;
            _lastStatusCheckTime = DateTime.Now;
        }
        _ = CheckGameStatusAsync();
    }

    // --- 已安装版本检测（基于 Dungeon.exe / Shipping.exe 的 SHA-1） ---

    /// <summary>
    /// 检测安装目录的完整状态：
    /// 目录不存在或为空 → 未安装；exe 与清单 SHA-1 匹配 → 已安装；否则 → 需修复。
    /// 仅校验 exe 以保证启动速度；完整校验在修复/下载流程中逐步进行。
    /// </summary>
    private async Task CheckGameStatusAsync()
    {
        int seq = ++_statusCheckSeq;
        GameStatus status = GameStatus.NotInstalled;

        if (SelectedVersion != null)
        {
            string dir = InstallPath;
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            {
                // 存在未完成的下载（.part 残留，通常是下载中断/意外关闭程序）→ 优先“恢复下载”，避免误判为需修复
                if (HasPendingDownload(dir))
                {
                    status = GameStatus.Interrupted;
                }
                else
                {
                    status = GameStatus.NeedsRepair;
                    string manifestPath = Path.Combine(ConfigDir, "manifest", $"{SelectedVersion.VersionName}.json");
                    if (File.Exists(manifestPath))
                    {
                        try
                        {
                            var manifest = JsonSerializer.Deserialize<McdManifest>(await File.ReadAllTextAsync(manifestPath));
                            string? sha1 = GetExeSha1(manifest);
                            if (!string.IsNullOrEmpty(sha1) && await IsExeValidAsync(sha1))
                                status = GameStatus.Installed;
                        }
                        catch { /* 清单读取失败则视为需修复 */ }
                    }
                }
            }
        }

        if (seq != _statusCheckSeq) return; // 已有更新的检测结果，丢弃本次
        _gameStatus = status;
        NotifyGameState();
    }

    /// <summary>从清单中取 Dungeon.exe 或 Shipping.exe 的 raw SHA-1（与下载所用来源一致）。</summary>
    private static string? GetExeSha1(McdManifest? manifest)
    {
        if (manifest?.Files == null) return null;
        foreach (var key in new[] { "Dungeons.exe", "Dungeons/Binaries/Win64/Dungeons-Win64-Shipping.exe" })
        {
            if (manifest.Files.TryGetValue(key, out var f) && f.Downloads != null)
            {
                var dl = f.Downloads.Raw ?? f.Downloads.Lzma;
                if (!string.IsNullOrEmpty(dl?.Sha1)) return dl.Sha1;
            }
        }
        return null;
    }

    /// <summary>本地任一 exe 的 SHA-1 与清单匹配即视为已安装。</summary>
    private async Task<bool> IsExeValidAsync(string expectedSha1)
    {
        foreach (var rel in new[] { "Dungeons.exe", "Dungeons/Binaries/Win64/Dungeons-Win64-Shipping.exe" })
        {
            var path = Path.Combine(InstallPath, rel.Replace('/', Path.DirectorySeparatorChar));
            if (await _downloadService.VerifyFileAsync(path, expectedSha1)) return true;
        }
        return false;
    }

    /// <summary>安装目录下是否存在未完成的下载（.part 残留），用于判定“恢复下载”。</summary>
    private bool HasPendingDownload(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.part", SearchOption.AllDirectories).Any();
        }
        catch { return false; }
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            string path = Path.Combine(ConfigDir, "mcd_versions.json");
            if (!File.Exists(path)) { VersionLoadInfo = "配置文件丢失"; return; }

            var json = await File.ReadAllTextAsync(path);
            var list = JsonSerializer.Deserialize<ObservableCollection<McdVersion>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Dispatcher.UIThread.Post(() => {
                Versions = list ?? new();
                // 默认选中 1.17.0.0，不存在则回退到第一个
                SelectedVersion = Versions.FirstOrDefault(v => v.VersionName == _savedVersionName) ?? Versions.FirstOrDefault();
                _versionsLoadedCount = Versions.Count;
                VersionLoadInfo = string.Format(Loc["Versions_Loaded"], Versions.Count);
            });
        }
        catch (Exception ex) { VersionLoadInfo = $"加载失败: {ex.Message}"; }
    }

    private void UpdateUIProgress(long current, long total, int count, int totalCount, DateTime start)
    {
        var elapsed = (DateTime.Now - start).TotalSeconds;
        if (elapsed < 0.1) return; // 频率控制

        Dispatcher.UIThread.Post(() => {
            ProgressPercentage = total > 0 ? (double)current / total * 100 : 0;
            ProgressDetailText = $"{count}/{totalCount} 项 ({current / 1048576}MB / {total / 1048576}MB)";
            SpeedText = $"{(current / 1048576.0 / elapsed):F2} MB/s";
        });
    }
}