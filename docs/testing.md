# 测试记录

日期：2026-11-14
环境：Windows 11 26200 x64、AMD Ryzen 5 5500U、16GB、1536×864 @125% DPI
运行时：.NET SDK 10.0.404、Avalonia 12.1.6、SkiaSharp 3.119.4、Semantic Kernel 1.82.0、LLamaSharp 0.30.0

---

## 单元测试（已执行，全部通过）

```
dotnet test tests/MonoLauncher.Tests/MonoLauncher.Tests.csproj
通过: 24，失败: 0，跳过: 0
```

覆盖：

| 测试类 | 内容 |
|---|---|
| JavaTests | 版本号解析（21.0.7 / 1.8.0_442 / 17-ea）、按目标版本选择 Java |
| OfflineIdentityTests | 离线 UUID 与官方 v3 算法一致（Notch 已知值）、长度校验 |
| LaunchArgumentTests | 引号分词、accessToken 脱敏、legacy session token 脱敏、现代参数构建 |
| VersionMergeTests | 父子版本合并（主类覆盖、库拼接、参数拼接、assets 继承） |
| MavenTests | Maven 坐标转路径、classifier 与 @zip 扩展名 |
| SecretRedactorTests | `sk-` 前缀、Bearer、已知值脱敏 |
| DownloadVerificationTests | SHA-1 不匹配检测、文件大小不匹配检测 |
| RuleTests | OS 规则匹配、库规则 allow/disallow |

---

## 端到端测试（已执行）

### 1. 原版 1.21.1 安装

- 命令：`MonoLauncher.Cli install 1.21.1`
- 结果：73 个核心文件 + 4036 个资源对象全部下载并通过 SHA-1 校验
- 首次运行因单个大文件 60 秒总超时失败；修复为「超时只作用于响应头 + 失败二次重试」后完整通过
- 断点续传验证：中断后重跑，2.8 秒补齐剩余 187 个文件

### 2. 原版 1.21.1 启动

- 命令：`MonoLauncher.Cli launch 1.21.1 Player`
- 结果：Java 21 启动，日志出现 `Setting user: Player`、`LWJGL version 3.3.3+5`、`OpenAL initialized`，游戏窗口标题 `Minecraft 1.21.1`，主菜单截图 `docs/minecraft-window.png`

### 3. NeoForge 21.1.1 安装

- 命令：`MonoLauncher.Cli install 1.21.1 --loader neoforge`
- 过程：
  - 官方 `maven.neoforged.net` 不可达 → 自动回退到 BMCLAPI 获取版本列表
  - 官方安装器下载成功，但内部 JSON 的 Maven URL 不可达 → 代码原地重写为 BMCLAPI 镜像
  - 安装器缺少 `launcher_profiles.json` → 自动创建
  - `neoform` 包下载失败 → 自动预下载
- 结果：生成 `versions/neoforge-21.1.1/neoforge-21.1.1.json`，安装器输出 `Successfully installed client into launcher.`

### 4. NeoForge 启动

- 命令：`MonoLauncher.Cli launch neoforge-21.1.1 Player`
- 首次失败：`Modules minecraft and _1._21._1 export package ...`（classpath 同时含原版 jar 与 patched client）
- 修复：检测 `BootstrapLauncher` 主类，classpath 不再加入原版 jar
- 结果：窗口标题 `Minecraft NeoForge* 1.21.1`，主菜单截图 `docs/neoforge-running.png`

### 5. UI 内启动实例

- 命令：`MonoLauncher.exe --launch-instance inst-neoforge-1211 --screenshot docs/ui-launch-neoforge.png`
- 结果：实例页显示「运行中 (pid 15232)」，游戏日志实时滚动，截图 `docs/ui-launch-neoforge.png`

### 6. 实例导入

- 扫描本机 `%APPDATA%\.minecraft` → 识别为「原版 .minecraft」
- `MonoLauncher.Cli import .minecraft` → 创建独立实例，源目录未修改
- 结果：`已导入 .minecraft，存档 0，Mod 0`（源目录本身为空）

### 7. Agent 工具

- `MonoLauncher.Cli tools` 直接调用工具实现：
  - `list_instances` 返回两个实例的真实 JSON
  - `list_installed_versions` 返回 `1.21.1`、`neoforge-21.1.1`
  - `search_modrinth("jei","1.21.1","neoforge")` 返回 JEI 等 8 个真实项目（JEI 8548 万下载）

### 8. 渲染与性能

- 均衡档：首页 37–45 FPS、实例页 37–56 FPS、发布版 44 FPS
- 应用内截图（RenderTargetBitmap）验证 1450×900 输出，玻璃背景模糊、色调、边缘高光、指针光均可见
- 档位切换即时生效（`InvalidateVisual` 全树刷新）

### 9. 发布产物

- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
- 产物：`publish/win-x64/MonoLauncher.exe`，60.9 MB，单文件
- 运行验证：`MonoLauncher.exe --page home --screenshot` 输出 `docs/published-home.png`（44 FPS）

---

## 未验证项（明确标注）

| 项 | 原因 | 状态 |
|---|---|---|
| Microsoft 登录 / Token 刷新 | 无受控测试账户 | 未验证 |
| 真实云端 LLM 调用 | 无 API Key | 未验证 |
| 本地 GGUF 推理 / GPU 卸载 | 无模型文件 | 未验证 |
| Fabric / Quilt 安装 | 仅实现，未跑通 | 未验证 |
| Forge 安装 | 仅实现，未跑通 | 未验证 |
| 整合包（.mrpack / CurseForge） | 未实现 | 未实现 |
| 真实 PCL2 / HMCL / Prism 实例导入 | 仅本机 .minecraft 验证 | 部分验证 |
| 高性能 / 低功耗 / 关闭档精确帧率 | 未逐档量化 | 未量化 |
| 断网、磁盘不足、文件损坏故障注入 | 仅代码路径处理 | 未系统测试 |

---

## 环境相关说明

- 本机开启火绒安全软件时，新编译的 EXE 首次联网会被拦截（`WSAEACCES`），需在火绒中放行。放行后所有网络测试通过。
- 本机为多虚拟显示器 + 125% DPI，外部 GDI 截图会裁切窗口；应用内截图不受影响。
