# MonoLauncher

> 一个面向 Windows 11 的 Minecraft: Java Edition 启动器。纯黑白界面、原生 Avalonia 渲染，内置多提供商 AI Agent 与本地模型推理，联机部分集成 [Terracotta](https://github.com/burningtnt/Terracotta)。

没有 WebView，没有 HTML / CSS。所有界面由 Avalonia 控件与 Skia 自绘。

---

## 特性

### 启动与版本

- 读取 Mojang 官方版本清单，按 **正式版 / 测试版 / 愚人节版 / 远古版 / 全部** 分类浏览（含计数）
- 两步式安装：先选版本 → 再选加载器（原版 / Fabric / Quilt / NeoForge / Forge）→ 下载
- 下载引擎：并发下载、断点续传（`.part` + sidecar）、指数退避重试、SHA-1/SHA-256 校验、原子改名
- 资源文件分批下载，进度、速度、当前文件实时显示

### 实例与 Mod

- 实例隔离：每个实例独立的 `minecraft` 目录（存档 / Mod / 配置互不影响）
- 导入现有 `.minecraft`、PCL、HMCL、Prism 实例
- Mod 识别 Fabric / Quilt / NeoForge / Forge 元数据，支持启停、删除（移到回收站）
- 从 Modrinth 搜索并安装 Mod

### 联机

- **陶瓦联机（推荐）**：通过 Terracotta 的房间码跨网络联机，无需公网 IP
  - 首次使用自动下载官方原版二进制（不随本仓库分发、未修改）
  - 创建 / 加入房间、成员列表、状态轮询、一键启动并直连
- **局域网扫描**：监听 `224.0.2.60:4445` 的原版 LAN 广播
- **手动直连**：填 `地址:端口` 直接连接

### 账户

- 微软正版登录：OAuth 设备码 → Xbox Live → XSTS → Minecraft Services
- 登录窗口显示代码、有效期倒计时，可随时换码
- 凭据经 Windows DPAPI 加密保存在本机，日志自动脱敏

### AI Agent

- 基于 Semantic Kernel 的多提供商接口：OpenAI、Azure OpenAI、Anthropic、Gemini、DeepSeek、OpenRouter、智谱、通义、Kimi、硅基流动、Ollama、LM Studio 及任意 OpenAI 兼容端点
- 流式输出、取消、工具调用中间状态
- 高风险工具（安装 / 启动 / 删除）强制原生确认框
- LLamaSharp 本地 GGUF 推理（GPU 层数 / 上下文 / 线程可调）

### 界面

- 无边框窗口 + 自绘标题栏（拖动、双击最大化、Win11 圆角、原生最小化动画）
- 顶部导航，白色选中胶囊平滑滑动，页面切换淡入上移
- 纯黑主题：`#000` 背景、`#0A0A0A` 卡片、白底黑字主按钮
- 视觉档位四档（完整 / 标准 / 简洁 / 关闭），低帧率自动降档

---

## 快速开始

### 方式一：直接运行

从 [Releases](../../releases) 下载 `MonoLauncher-win-x64.zip`，解压后双击 `MonoLauncher.exe`，无需安装 .NET。

数据目录：`%APPDATA%\MonoLauncher`。

### 方式二：从源码构建

需要 .NET SDK 10.0.100 或更高。

```bash
git clone <this-repo>
cd MonoLauncher
dotnet build MonoLauncher.slnx -c Release
dotnet run --project src/MonoLauncher.App
```

发布单文件：

```bash
dotnet publish src/MonoLauncher.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish/win-x64
```

---

## 项目结构

```
MonoLauncher/
├─ MonoLauncher.slnx
├─ src/
│  ├─ MonoLauncher.Core/     核心：版本、下载、启动、账户、Loader、Mod、联机、更新
│  ├─ MonoLauncher.Agent/    AI：Semantic Kernel 封装、工具、本地 GGUF
│  ├─ MonoLauncher.App/      Avalonia 界面（MVVM + 自绘控件）
│  └─ MonoLauncher.Cli/      命令行：list / install / launch / java / nettest / terracotta …
├─ tests/MonoLauncher.Tests/ xUnit 单元测试
└─ docs/                     使用指南、界面说明、性能档位、测试记录、许可调研
```

---

## 技术栈

| 层 | 选型 |
|---|---|
| 运行时 | .NET 10（自包含发布） |
| UI | Avalonia 12.1.6 + SkiaSharp |
| 模式 | CommunityToolkit.Mvvm（`[ObservableProperty]` / `[RelayCommand]`） |
| AI | Microsoft.SemanticKernel 1.82 |
| 本地推理 | LLamaSharp 0.30（CPU 后端） |
| 测试 | xUnit |
| 字体 | Segoe UI Variable（系统字体，随系统提供） |

---

## 验证状态

**已在真实环境验证：**

- 版本清单拉取、1.21.1 原版下载与启动（NeoForge 21.1.1 实例可进入游戏）
- NeoForge 安装（含 BMCLAPI 镜像回退、版本元数据补全）
- 下载引擎断点续传、校验、重试
- 实例导入（PCL / HMCL / Prism / .minecraft 格式）
- Modrinth 搜索与安装、Mod 启停
- 局域网扫描与直连启动
- Terracotta 二进制下载、启动、`/state` 轮询、优雅退出
- 微软设备码申请（拿到 `user_code` 与 `verification_uri`）

**未验证（无对应条件，代码按公开协议实现）：**

- 微软正版登录的 XBL / XSTS / Minecraft Services 换取环节（无正版账号）
- Fabric / Quilt / Forge 的实际安装（NeoForge 已验证）
- 陶瓦联机的创建 / 加入房间全流程（需两名玩家与游戏内局域网开放）
- 真实 LLM 调用与本地 GGUF 推理（无 API Key / 模型文件）
- 整合包（`.mrpack`）与 CurseForge 支持（未实现）

---

## 已知限制

1. 仅支持 Windows 10/11 x64；不提供 macOS / Linux 构建。
2. 微软登录端点依赖网络可达；部分网络环境需自备代理（设置页可填）。
3. 更新公告需要 GitHub API 可访问，失败时显示缓存。
4. 陶瓦联机的公共节点由 Terracotta 项目维护，本仓库不担保其可用性。
5. 单文件发布体积约 70 MB（含 LLamaSharp CPU 后端）。

---

## 许可证

本项目原创代码以 **MIT** 发布，见 [LICENSE](LICENSE)。

第三方依赖与参考实现的许可见 [docs/reference-licenses.md](docs/reference-licenses.md)。要点：

- **Avalonia / SkiaSharp / Semantic Kernel / LLamaSharp**：MIT
- **Terracotta**：AGPL-3.0-or-later。本仓库**不包含**其二进制，运行时通过 `terracotta --hmcl` 启动官方原版，并只调用其本地 HTTP API；界面中标注了版权信息，符合其 AGPL 例外条款
- **Minecraft**：Mojang Studios 商标。本软件不包含任何游戏本体、资源或账号

> 本项目早期使用 Apple SF Pro 字体做界面，因其许可仅限 Apple 平台，现已改为系统字体 Segoe UI Variable。若你希望使用自定义字体，可直接修改 `MainWindow.axaml` 与 `HomeView.axaml` 中的 `FontFamily`。

---

## 免责声明

- 本软件与 Mojang Studios、Microsoft 无隶属或授权关系。
- 本软件不提供、不分发任何 Minecraft 游戏文件；所有下载均来自 Mojang 官方服务地址，请使用合法拥有的账号。
- 使用本软件造成的存档损坏、数据丢失、账号封禁或服务器纠纷，作者与贡献者不承担责任。请自行备份重要存档。
- 禁止将本软件用于传播盗版、绕过正版验证、搭建未经授权的付费私服或其他违法违规行为。

首次启动会显示《用户协议与免责声明》，不同意将直接退出。

---

## 致谢

- [Avalonia](https://avaloniaui.net/)、[SkiaSharp](https://github.com/mono/SkiaSharp)
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel)、[LLamaSharp](https://github.com/SciSharp/LLamaSharp)
- [Terracotta](https://github.com/burningtnt/Terracotta)（陶瓦联机）
- [BMCLAPI](https://bmclapi2.bangbang93.com/)（下载镜像）
- [Modrinth](https://modrinth.com/)（Mod 平台）
- 界面布局参考了 PCL2 的公开截图（未使用其代码或资源）
