# GlassCraft

一个面向 Windows 11 的 Minecraft: Java Edition 启动器。界面参照 PCL2 的**顶部导航栏**结构，黑白极简主题（自绘标题栏、SF Pro 字体、无边框窗口），原生 **Avalonia 12 + Skia** 渲染；内置基于 **Semantic Kernel** 的多提供商 AI Agent 与 **LLamaSharp** 本地推理。

没有 WebView，没有 HTML/CSS。

---

## 当前状态

本仓库是在一台 Windows 11 x64 机器上实际开发、编译、运行并测试的产物。

### 已实测通过

| 功能 | 证据 |
|---|---|
| 官方版本清单（923 个版本） | 截图 `docs/mc2-versions.png` |
| 原版 1.21.1 完整安装（Client JAR + 73 个库 + 4036 个资源对象，SHA-1 校验） | 日志与数据目录 |
| Java 21 自动检测与选择 | 日志 `检测到 2 个 Java 运行时` |
| 原版 1.21.1 启动到主菜单 | 截图 `docs/minecraft-window.png` |
| NeoForge 21.1.1 安装（官方安装器 + 镜像重写）与启动 | 截图 `docs/neoforge-running.png` |
| UI 内一键启动实例并实时捕获日志 | 截图 `docs/ui-launch-neoforge.png` |
| Minecraft 风格界面 / 无边框窗口 / 自绘标题栏 | 截图 `docs/mc-home3.png` |
| 局域网联机（LAN 发现 + 直连） | 截图 `docs/mc-network.png` |
| Agent 流式对话（演示模式） | 截图 `docs/mc2-agent.png` |
| Agent 工具真实执行（实例列表 / Modrinth 搜索） | CLI `tools` 输出 |
| 实例导入（扫描 + 复制存档，源目录只读） | CLI `import` |
| 单元测试 24 项 | `dotnet test` 全绿 |

### 已实现但未在真实环境验证

| 功能 | 原因 |
|---|---|
| Microsoft 正版登录 / Token 刷新 | 无受控测试账户 |
| 真实云端 LLM 提供商 | 无 API Key |
| 本地 GGUF 推理（LLamaSharp） | 本机无 GGUF 模型文件 |
| Fabric / Quilt / Forge 安装 | 仅实现并编译，未实际跑通 |
| 公网联机（中继） | 未实现，需要第三方中继服务 |
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

### P4 界面
- 页面：启动（首页）/ 下载 / 版本 / Mod / 联机 / 日志 / AI / 设置
- 参照 PCL2 的顶部导航栏；黑白极简主题（黑色顶栏、白色卡片、灰色背景）
- 首页：左侧账户/实例/启动按钮，右侧 Today + 更新公告（GitHub Release 自动拉取）
- 无边框窗口 + 自绘标题栏（拖动 / 双击最大化 / 最小化 / 最大化 / 关闭）
- 最大化不覆盖任务栏；DPI 自适应
- 界面效果四档：完整 / 标准 / 简洁 / 关闭（即时生效）
- 首次启动显示《用户协议与免责声明》，拒绝则退出

### P5 AI Agent
- Semantic Kernel 抽象（自实现 OpenAI 兼容 + Anthropic 连接器）
- 预设：OpenAI、Azure OpenAI、Anthropic、Gemini、DeepSeek、OpenRouter、智谱、通义、Kimi、硅基流动、Ollama、LM Studio、自定义 OpenAI 兼容端点
- 流式输出、取消、工具调用中间状态
- 高风险工具（安装/启动/删除）强制原生确认对话框
- API Key 存 Windows DPAPI，日志自动脱敏
- LLamaSharp 本地模型加载（GPU 层数/上下文/线程/批大小可调）

### P6 联机
- 局域网扫描：监听 `224.0.2.60:4445` 原版 LAN 广播，解析 MOTD 与端口
- 一键加入：选择实例 → 启动并直连（1.20+ 走 `quickPlayMultiplayer`，旧版走 `--server/--port`）
- 手动地址直连
- 公网联机（中继）未实现，界面中明确说明

### P7 性能系统
- 视觉档位 + 动画开关 + 自动降档
- 下载并发纳入设置页
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

或直接运行 `scripts/build.ps1 -Publish`。

产物：`publish/win-x64/GlassCraft.exe`（单文件，无需安装 .NET 运行时）。

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

详细操作见 [docs/usage.md](docs/usage.md)。

---

## 项目结构

```
GlassCraft/
  GlassCraft.slnx
  Directory.Build.props / Directory.Packages.props
  src/
    GlassCraft.Core/          版本、下载、Java、认证、启动、实例、Loader、Mod、联机发现、日志
    GlassCraft.Agent/         Semantic Kernel 提供商、Agent 会话、工具、LLamaSharp
    GlassCraft.App/           Avalonia UI、Minecraft 风格控件、ViewModel
    GlassCraft.Cli/           命令行验证工具
  tests/GlassCraft.Tests/      xUnit 单元测试
  docs/                        设计、许可证、测试、路线图
  scripts/                     构建与运行辅助脚本
```

---

## 关键设计

### 界面
`GlassCard` 是自绘面板：深灰填充、1px 边框、顶部高光与底部阴影（按档位裁剪）。背景 `AuroraBackground` 用 Skia 程序化生成石质纹理（像素网格 + 随机明暗块 + 顶部草绿光晕），尺寸变化时重建并缓存。

窗口使用 `SystemDecorations="None"`，标题栏完全自绘：拖动、双击最大化、三个窗口按钮；最大化时由 Avalonia 处理工作区，不覆盖任务栏。

### 下载引擎
每个文件写入 `<dest>.part`，配 sidecar 记录来源 URL；仅当哈希校验通过后原子重命名。并发由 `SemaphoreSlim` 控制，失败按指数退避重试。

### 版本继承
从根父版本向下合并，子版本字段优先，`libraries` 与 `arguments` 父先子后拼接，并检测循环继承。

### NeoForge 网络适配
官方 `maven.neoforged.net` 在部分网络不可达。`LoaderService` 读取官方安装器内的 JSON，把下载 URL 原地改写为 BMCLAPI 镜像（JAR 结构不变），预下载 `neoform`，再运行官方安装器。这是**下载源适配**，没有绕过授权或校验。

### 联机
`LanDiscovery` 加入 `224.0.2.60` 组播组监听原版 LAN 公告，只读不探测。加入时把地址写入启动参数：现代版本通过 `quickPlayMultiplayer` 变量（由 `has_quick_plays_support` 规则启用），旧版本回退到 `--server/--port`。

### Agent 工具安全
`AgentSession` 按工具名分级；`launch_instance` / `install_version` / `delete_instance` / `install_mod` 必须经过原生确认对话框，拒绝后不重试。

---

## 文档索引

- [使用指南](docs/usage.md)
- [界面设计](docs/ui-design.md)
- [阶段 0 报告](docs/stage0-report.md)
- [参考仓库许可证调研](docs/reference-licenses.md)
- [Agent 提供商配置](docs/agent-providers.md)
- [性能与界面档位](docs/performance-tiers.md)
- [测试记录](docs/testing.md)
- [后续路线图](docs/roadmap.md)

---

## 已知限制

1. 本机为 125% DPI + 多虚拟显示器环境，外部 GDI 截图可能裁切窗口；`docs/` 中截图由应用自身渲染或 PrintWindow 生成。
2. NeoForge 安装依赖 BMCLAPI 镜像；官方网络恢复后可去掉重写逻辑。
3. Forge 安装器未实测；Fabric/Quilt 未实测。
4. LLamaSharp 本地模型未实测（无 GGUF 文件），GPU 层卸载未验证。
5. 微软登录未验证（无账户），代码基于公开协议实现。
6. 公网联机（中继）未实现。
7. Mod 页面只支持单个 Mod 安装，整合包未实现。
8. 单文件发布体积较大（含 LLamaSharp CPU 后端与 SF Pro 字体）。
9. **字体许可**：SF Pro 为 Apple 字体，其许可仅允许 Apple 平台使用。仓库未提交字体文件（`Assets/Fonts` 被 .gitignore 忽略），本地构建需自行放置；公开发布请替换为 Inter 等开源字体。
10. 更新公告需要 GitHub 网络可达；国内网络可能失败，失败时显示缓存。

---

## 许可证

本项目原创代码以 **MIT** 发布。第三方依赖与参考仓库的许可证见 [docs/reference-licenses.md](docs/reference-licenses.md)。特别注意：

- 未复制 PCL2（自定义专有许可）、HMCL（GPL-3.0 + 附加条款）、Prism Launcher（GPL-3.0）的任何源代码或素材。
- LiquidGlassAvaloniaUI / Fluid.Avalonia / CompositionMaterial.Avalonia 仅借鉴思路，未引入依赖（当前界面已改为扁平风格，不再使用玻璃管线）。
- Minecraft 是 Mojang Studios 的商标；本项目不包含任何 Mojang 游戏资源。
