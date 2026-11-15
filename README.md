<p align="center">
  <img src="assets/icon-256.png" width="112" alt="MonoLauncher" />
</p>

# MonoLauncher

> 面向 Windows 11 的 Minecraft: Java Edition 启动器。纯黑白界面、原生 Avalonia 渲染，内置应用内自动更新、多提供商 AI Agent 与本地模型推理，联机部分集成 [Terracotta](https://github.com/burningtnt/Terracotta)。

没有 WebView，没有 HTML / CSS，界面全部由 Avalonia 控件与 Skia 自绘。

**当前版本：v1.0.3** · Windows 10/11 x64 · 免安装

---

## 下载

| 来源 | 地址 |
|---|---|
| **GitHub Releases（主）** | [github.com/cyl-0121/Mono-Launcher/releases](https://github.com/cyl-0121/Mono-Launcher/releases) |
| 国内镜像 | [ada1f3c4ba9a13ac3.app.workbuddy.host](https://ada1f3c4ba9a13ac3.app.workbuddy.host/?sharecode=2TEW6KGZWT) |

下载 `MonoLauncher-win-x64.zip`（约 84 MB），解压后双击 `MonoLauncher.exe` 即可，**不需要安装 .NET**。

数据目录：`%APPDATA%\MonoLauncher`（存档、实例、配置都在这里，删程序不会丢）。

> 镜像站由社区托管，访问时可能需要链接里的分享码；主下载还是 GitHub。

---

## 特性

### 版本与加载器

- 读取 Mojang 官方版本清单，按 **正式版 / 测试版 / 愚人节版 / 远古版 / 全部** 分类浏览（带数量统计）
- 两步式安装：先选版本 → 再选加载器（原版 / Fabric / Quilt / NeoForge / Forge）→ 下载
- 下载引擎：并发、断点续传（`.part` + sidecar）、指数退避重试、SHA-1/SHA-256 校验、原子改名
- 资源文件分批下载，进度、速度、当前文件实时显示

### 应用内自动更新

- 启动时自动检查最新版本：**镜像站的 `releases.json` 优先**，失败回退 GitHub 的 Atom feed（不受 API 限额影响）
- 首页显示更新公告：版本号、日期、完整更新说明
- 发现新版本 → 点「更新到 vX」→ **下载 → 替换自身 → 自动重启**，不用手动下载解压
- 替换前把旧程序备份为 `MonoLauncher.exe.bak`，失败自动回滚并提示
- 下载地址优先采用镜像站给出的链接，失败回退 GitHub

### 强制更新

当出现必须修复的问题时，发布者可以在 `releases.json` 里加一个字段：

```json
"latest": { "mandatory": true, ... }
```

旧版本启动后会看到覆盖内容区的提示遮罩：

- 导航栏禁用，点不动
- 点「启动游戏」会被拦截并跳回提示
- 只保留「更新到 vX」和关闭窗口两条出路

更新完成重启后自动解除。平时这个字段保持 `false` 或不写即可。

### 实例与 Mod

- 实例隔离：每个实例独立的 `minecraft` 目录（存档 / Mod / 配置互不影响）
- 导入现有 `.minecraft`、PCL、HMCL、Prism 实例
- Mod 识别 Fabric / Quilt / NeoForge / Forge 元数据，支持启停、删除（移到回收站）
- 从 Modrinth 搜索并安装 Mod
- **安装 Mod 自动补齐前置**：按 Modrinth 的 `required` 依赖递归下载前置模组，去重、校验 SHA-1
- **一键部署环境**：自动下载缺失的 Java 运行时（Temurin）、装版本与 Loader、建实例、应用内存优化，可顺带装 Fabric API

### 内存优化

- PCL 风格的内存建议：读取本机真实物理内存，按总量给出 `-Xms/-Xmx`，并始终为系统留出余量
- 一键开启 Aikar/G1 调优参数（`-XX:+UseG1GC`、`G1NewSizePercent`、`StringDeduplication` 等）
- 可作用到单个实例，也可作为全局默认；重复应用不会产生重复参数
- AI 工具 `optimize_memory` / `set_memory` 可让助手代劳

### 联机

- **陶瓦联机（推荐）**：通过 Terracotta 的房间码跨网络联机，无需公网 IP
  - 首次使用自动下载官方原版二进制（不随本仓库分发、未修改）
  - 创建 / 加入房间、成员列表、状态轮询、一键启动并直连
- **局域网扫描**：监听 `224.0.2.60:4445` 的原版 LAN 广播
- **手动直连**：填 `地址:端口` 直接连接

### 账户

- 微软正版登录：OAuth 设备码 → Xbox Live → XSTS → Minecraft Services
- 登录窗口显示代码和有效期倒计时，可随时换码
- 凭据经 Windows DPAPI 加密保存在本机，日志自动脱敏

### AI Agent

- Semantic Kernel 多提供商：OpenAI、Azure OpenAI、Anthropic、Gemini、DeepSeek、OpenRouter、智谱、通义、Kimi、硅基流动、Ollama、LM Studio 及任意 OpenAI 兼容端点
- 流式输出、取消、工具调用中间状态
- 高风险工具（安装 / 启动 / 部署 / 删除）强制原生确认框
- LLamaSharp 本地 GGUF 推理（GPU 层数 / 上下文 / 线程可调）
- **本地 AI 一键部署**：选「本地 AI（本机一键部署）」→ 自动检测 CPU / 内存 / 显卡并给出结论 → 一键下载 Qwen2.5 GGUF（1.5B / 3B / 7B，按内存推荐）→ 自动写入推理参数并加载。同一安装包内置 **CPU + Vulkan 双后端**，检测到可用显卡自动加速（AMD / Intel / NVIDIA），否则回退 CPU，不联网、不产生费用
- 内置工具：`deploy_environment`（一键部署环境）、`install_mod_full`（装 Mod 连前置）、`get_project_detail` + `translate_text`（**Mod 简介翻译**）、`recommend_mods`（**按口味推荐 Mod**）、`diagnose_crash`（**崩溃原因排查**：汇总崩溃报告、日志与 Mod 列表后给出结论）、`optimize_memory` / `set_memory`（内存优化），以及实例/版本/日志查询

### 界面

- 无边框窗口 + 自绘标题栏（拖动、双击最大化、Win11 圆角、原生最小化动画）
- 顶部导航，白色选中胶囊平滑滑动，页面切换淡入上移
- 纯黑主题：`#000` 背景、`#0A0A0A` 卡片、白底黑字主按钮
- 视觉档位四档（完整 / 标准 / 简洁 / 关闭），低帧率自动降档

---

## 从源码构建

需要 .NET SDK 10.0.100 或更高。

```bash
git clone https://github.com/cyl-0121/Mono-Launcher.git
cd Mono-Launcher
dotnet build MonoLauncher.slnx -c Release
dotnet run --project src/MonoLauncher.App
```

发布单文件：

```bash
dotnet publish src/MonoLauncher.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish/win-x64
```

打包发布包（约定：产物统一压成 zip，只放单个 exe，不带 pdb / runtimes 残留）：

```powershell
Compress-Archive -Path publish/win-x64/MonoLauncher.exe `
  -DestinationPath publish/MonoLauncher-win-x64.zip -CompressionLevel Optimal
```

`publish/MonoLauncher-win-x64.zip` 约 84 MB，解压后只有一个 `MonoLauncher.exe`，双击即可运行，**不需要安装 .NET**。

---

## 项目结构

```
MonoLauncher/
├─ MonoLauncher.slnx
├─ src/
│  ├─ MonoLauncher.Core/     核心：版本、下载、启动、账户、Loader、Mod、联机、更新
│  ├─ MonoLauncher.Agent/    AI：Semantic Kernel 封装、工具、本地 GGUF
│  ├─ MonoLauncher.App/      Avalonia 界面（MVVM + 自绘控件 + 更新器）
│  └─ MonoLauncher.Cli/      命令行：list / install / launch / java / nettest / terracotta …
├─ tests/MonoLauncher.Tests/ xUnit 单元测试
├─ docs/                     使用指南、界面说明、性能档位、测试记录、许可调研
├─ website/                  国内镜像站（纯静态，见 website/README.md）
├─ scripts/                  构建、运行、站点发布脚本
├─ assets/                   应用图标（SVG / PNG）
└─ .github/workflows/        每日可用性检查
```

### 镜像站

`website/` 是一个纯静态下载页，丢到任意静态托管即可，不需要后端：

```
website/
├─ index.html      下载页（自动跟随 GitHub 最新 Release）
├─ admin.html      发布页（填版本号/说明/选 zip → 生成 releases.json）
├─ releases.json   版本数据（页面兜底用）
├─ icon.svg
└─ downloads/      放 MonoLauncher-win-x64.zip
```

- 支持 `?v=0.9.0` 参数自动判断是否最新
- 页面会去 GitHub 查最新 Release，所以发版后镜像站会自动显示新版本号与说明
- 如果镜像站里还没有新 zip，下载按钮会自动回退到 GitHub 直链
- 启动器「设置 → 下载站地址」填上站点地址后，自动更新会优先从镜像站下载

---

## 技术栈

| 层 | 选型 |
|---|---|
| 运行时 | .NET 10（自包含单文件发布） |
| UI | Avalonia 12.1.6 + SkiaSharp |
| 模式 | CommunityToolkit.Mvvm（`[ObservableProperty]` / `[RelayCommand]`） |
| AI | Microsoft.SemanticKernel 1.82 |
| 本地推理 | LLamaSharp 0.30（CPU 后端） |
| 测试 | xUnit |
| 字体 | Segoe UI（系统字体，随系统提供） |

---

## 验证状态

**已在真实环境验证：**

- 版本清单拉取、1.21.1 原版下载与启动（NeoForge 21.1.1 实例可进入游戏）
- NeoForge 安装（含 BMCLAPI 镜像回退、版本元数据补全）
- **Fabric 全链路**：1.21.1 + Fabric Loader 0.20.1 的 profile 解析、库下载、实例创建（实测曾暴露并修复 “`+0000` 时区解析” 与 “Loader 库被错拼到 Mojang 仓库” 两个问题）
- **一键部署环境**：1.21.1 + Fabric + 自动前置（Fabric API）实测跑通，产出 889 MB 完整可玩目录
- **内存优化**：读取真实物理内存（15.3 GB → 1536–6144 MB）并落盘到设置与实例
- 下载引擎断点续传、校验、重试
- 实例导入（PCL / HMCL / Prism / .minecraft 格式）
- Modrinth 搜索与安装、Mod 启停
- 局域网扫描与直连启动
- Terracotta 二进制下载、启动、`/state` 轮询、优雅退出
- 微软设备码申请（拿到 `user_code` 与 `verification_uri`）
- **应用内自动更新全流程**：下载 68 MB → 解压 → 备份 73 MB → 替换 → 新程序正常启动
- 38 项单元测试（含启动参数合并、工具注册、宽松时间戳解析、Loader 仓库地址）

**未验证（缺少对应条件，按公开协议实现）：**

- 微软正版登录的 XBL / XSTS / Minecraft Services 换取环节（没有正版账号）
- Quilt / Forge 的实际安装（Fabric 与 NeoForge 已验证）
- 陶瓦联机的创建 / 加入房间全流程（需要两名玩家和游戏内局域网开放）
- 真实 LLM 调用与本地 GGUF 推理（没有 API Key / 模型文件）
- 整合包（`.mrpack`）与 CurseForge 支持（未实现）

---

## 已知限制

1. 仅支持 Windows 10/11 x64，不提供 macOS / Linux 构建。
2. 微软登录端点需要网络可达，部分网络环境要在设置里填代理。
3. 自动更新要求程序所在目录可写；放在 `Program Files` 下会提示没有写入权限。
4. 陶瓦联机的公共节点由 Terracotta 项目维护，本项目不担保其可用性。
5. 单文件发布体积约 90 MB / zip 约 84 MB（含 CPU 与 Vulkan 双推理后端）。
6. 镜像站依赖第三方静态托管，可能要求分享码或延迟同步。

---

## 许可证

本项目原创代码以 **MIT** 发布，见 [LICENSE](LICENSE)。

第三方依赖与参考实现的许可见 [docs/reference-licenses.md](docs/reference-licenses.md)：

- **Avalonia / SkiaSharp / Semantic Kernel / LLamaSharp**：MIT
- **Terracotta**：AGPL-3.0-or-later。本仓库**不包含**其二进制，运行时通过 `terracotta --hmcl` 启动官方原版，只调用其本地 HTTP API；界面中标注了版权信息，符合其 AGPL 例外条款
- **Minecraft**：Mojang Studios 商标。本软件不包含任何游戏本体、资源或账号

> 早期版本使用 Apple SF Pro 字体，因其许可仅限 Apple 平台，现已改为系统字体 Segoe UI。想换字体可改 `MainWindow.axaml` 与 `HomeView.axaml` 里的 `FontFamily`。

---

## 免责声明

- 本软件与 Mojang Studios、Microsoft 无隶属或授权关系。
- 本软件不提供、不分发任何 Minecraft 游戏文件；所有下载均来自 Mojang 官方服务地址，请使用合法拥有的账号。
- 使用本软件造成的存档损坏、数据丢失、账号封禁或服务器纠纷，作者与贡献者不承担责任。请自行备份重要存档。
- 禁止将本软件用于传播盗版、绕过正版验证、搭建未经授权的付费私服或其他违法违规行为。

首次启动会显示《用户协议与免责声明》，不同意会直接退出。

---

## 致谢

- [Avalonia](https://avaloniaui.net/)、[SkiaSharp](https://github.com/mono/SkiaSharp)
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel)、[LLamaSharp](https://github.com/SciSharp/LLamaSharp)
- [Terracotta](https://github.com/burningtnt/Terracotta)（陶瓦联机）
- [BMCLAPI](https://bmclapi2.bangbang93.com/)（下载镜像）
- [Modrinth](https://modrinth.com/)（Mod 平台）
- 界面布局参考了 PCL2 的公开截图，未使用其代码或资源
