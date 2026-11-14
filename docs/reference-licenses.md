# 参考仓库许可证调研

调研日期：2026-11-13
项目：MonoLauncher（Minecraft 启动器，Avalonia 12.1.6 + .NET 10，含 AI Agent 面板）
方法：逐个仓库用 `web_fetch` 读取 LICENSE 原文，优先 `raw.githubusercontent.com`，分支先 `main` 后 `master`；未取到原文的条目均已注明原因，不做条款臆测。

## 0. 原文获取情况（先说结论）

任务给出的 10 个 URL 中，**5 个直接命中**（HMCL、Prism、x-minecraft-launcher、semantic-kernel、LLamaSharp），其余 5 个 URL 本身有问题，已全部定位到真实路径并补齐原文：

| # | 任务给定 URL | 实际结果 | 真实路径 |
|---|---|---|---|
| 1 | `Meloong-Git/PCL/main/LICENSE` | 404 —— 仓库许可证文件名为英式拼写 **`LICENCE`** | `Meloong-Git/PCL/main/LICENCE` ✅ |
| 2 | `HMCL-dev/HMCL/main/LICENSE` | 200 | 同左 ✅ |
| 3 | `PrismLauncher/PrismLauncher/develop/LICENSE` | 200 | 同左 ✅ |
| 4 | `Voxelum/x-minecraft-launcher/master/LICENSE` | 200 | 同左 ✅ |
| 5 | `BangBang93/BMCLAPI/master/LICENSE` | 404 —— **该仓库不存在**（GitHub API 亦 404） | `bangbang93/openbmclapi/master/LICENSE` ✅（BMCLAPI 服务端实现） |
| 6 | `microsoft/semantic-kernel/main/LICENSE` | 200 | 同左 ✅ |
| 7 | `SciSharp/LLamaSharp/master/LICENSE` | 200 | 同左 ✅ |
| 8 | `Cosseyyyyy/LiquidGlassAvaloniaUI/main/LICENSE` | 404 —— **owner 不存在** | `KaranocaVe/LiquidGlassAvaloniaUI/main/LICENSE` ✅ |
| 9 | `cdmoscow/Fluid.Avalonia/main/LICENSE` | 404 —— **owner 不存在** | `Alpaq92/Fluid.Avalonia/main/LICENSE` ✅ |
| 10 | `svntokio/CompositionMaterial.Avalonia/main/LICENSE` | 404 —— **owner 不存在** | `HelloWRC/CompositionMaterial.Avalonia/master/LICENSE.txt` ✅ |

也就是说：**10 个仓库的许可证原文全部拿到**，但其中 5 条是按真实仓库路径取得的，任务给定的 URL 本身不可用。下文"未能获取原文"仅出现在 BMCLAPI 这一格的语义澄清上——BMCLAPI 是**镜像服务**，服务本身没有、也不应有 LICENSE 文件，MIT 只覆盖其服务端实现 `openbmclapi`。

## 结论速览

| 仓库 | 许可证（SPDX） | 复制代码？ | 对 MonoLauncher 的结论 |
|---|---|---|---|
| PCL2（Meloong-Git/PCL） | 非 SPDX：自定义《PCL 分发有限许可》+《合理使用指南》 | ❌ 不可 | **仅可参考设计，不得复制代码**；重度使用会被要求改名、开源、蓝色主色相、购买劝导 |
| HMCL（HMCL-dev/HMCL） | GPL-3.0 + GPLv3 §7 附加条款 | ❌ 不可 | 强 copyleft，**复制即传染**；只读设计参考 |
| Prism Launcher | GPL-3.0（无附加条款） | ❌ 不可 | 同 GPL 传染；**只读设计参考** |
| x-minecraft-launcher（Voxelum） | MIT | ✅ 可 | 可复用，保留版权声明；TS/Electron 代码对 C# 的复用价值以协议与数据模型为主 |
| BMCLAPI / openbmclapi | MIT（服务端实现） | ✅ 可 | 代码可复用；**服务本身**按公开使用规范接入（UA、限速、不滥用），非许可证授权 |
| microsoft/semantic-kernel | MIT | ✅ 可 | NuGet 引用即合规，保留 NOTICE |
| SciSharp/LLamaSharp | MIT | ✅ 可 | NuGet 引用；注意随包分发的 llama.cpp 原生库 |
| LiquidGlassAvaloniaUI | MIT | ✅ 可（但不必） | 只借鉴着色器/管线思路，**不整体依赖** |
| Fluid.Avalonia | MIT | ✅ 可（但不必） | MIT 主题库，可选用；本项目已自绘玻璃，不引入 |
| CompositionMaterial.Avalonia | MIT（`LICENSE.txt`） | ✅ 可（但强烈不建议） | 实验性、仅 Windows、**锁定 Avalonia 12.1.4**，与项目 12.1.6 冲突；只借鉴思路 |

## 逐仓库分析

### 1. PCL2 — Meloong-Git/PCL

- **原文获取**：给定 `LICENSE` 404；实际文件 `LICENCE`（3168 字节），已读取全文。⚠️ 该文件中的《PCL 分发有限许可》正文仅指向石墨文档链接（`shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-X0bo`），**正文未能获取原文**（需登录/动态渲染）；下文条款以仓库内完整可见的《PCL 存储库合理使用指南》为准，指南本身声明"不是法律文件或协议"，因此对其约束力应作保守解读。
- **许可证类型**：**非 SPDX**，自定义《PCL 分发有限许可》，正文仅一行指向 shimo 文档，随后是《PCL 存储库合理使用指南》（文件自述"仅作为说明，不是法律文件或协议"，但明确"除许可明确授予的权利外，开发者保留全部权利"）。因此它既不是开源许可，也不是可无条件复制的公共代码——**与已知事实一致**。
- **关键条款**：
  - **轻度使用**（仅参考极小部分内容，或制作的软件与 PCL 实质功能无重叠）：需 ① 适当署名；② 不得与 PCL 混淆或暗示关联（禁用 `Potato Craft Launcher`、`PCL 手机版` 等名称）。
  - **重度使用**（基于仓库修改；或较大程度参考以重新实现 PCL 实质功能，例如启动/下载 Java 版 MC、下载管理 Mod）：① 明确声明为第三方独立二次创作；② 名称必须 **以 `Plain Craft Launcher (PCL)` 开头**并加第三方后缀；③ 关于页首位署名龙腾猫跃 + 赞助链接、**公开源代码**、继续使用该文件作指南；④ 若含 MC 启动功能，须实现与 `McLaunchPrecheck` 一致（或更激进）的正版购买劝导；⑤ Windows 上不得出现类似 PCL 赞助解锁的功能/表现，例如**界面主色相必须为蓝色**。
  - 无商业使用限制条文，但"保留全部权利"意味着默认无授权；上述条件实际等价于 **copyleft + 品牌条款 + 产品行为条款**。
- **对本项目**：MonoLauncher 必然要实现"启动 MC、下载、Mod 管理"，属于其定义的重度使用；接受条款等于放弃自主许可、强制开源、强制蓝色主色相并复刻购买劝导，与项目定位冲突。**结论：可参考设计（信息架构、安装流程、错误提示文案思路），不得复制任何代码、资源、文案，不得使用 PCL 名称或近似命名。**

### 2. HMCL — HMCL-dev/HMCL

- **原文获取**：`main/LICENSE` HTTP 200，已读全文（GPLv3 全文 + 末尾附加条款）。
- **许可证类型**：**GPL-3.0**，附带两条 GPLv3 §7 附加条款（文件中标题为 "Additional Terms for Hello Minecraft! Launcher"）。**与已知事实一致**：GPLv3 及附加条款。
- **关键条款**：
  - 标准 GPLv3：分发衍生作品须以 GPLv3 授权**整个作品**（§5c），提供对应源码（§6），修改需标注（§5a），不得附加进一步限制（§10）。
  - 附加条款 1（依 §7(c)）：分发修改版时，必须以合理方式**更改软件名称或版本号**以区别于原版。
  - 附加条款 2（依 §7(b)）：**不得移除软件中展示的版权声明**。
  - 无商业使用禁止——GPL 允许收费分发，但衍生必须开源。
- **对本品**：任何源码级复制、翻译式改写（即便换成 C#）都可能被认定为衍生作品，导致 MonoLauncher 整体须以 GPLv3 开源。**结论：只读设计参考，禁止复制/移植代码；不得引入其 jar/二进制资源。若未来确需 GPL 组件，须走进程隔离（独立可执行、命令行/HTTP 通信）并单独评估。**

### 3. Prism Launcher — PrismLauncher/PrismLauncher

- **原文获取**：`develop/LICENSE` HTTP 200，GPLv3 标准全文，**末尾无附加条款**。
- **许可证类型**：**GPL-3.0**。与已知事实一致。
- **关键条款**：标准 GPLv3 传染性；分发二进制须提供对应源码；不得附加限制；专利授权条款（§11）随分发生效。其第三方依赖（Qt、QuaZip 等）各自许可，与本仓库 LICENSE 无关。
- **对本品**：**只读设计参考**（实例隔离、组件版本管理、Java 自动探测的交互设计属思想层面，不受版权保护；但代码、QSS/QML、图标、翻译文件均不可复制）。禁止链接其 GPL 代码。

### 4. x-minecraft-launcher — Voxelum/x-minecraft-launcher

- **原文获取**：`master/LICENSE` HTTP 200。
- **许可证类型**：**MIT**，`Copyright (c) 2023 ci010 (cijhn@hotmail.com)`。
- **关键条款**：授予使用、复制、修改、合并、发布、再许可、销售等全部权利；**唯一义务**是在所有副本或实质部分中保留上述版权声明与许可声明；无担保。
- **对本品**：**可复用，需保留版权声明**。实际可复用的是协议层与数据模型（CurseForge/Modrinth 接口封装思路、实例元数据结构、下载队列设计），其 TS/Electron 代码不宜直接搬进 C#；若借鉴具体实现片段，须在文件头注明来源与 MIT 声明，并在 `THIRD-PARTY-NOTICES` 收录全文。

### 5. BMCLAPI — bangbang93/openbmclapi

- **原文获取**：给定 `BangBang93/BMCLAPI` 的 raw 与 GitHub API **均为 404，该仓库不存在**；BMCLAPI 是镜像**服务**（`bmclapi2.bangbang93.com`），其开源部分是服务端实现 `bangbang93/openbmclapi`，`master/LICENSE` HTTP 200。
- **许可证类型**：**MIT**，`Copyright (c) 2019 bangbang93`。
- **关键条款**：同标准 MIT，保留版权与许可声明即可；无 copyleft、无商业限制。
- **重要区分**：MIT 覆盖的是**服务端代码**；MonoLauncher 作为客户端**调用 BMCLAPI 镜像**属于使用第三方服务，授权来自服务方的公开使用规范（通常要求设置可识别的 User-Agent、遵守限速、不得滥用/盗链、必要时申请白名单），不是 MIT 授予的权利。
- **对本品**：代码层面可复用（如需自建镜像或参考其缓存/回源策略），保留声明；**服务接入层面**应遵守 BMCLAPI 公开条款，默认 UA 标注 `MonoLauncher/<version>`、支持失败回退官方源、不并发轰炸。**不要把"MIT"误读为"可以无限量免费拉取镜像流量"。**

### 6. microsoft/semantic-kernel

- **原文获取**：`main/LICENSE` HTTP 200。
- **许可证类型**：**MIT**，`Copyright (c) Microsoft Corporation.`（NuGet 包 `Microsoft.SemanticKernel` 1.82.0 同许可）。
- **关键条款**：标准 MIT；保留版权与许可声明；无担保；含专利/商标不授权（MIT 文本本身不含专利条款，Microsoft 另有 MIT 之外的商标政策，不影响代码使用）。
- **对本品**：**可直接依赖 NuGet 包**，在关于页/NOTICE 中列出 "Semantic Kernel — MIT — Copyright (c) Microsoft Corporation" 即满足要求。注意 SK 的部分 connector/实验包可能引用其他 Microsoft 许可（如 `Microsoft.Extensions.AI` 仍为 MIT），升级时复核。

### 7. SciSharp/LLamaSharp

- **原文获取**：`master/LICENSE` HTTP 200。
- **许可证类型**：**MIT**，`Copyright (c) 2025 SciSharp STACK`。
- **关键条款**：标准 MIT。需注意**随包分发的原生依赖**：`LLamaSharp.Backend.Cpu` 内含 llama.cpp 编译产物（llama.cpp 为 MIT），本项目当前只用 CPU 后端，无额外限制；若切换到 CUDA 后端，会涉及 NVIDIA CUDA 运行时/cuBLAS 的专有许可（允许随应用分发，但禁止逆向与单独再分发），且模型权重（GGUF）另有各自许可，不能默认商用。
- **对本品**：NuGet 引用合规，NOTICE 列 "LLamaSharp — MIT — Copyright (c) 2025 SciSharp STACK" 与 "llama.cpp — MIT"；**模型文件不入库、不随安装包分发**，由用户自备或下载时单独提示许可。

### 8. LiquidGlassAvaloniaUI — KaranocaVe/LiquidGlassAvaloniaUI

- **原文获取**：给定 `Cosseyyyyy/...` 404（owner 不存在）；真实 `KaranocaVe/LiquidGlassAvaloniaUI`，`main/LICENSE` HTTP 200，README 亦已读。
- **许可证类型**：**MIT**，`Copyright (c) 2025 KaranocaVe`。**与已知事实（MIT）一致**。
- **关键条款**：标准 MIT，保留声明即可。
- **README 的关键技术判断（已核实原话）**：作者在 "Reflection" 一节明确写道 **"AvaloniaUI is still relatively immature when it comes to high-end visual effects"**——框架在底层渲染管线与高级着色器集成上仍缺"成熟顺滑"的体验，做这类效果常常是在"和底层 API 搏斗"。实现路径为 `Avalonia + SkiaSharp(SKRuntimeEffect)`，管线是 vibrancy → blur → 圆角矩形透镜折射（可选色散）→ 边缘高光，且**必须使用 Skia 渲染器**。
- **对本品**：**MIT 允许复用，但只借鉴着色器思路，不整体依赖**。理由：① 它绑定 `SKRuntimeEffect` 与 Skia 渲染器，而本项目阶段 0 的经验是 SkiaSharp 3.119 的 `SKRuntimeEffect` API 与旧版差异较大、直接用原生滤镜管线更稳（见 `stage0-report.md`）；② 其折射/色散是模拟，视觉收益与调试成本不成正比。**做法：读它的管线分层与参数（模糊半径、边缘高光、色散偏移），用自己的 `ICustomDrawOperation` 重写，不复制其控件类。**

### 9. Fluid.Avalonia — Alpaq92/Fluid.Avalonia

- **原文获取**：给定 `cdmoscow/...` 404（owner 不存在）；真实 `Alpaq92/Fluid.Avalonia`，`main/LICENSE` HTTP 200，README 已读。
- **许可证类型**：**MIT**，`Copyright (c) 2026 Fluid.Avalonia contributors`（NuGet 页亦标 MIT）。**与已知事实（MIT）一致**。
- **关键条款**：标准 MIT；README 声明其 Fluent 2 token 与 WinUI 资源结构**移植自 MIT 的 microsoft-ui-xaml 与 WinUI 3 Gallery**，内置强调色预设来自 MIT 的 Open Color——即它的 MIT 是干净的，但再分发时也应保留这些上游署名。
- **技术边界（已核实）**：它是**主题库**（Fluent 2 tokens 覆盖 FluentTheme），背景能力靠 `FluidWindow.TransparencyEnabled` 走平台原生（Windows Mica、macOS vibrancy、KDE KWin blur），**其他平台退化为不透明**；本身依赖 Avalonia ≥ 12.1.5。它并不提供一个跨平台的软件渲染玻璃着色器（那部分在配套的 `Fluid.Avalonia.Acrylic`）。
- **对本品**：**可选用但不引入**。MonoLauncher 已有自绘 `GlassCard`/`GlassBackdrop`，若整体换成 Fluid 主题会与现有设计语言打架，且其玻璃依赖原生合成、在无合成器的环境退化。**结论：借鉴其 token 分层与"原生优先、软件兜底"的降级策略；不添加包依赖。**

### 10. CompositionMaterial.Avalonia — HelloWRC/CompositionMaterial.Avalonia

- **原文获取**：给定 `svntokio/...` 404（owner 不存在）；真实 `HelloWRC/CompositionMaterial.Avalonia`，`main` 与 `master` 的 `LICENSE` 均 404，**实际文件为 `master/LICENSE.txt`**，HTTP 200；README.zh-CN 已读。
- **许可证类型**：**MIT**，`Copyright (c) 2026 HelloWRC`。**与已知事实（MIT）一致**。
- **README 的硬约束（已核实原文）**：
  - 顶部 WARNING：**"本项目尚处于实验阶段，且仅支持 Windows"**，依赖 Avalonia 内部实现，任何内部变更都可能随时导致功能失效，**不建议在生产环境使用**。
  - 要求**精确使用 Avalonia / Avalonia.Win32 12.1.4**（本项目是 **12.1.6**，ABI 契约不匹配）。
  - 仅 Windows；材质采样桌面/窗口背景而非同一交换链；不透明父元素会遮住材质；**不支持 NativeAOT**；`Liquid Glass 不实现真实的位移、折射或色散`。
- **对本品**：**不依赖，只借鉴思路**。其"把材质 Visual 插入 `Windows.UI.Composition` 视觉树 + `FallbackBrush` 兜底 + 诊断开关"的架构值得学，但 12.1.4 锁版本 + 实验性 + 仅 Windows，对本项目（12.1.6、需跨平台兜底）是不可接受的风险。**结论：读设计，不引包，不抄代码。**

## 本项目的复用策略

### 分层规则

1. **思想层（可自由借鉴）**：界面布局、交互流程、信息架构、错误文案的"表达方式"、协议字段与 API 用法。版权不保护思想与功能，但**不要逐字照抄文案、不要复制图标/截图/翻译文件**。
2. **代码层（白名单）**：仅 MIT / Apache-2.0 / BSD 类宽松许可可复制或改写，且必须：文件头注明来源仓库 + 版权行 + 许可名；`THIRD-PARTY-NOTICES.md` 收录许可全文；保留原始版权声明不得删除。
3. **代码层（黑名单）**：GPL/LGPL/AGPL 与一切自定义许可（含 PCL）**禁止复制、禁止改写移植、禁止静态/动态链接**。确需使用时只能进程隔离（独立 exe + 命令行/HTTP/JSON-RPC），并单独做法务评估。
4. **默认动作**：拿不准就"只读、重写"。任何从参考仓库粘贴超过约 10 行的片段，都应视为需要记录来源的复用。

### 具体到每个仓库

| 仓库 | 允许 | 禁止 |
|---|---|---|
| PCL2 | 读安装/下载流程、错误分类、UI 组织；独立重写实现 | 复制任何 `.cs`/`.xaml`/图片/文案；使用 PCL 名称或近似名；复制其购买劝导文案 |
| HMCL | 读实例隔离、Java 探测、版本 JSON 处理的设计 | 复制/翻译代码；引入其资源包；链接其库 |
| Prism | 读多实例、组件版本管理、更新器设计 | 复制 C++/QML 代码、图标、翻译 |
| x-minecraft-launcher | 协议封装、数据模型、下载队列思路；MIT 片段复用 | 删除版权声明；把 MIT 代码伪装成自有原创 |
| BMCLAPI | 参考缓存/回源策略；按规范接入镜像 | 无限量滥用镜像；去掉 UA；把镜像当自有 CDN |
| semantic-kernel | 直接 NuGet 依赖 | 删除包内 LICENSE/NOTICE |
| LLamaSharp | 直接 NuGet 依赖（CPU 后端） | 把 GGUF 模型打包分发；忽略 CUDA 后端许可 |
| LiquidGlassAvaloniaUI | 借鉴 blur→折射→高光管线与参数 | 整体替换自绘管线；复制控件类 |
| Fluid.Avalonia | 借鉴 token 分层、原生优先降级 | 引入包替换现有主题 |
| CompositionMaterial.Avalonia | 读 Composition 树插入 + FallbackBrush 思路 | 引入包（12.1.4 锁版本）、复制私有 ABI 反射代码 |

### 落地动作（建议）

- 新建 `THIRD-PARTY-NOTICES.md`，按"包名 / 版本 / 许可 / 版权行 / 用途"逐条登记，构建时随安装包分发。
- 在"关于"页提供"开源许可"入口，展示 Avalonia、Semantic Kernel、LLamaSharp、SkiaSharp、CommunityToolkit.Mvvm、xunit 等的许可文本。
- 对任何"参考实现"提交，在 PR 模板里加一栏：是否复制了外部代码？来源与许可？
- 供应链：`dotnet list package --vulnerable` + 定期复核 `PackageReference` 的许可变化（尤其 SK 的 connector 分包）。
- **不引入** `CompositionMaterial.Avalonia`、`Fluid.Avalonia`、`LiquidGlassAvaloniaUI` 作为包依赖；玻璃效果继续走自绘 `ICustomDrawOperation`（阶段 0 已验证 37–45 FPS）。

## 第三方依赖清单

依据 `MonoLauncher/Directory.Packages.props`（集中包版本管理）与各上游许可原文。标注"未逐一下载原文"的条目，结论来自 NuGet 元数据与上游仓库公开声明，**引入前建议在 CI 中再核一次**。

### 运行时依赖

| 包 | 版本 | 许可证 | 版权/备注 | 需做什么 |
|---|---|---|---|---|
| Avalonia | 12.1.6 | MIT | `Copyright (c) AvaloniaUI OÜ`（已读 `licence.md`） | NOTICE 登记 |
| Avalonia.Desktop | 12.1.6 | MIT | 同上 | 同上 |
| Avalonia.Themes.Fluent | 12.1.6 | MIT | 同上 | 同上 |
| Avalonia.Fonts.Inter | 12.1.6 | MIT（字体为 SIL OFL 1.1） | Inter 字体本身是 OFL-1.1，随包分发需保留 OFL 文本 | 补 Inter 的 OFL 声明 |
| Avalonia.Controls.DataGrid | 12.1.2 | MIT | 同 Avalonia | NOTICE 登记 |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | `.NET Foundation`（未逐一下载原文） | NOTICE 登记 |
| SkiaSharp | 3.119.4 | MIT | `Copyright (c) Microsoft Corporation`；内含 Skia（BSD-3-Clause） | 同时登记 Skia 的 BSD 声明 |
| Microsoft.SemanticKernel | 1.82.0 | MIT | `Copyright (c) Microsoft Corporation`（已读 LICENSE） | NOTICE 登记 |
| Microsoft.SemanticKernel.Connectors.OpenAI | 1.82.0 | MIT | 同上 | 同上 |
| LLamaSharp | 0.30.0 | MIT | `Copyright (c) 2025 SciSharp STACK`（已读 LICENSE） | NOTICE 登记 |
| LLamaSharp.Backend.Cpu | 0.30.0 | MIT | 含 llama.cpp 原生库（MIT） | 另登记 llama.cpp |
| Microsoft.Extensions.DependencyInjection / Logging / Logging.Console / Http / Hosting | 10.0.15 | MIT | `Copyright (c) .NET Foundation`（未逐一下载原文） | NOTICE 登记 |
| System.Security.Cryptography.ProtectedData | 10.0.15 | MIT | 同上 | 同上 |
| System.IO.Hashing | 10.0.15 | MIT | 同上 | 同上 |
| System.Text.Json | 10.0.15 | MIT | 同上 | 同上 |

### 测试依赖

| 包 | 版本 | 许可证 | 备注 |
|---|---|---|---|
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | `Copyright (c) .NET Foundation`（未逐一下载原文） |
| xunit | 2.9.3 | **Apache-2.0** | 已读 `xunit/xunit` LICENSE：Apache-2.0；其中 `ConsoleHelper.cs` 例外为 MIT（源自 MSBuild） |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | 与 xunit 同 |

### 清单层面的风险提示

1. **Avalonia 版本分裂**：项目主包 12.1.6，而 `Avalonia.Controls.DataGrid` 固定在 12.1.2。MIT 不限制，但跨版本混用有 ABI 风险，建议对齐到 12.1.6（与本调研的许可结论无关，属工程风险）。
2. **Apache-2.0 的专利与声明义务**：xunit 仅在测试期使用，不分发，义务可忽略；若将来引入 Apache-2.0 的运行时代码，需附 `NOTICE` 并保留修改声明。
3. **原生/模型资产**：`LLamaSharp.Backend.Cpu` 的原生 `.dll` 会随包分发（MIT 合规）；**GGUF 模型不得随安装包分发**，其许可由模型作者决定。
4. **字体**：Inter（OFL-1.1）与 Avalonia 自带字体在"关于/许可"页应单列，OFL 要求保留字体名与许可文本，不得单独售卖字体。
5. **不引入 GPL**：当前清单中**没有任何 GPL/LGPL/AGPL 组件**，这是项目保持自主许可的前提，评审新包时必须继续满足。

---

## 8. 运行期集成的外部程序：Terracotta

`docs/usage.md` 的联机功能通过 [burningtnt/Terracotta](https://github.com/burningtnt/Terracotta) 提供。它**不是**编译期依赖，而是运行时下载的独立进程。

| 项 | 内容 |
|---|---|
| 许可证 | **AGPL-3.0-or-later**，附例外条款 |
| 集成方式 | 启动器不打包、不修改其二进制；首次使用时从 GitHub / Gitee 下载官方原版，通过 `terracotta --hmcl <file>` 启动，只调用它的本地 HTTP API（`/state`、`/state/scanning`、`/state/guesting`、`/panic`） |
| 例外条款 | ① 以打包方式包含**未修改**二进制且不静态/动态链接；② 通过其进程间接口交互，**并在 UI 明显处标注版权** |
| 本项目做法 | 两者都满足：二进制不随 EXE 分发（运行时下载）、无链接、联机页右下角标注 `联机由 Terracotta 提供 · AGPL-3.0 · github.com/burningtnt/Terracotta` |
| 义务 | 保留上述标注；不得修改二进制后继续套用例外；不得移除版权信息 |

> 注意：Terracotta 内部使用 EasyTier（Apache-2.0），其网络行为与公共节点由 Terracotta 项目负责，本项目不做担保。

---

**一句话总结**：10 个仓库的许可证原文全部取到（其中 PCL 因文件名为 `LICENCE`、另 4 个因任务 URL 的 owner/仓库名有误而改走真实路径）；PCL 是自定义许可、HMCL 与 Prism 是 GPL-3.0（HMCL 另有两条 §7 附加条款），其余 7 项为 MIT，故结论是"GPL/自定义一律只读参考，MIT 可复用但需保留版权声明"。运行期集成的 Terracotta 为 AGPL-3.0，按例外条款以"未修改二进制 + HTTP API + UI 署名"的方式使用。
