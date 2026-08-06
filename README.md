# MCDArchive - Minecraft Dungeons 历史版本启动器

> [!IMPORTANT]
> **📢 重要提示：**
> 1. 本启动器只提供正版 Minecraft Dungeons 资源，不负责任何形式的盗版 Patch。因此，在启动游戏时遇到需要微软账号登录验证属于**完全正常**的现象。
> 2. 由于程序采用 **Native AOT** 原生编译以追求极致性能与纯净度，部分杀毒软件可能会出现**误报（误杀）**。本项目完全开源，请放心运行，或将其加入白名单。

`MCDArchive` 是一款基于 .NET 8 与 **Avalonia UI** 开发的开源启动器，专为下载、管理及启动《我的世界：地下城》（Minecraft Dungeons）的历史版本而设计。

本项目采用 **Native AOT 引导技术**，实现了主程序与依赖库的彻底分离，为用户提供一个极致纯净的根目录运行环境，同时具备完善的断点续传与校验机制。

---

## 🌟 核心功能特性

- **极致纯净的目录**：通过原生 AOT 引导程序，根目录仅保留一个 `.exe`，所有复杂的依赖项（约 100+ DLLs）均隔离在 `app/` 文件夹中。
- **鲁棒的下载系统**：
  - **断点续传**：支持暂停与恢复。程序会生成 `.part` 临时文件，在网络中断后可无缝继续。
  - **并发下载**：采用信号量控制多线程同步下载游戏碎片，极大提升下载速度。
  - **严格校验**：基于 **SHA-1** 算法对每个文件进行校验，自动修复损坏或缺失的二进制文件。
- **多版本存档切换**：通过解析网络 `manifest` 配置文件，自由选择并安装从 Beta 到现行的历史版本。
- **全自动环境修复**：内置 **VC++ 2015-2022** 运行库检测与一键静默安装功能。
- **现代化的 UI 交互**：基于 **FluentAvalonia** 风格设计，支持侧边栏导航、全动态进度显示及实时速度监测。
- **完善的国际化 (i18n)**：内置多语言支持，支持通过外部 JSON 轻松扩展翻译。

---

## 📂 运行目录结构 (Distribution Layout)

发布版本遵循以下布局。**用户应始终运行根目录下的 `MCDArchive.exe`**：

```text
MCDArchive/
├── MCDArchive.exe        # 引导程序 (入口)，采用 Native AOT 编译，秒开且无依赖
├── config/               # 配置目录
│   ├── mcd_versions.json # 版本定义列表
│   └── manifest/         # 存放各版本的 .json 清单文件 (备用)
├── lang/                 # 多语言翻译目录 (zh-CN.json, en-US.json)
└── app/                  # 核心程序文件夹 (包含所有运行时依赖)
    └── MCDArchive.exe    # 实际的核心逻辑程序 (被引导器调用)
```

---

## 🛠️ 开发与编译 (Development)

本项目由两个子项目组成：
1. `src/MCDArchive`: **主程序** (Avalonia UI 逻辑)。
2. `src/MCDArchive.Launcher`: **引导程序** (Native AOT 壳)。

### 1. 本地开发调试
如果您希望调试 UI 或核心逻辑，请直接运行主程序项目：
```bash
# 克隆仓库
git clone https://github.com/CreatorCSIE/MCDArchive.git
cd MCDArchive

# 调试核心 UI 逻辑
dotnet run --project src/MCDArchive/MCDArchive.csproj
```
*提示：在开发模式下，程序会自动定位到项目根目录下的 `lang` 和 `config` 文件夹。*

### 2. 手动构建发布包
若要手动构建出和 Release 页面一致的整洁目录：
```bash
# 1. 编译核心到 app 目录 (Self-Contained)
dotnet publish src/MCDArchive/MCDArchive.csproj -c Release -r win-x64 --self-contained -o ./dist/app

# 2. 编译引导程序到根目录 (需要安装 C++ 构建工具以支持 AOT)
dotnet publish src/MCDArchive.Launcher/Launcher.csproj -c Release -r win-x64 -o ./dist /p:PublishAot=true
```

---

## 🌐 国际化与翻译 (Localization)

本项目的翻译文件位于 `lang/` 目录下。我们采用键值对 JSON 格式，欢迎提交 **Pull Request** 为我们提供更多语言支持。

*Localization files are located in the `lang/` directory. Contributions for new languages are always welcome!*

---

## 💻 运行环境要求

- **操作系统**：Windows 10 / 11 (x64)。
- **.NET 运行时**：发布包为 `Self-Contained` 模式，用户**无需**额外安装 .NET 运行时。
- **VC++ 运行库**：游戏运行必须具备 **Visual C++ Redistributable 2015-2022**（启动器内提供自动安装选项）。

---

## 📄 许可证与版权声明 (License & Copyright)

- **开源协议**：本项目采用 **[GNU General Public License v3 (GPL v3)](LICENSE)** 协议开源。
- **版权所有**：Copyright (c) 2026 **CreatorCSIE**. All rights reserved.
- **法律免责声明**：
  - 本项目为开源第三方工具，与 **Mojang Studios**、**Double Eleven** 或 **Microsoft** 无任何关联。
  - 本仓库**不包含、不分发**任何官方游戏受版权保护的二进制资源。所有游戏文件均根据用户选择，通过官方清单地址在用户本地下载。
  - Minecraft Dungeons 的商标及所有相关资产版权归其各自所有者所有。

---

## ❓ 常见问题

- **为什么有两个 MCDArchive.exe？** 根目录的是“引导器”，负责保持环境整洁；`app/` 目录下的是“核心程序”。用户应始终点击根目录的文件。
- **下载卡在 0%？** 请检查网络连接，或确认 `config/manifest` 中的下载链接在您所在地区是否可达。
- **如何添加新版本？** 在 `config/mcd_versions.json` 中添加条目，并确保 `config/manifest/` 下有对应的清单文件。