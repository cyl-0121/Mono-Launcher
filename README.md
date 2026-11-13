# GlassCraft

一个面向 Windows 11 的 Minecraft: Java Edition 启动器。原生 **Avalonia 12 + Skia** 渲染，自绘 Liquid Glass 视觉管线，内置基于 **Semantic Kernel** 的多提供商 AI Agent 与 **LLamaSharp** 本地推理。

没有 WebView，没有 HTML/CSS 伪装，玻璃效果全部走 Avalonia 的 Skia 渲染后端。

---

## 目录

- [当前状态](#当前状态)
- [功能清单](#功能清单)
- [构建](#构建)
- [运行](#运行)
- [项目结构](#项目结构)
- [关键设计](#关键设计)
- [文档索引](#文档索引)
- [已知限制](#已知限制)
- [许可证](#许可证)

---

## 当前状态

本仓库是在一台 Windows 11 x64 机器上实际开发、编译、运行并测试的产物。状态如实标注：

### 已实测通过

| 功能 | 证据 |
|---|---|
| 官方版本清单（923 个版本） | 截图 `docs/ui2-versions.png` |
| 原版 1.21.1 完整安装（Client JAR + 73 个库 + 4036 个资源对象，SHA-1 校验） | 日志与 `%APPDATA%\GlassCraft\data` |
| Java 21 自动检测与选择 | 日志 `检测到 2 个 Java 运行时` |
| 原版 1.21.1 启动到主菜单 | 截图 `docs/minecraft-window.png` |
| NeoForge 21.1.1 安装（官方安装器 + 镜像重写）与启动 | 截图 `docs/neoforge-running.png` |
| UI 内一键启动实例并实时捕获日志 | 截图 `docs/ui-launch-neoforge.png` |
| Liquid Glass 四档渲染 | 截图 `docs/stage0-ui-home.png` 等 |
| Agent 流式对话（演示模式） | 截图 `docs/stage0-agent.png` |
| Agent 工具真实执行（实例列表 / Modrinth 搜索） | CLI `tools` 输出 |
| 实例导入（扫描 + 复制存档，源目录只读） | CLI `import` |
| 单元测试 24 项 | `dotnet test` 全绿 |

### 已实现但未在真实环境验证

| 功能 | 原因 |
|---|---|
| Microsoft 正版登录 / Token 刷新 | 无受控测试账户 |
| 真实云端 LLM 提供商（OpenAI/DeepSeek/Anthropic/Gemini 等） | 无 API Key |
| 本地 GGUF 推理（LLamaSharp） | 本机无 GGUF 模型文件 |
| Fabric / Quilt / Forge 安装 | 仅实现并编译，未实际跑通 |
| `.mrpack` / CurseForge 整合包 | 未实现 |
| PCL2 / HMCL / Prism 真实实例导入 | 仅实现扫描器并用本机 `.minecraft` 验证流程 |

---

## 功能清单

### P0 启动链路
- 官方版本清单（Release / Snapshot / 历史）
- Client JAR、Libraries、Assets、Natives 下载与 SHA-1 校验
- 版本 `inheritsFrom` 继承与 JSON 合并
- 启动参数生成（新式 `arguments` + 旧式 `minecraftArguments`，规则求值，占位符替换）
- Java 检测（注册表 / PATH / 常见目录 / 启动器运行时）与版本匹配
- Microsoft 设备码登录 → Xbox Live → XSTS → Minecraft Services（代码完整，未验证）
- 离线账户（与官方一致的 v3 UUID）
- Classpath / JVM / 游戏参数构建
- 进程启动、日志捕获、崩溃报告定位
- 安装进度、速度、剩余文件展示

### P1 Mod Loader
- Fabric / Quilt：官方 meta profile JSON
- NeoForge / Forge：官方安装器（自动重写镜像源、预下载 neoform、生成 launcher_profiles）
- NeoForge 的 classpath 特殊处理（patched client 由 libraryDirectory 加载）

### P2 实例与 Mod
- 多实例共存，独立游戏目录
- 创建 / 启动 / 结束进程 / 移入回收站（不直接删除）
- 导入现有 `.minecraft` / PCL2 / HMCL / Prism（复制存档，不覆盖、不改源）
- Mod 扫描（fabric.mod.json / quilt.mod.json / mods.toml）、启停（`.disabled` 重命名）、删除（回收站）
- Modrinth 搜索与安装（SHA-1 校验）
- 自定义 JVM 参数、分辨率、内存

### P3 下载引擎
- 有界并发（默认 8）
- HTTP Range 断点续传（`.part` + sidecar 校验 URL）
- 指数退避重试 + 失败文件二次重试
- SHA-1 / SHA-256 校验
- 临时文件 + 原子提交
- 可取消、可配置代理/并发/镜像
- 同一文件去重
- 失败信息可定位（URL、状态码、重试次数）

### P4 UI 与 Liquid Glass
- 首页 / 游戏下载 / 实例 / Mod / 下载管理 / 日志 / Agent / 设置
- 暗色、中文、DPI 自适应
- 四档性能（高性能 / 均衡 / 低功耗 / 关闭），即时切换
- 帧率实时显示、低帧自动降档建议

### P5 AI Agent
- Semantic Kernel 抽象（自实现 OpenAI 兼容 + Anthropic 连接器）
- 预设：OpenAI、Azure OpenAI、Anthropic、Gemini、DeepSeek、OpenRouter、智谱、通义、Kimi、硅基流动、Ollama、LM Studio、自定义 OpenAI 兼容端点
- 流式输出、取消、工具调用中间状态
- 高风险工具（安装/启动/删除）强制原生确认对话框
- API Key 存 Windows DPAPI，日志自动脱敏
- LLamaSharp 本地模型加载（GPU 层数/上下文/线程/批大小可调）

### P6 性能系统
- 视觉档位 + 动画开关 + 指针光照 + 自动降级
- 下载并发纳入性能面板（设置页）
- 本地 AI 参数与性能面板联动

---

## 构建

### 环境要求
- Windows 10/11 x64
- .NET SDK 10.0（`global.json` 固定 10.0，`rollForward: latestFeature`）
- 可选：Java 8/17/21（启动游戏需要，启动器会检测）

### 命令

```powershell
# 还原 + 编译
dotnet build GlassCraft.slnx -c Debug

# 单元测试
dotnet test tests/GlassCraft.Tests/GlassCraft.Tests.csproj

# 发布 self-contained 单文件 EXE
dotnet publish src/GlassCraft.App/GlassCraft.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish/win-x64
```

产物：`publish/win-x64/GlassCraft.exe`（单文件，无需安装 .NET 运行时）。

> 说明：`GlassCraft.Agent` 引用了 `LLamaSharp.Backend.Cpu`，本地推理原生库会显著增大产物体积；若只需要云端 Agent，可移除该 PackageReference。

### 开发期直接运行

```powershell
dotnet run --project src/GlassCraft.App/GlassCraft.App.csproj
```

命令行工具（安装/启动/诊断）：

```powershell
dotnet run --project src/GlassCraft.Cli -- list
dotnet run --project src/GlassCraft.Cli -- install 1.21.1
dotnet run --project src/GlassCraft.Cli -- install 1.21.1 --loader neoforge
dotnet run --project src/GlassCraft.Cli -- launch 1.21.1 Player
dotnet run --project src/GlassCraft.Cli -- tools
```

---

## 运行

双击 `GlassCraft.exe`。首次启动会创建数据目录：

```
%APPDATA%\GlassCraft\
  config\        设置、DPAPI 加密的账户与密钥
  data\
    versions\    版本 JSON 与 Client JAR
    libraries\   共享库
    assets\      Asset Index 与对象
    instances\   实例（instance.json + minecraft/）
    downloads\   安装器与缓存
  logs\          结构化日志（JSON Lines）
```

界面语言为简体中文，暗色主题，随窗口大小与 DPI 缩放自适应。

---

## 项目结构

```
GlassCraft/
  GlassCraft.slnx
  Directory.Build.props / Directory.Packages.props
  src/
    GlassCraft.Core/          版本、下载、Java、认证、启动、实例、Loader、Mod、日志
    GlassCraft.Agent/         Semantic Kernel 提供商、Agent 会话、工具、LLamaSharp
    GlassCraft.App/           Avalonia UI、Liquid Glass、ViewModel
    GlassCraft.Cli/           命令行验证工具
  tests/GlassCraft.Tests/      xUnit 单元测试
  docs/                        设计、许可证、测试、路线图
  scripts/                     构建与运行辅助脚本
```

---

## 关键设计

### Liquid Glass
`GlassCard` 在 `Render` 中提交 `ICustomDrawOperation`，通过 `ISkiaSharpApiLeaseFeature` 拿到 `SKCanvas`，依次执行：背景降采样模糊 → 色调叠加 → 圆角折射描边 → 顶部边缘高光 → 可选指针光照。背景来自 `GlassBackdrop` 生成的极光壁纸（预生成 1/2 降采样模糊图供所有卡片共享）。详见 [docs/liquid-glass.md](docs/liquid-glass.md)。

### 下载引擎
`DownloadEngine` 每个文件写入 `<dest>.part`，配 sidecar 记录来源 URL；仅当哈希校验通过后原子重命名。并发由 `SemaphoreSlim` 控制，失败按指数退避重试。详见源码 `src/GlassCraft.Core/Download/DownloadEngine.cs`。

### 版本继承
`VersionListService.ResolveInheritanceAsync` 从根父版本向下合并，子版本字段优先，`libraries` 与 `arguments` 父先子后拼接，并检测循环继承。

### NeoForge 网络适配
官方 `maven.neoforged.net` 在部分网络不可达。`LoaderService` 会读取官方安装器内的 `install_profile.json` / `version.json`，把下载 URL 原地改写为 BMCLAPI 镜像（JAR 结构保持不变），并预下载 `neoform` 包，再运行官方安装器。这是**下载源适配**，没有绕过任何授权或校验。

### Agent 工具安全
`AgentSession` 在调用工具前按工具名分级；`launch_instance` / `install_version` / `delete_instance` / `install_mod` 为高风险，必须经过 `Dialogs.ConfirmToolAsync` 原生对话框显式确认，拒绝后不会重试。

---

## 文档索引

- [阶段 0 报告](docs/stage0-report.md)
- [参考仓库许可证调研](docs/reference-licenses.md)
- [Agent 提供商配置](docs/agent-providers.md)
- [性能档位对照](docs/performance-tiers.md)
- [Liquid Glass 实现说明](docs/liquid-glass.md)
- [测试记录](docs/testing.md)
- [后续路线图](docs/roadmap.md)

---

## 已知限制

1. 本机为 125% DPI + 多虚拟显示器环境，外部 GDI 截图会裁切窗口右侧；应用内截图正常（`docs/` 中带 `ui-` 前缀的截图均由应用自身渲染）。
2. NeoForge 安装依赖 BMCLAPI 镜像；官方网络恢复后可去掉重写逻辑。
3. Forge 安装器未实测。
4. Fabric/Quilt 的 profile 安装未实测（代码路径简单但未跑通）。
5. LLamaSharp 本地模型未实测（无 GGUF 文件），GPU 层卸载未验证。
6. 微软登录未验证（无账户），代码基于公开协议实现。
7. Mod 页面目前只支持单个 Mod 安装，整合包（`.mrpack`/CurseForge）未实现。
8. 未实现系统级 Java 环境变量修改（按需求禁止）。
9. 单文件发布体积较大（含 LLamaSharp CPU 后端）。

---

## 许可证

本项目原创代码以 **MIT** 发布。第三方依赖与参考仓库的许可证见 [docs/reference-licenses.md](docs/reference-licenses.md)。特别注意：

- 未复制 PCL2（自定义专有许可）、HMCL（GPL-3.0 + 附加条款）、Prism Launcher（GPL-3.0）的任何源代码或素材。
- LiquidGlassAvaloniaUI / Fluid.Avalonia / CompositionMaterial.Avalonia 仅借鉴着色思路，未引入依赖。
- Minecraft 是 Mojang Studios 的商标；本项目不包含任何 Mojang 游戏资源。
