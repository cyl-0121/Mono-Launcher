# 阶段 0 报告：骨架与可行性自证

日期：2026-11-13
环境：Windows 11 26200 x64，AMD Ryzen 5 5500U（Radeon 核显），16GB RAM，屏幕 1536×864 @ DPI 125%
运行时：.NET SDK 10.0.404（本地安装于 `D:\DevTools\dotnet`，未修改系统环境变量）、Avalonia 12.1.6、SkiaSharp 3.119.4、Semantic Kernel 1.82.0、LLamaSharp 0.30.0

## 1. 已完成内容

| 项 | 状态 | 证据 |
|---|---|---|
| Avalonia 12 + .NET 10 项目骨架 | 完成 | `src/MonoLauncher.App` 编译通过并运行 |
| Semantic Kernel 引入 | 完成 | `IChatCompletionService` 抽象 + `Kernel`/`KernelFunction` 工具机制 |
| LLamaSharp 引入 | 完成 | `LocalLlamaService`（GGUF 加载、GPU 层数、上下文、流式推理封装），本机无 GGUF 模型，未做实际推理验证 |
| 原生 Liquid Glass 卡片 | 完成 | `GlassCard` + `GlassBackdrop`，Skia 自绘，无 WebView |
| 最小 Agent 对话面板 | 完成 | 选择提供商 → 发送 → 流式增量渲染，截图 `stage0-agent.png` |
| 性能档位切换 | 完成（UI） | 首页下拉框即时切换，档位影响 GlassCard 绘制路径 |

## 2. Liquid Glass 实现路径

不使用 WebView、HTML/CSS 或任何浏览器内核。整条管线在 Avalonia 的 Skia 渲染后端内完成：

1. `GlassBackdrop` 程序化生成 1300×825 级别的极光壁纸（渐变 + 径向光斑 + 网格 + 噪点），并预生成一张 1/2 降采样 + `SKImageFilter.CreateBlur(18)` 的模糊图，作为所有玻璃卡片的共享背景采样源。
2. `GlassCard : Decorator` 在 `Render` 中提交 `ICustomDrawOperation`，通过 `ISkiaSharpApiLeaseFeature.Lease()` 取得 `SKCanvas`。
3. 绘制阶段：
   - 背景降采样模糊：按卡片在窗口中的位置映射到模糊图 `DrawImage(srcRect → dstRect)`；
   - 色调叠加：半透明色 + 顶亮底暗的线性渐变；
   - 圆角折射：`ClipRoundRect` + 内侧模糊描边（折射高光）；
   - 顶部边缘高光：1px 渐变描边 + 顶部 specular 线；
   - 指针光照：`SKBlendMode.Plus` 径向渐变（可被性能档位关闭）。
4. 档位差异：
   - 高性能：双层模糊（预模糊 + 实时 `CreateBlur(10)`）+ 折射 + 高光 + 指针光；
   - 均衡：预模糊 + 轻量实时 `CreateBlur(4)` + 色调 + 高光；
   - 低功耗：静态半透明采样（无实时模糊）；
   - 关闭：纯色填充。

> 说明：当前版本折射由描边/渐变模拟，未使用 `SKRuntimeEffect` 自定义 SKSL。原因是 SkiaSharp 3.119 的 `SKRuntimeEffect` API 与旧版差异较大，先用可控的原生滤镜管线跑通并保证帧率；SKSL 版本作为后续增强项记录在路线图中。这不影响"原生渲染、无 WebView"的硬性要求。

## 3. 实测帧率

应用内自绘截图（`RenderTargetBitmap`，1300×825 物理像素）与 FPS 计数器（`TopLevel.RequestAnimationFrame` 统计）：

- 首页，均衡档：**37–42 FPS**
- Agent 页面流式输出中，均衡档：**45 FPS**（生成期间 UI 无卡顿，消息增量渲染）
- 窗口 1040×660 逻辑像素、125% DPI、集成显卡

未测：高性能档（预计更低，因为双层实时模糊）、低功耗档、关闭档。档位对帧率的影响将在阶段 D 用统一的基准脚本量化。

## 4. 可交互性说明

- Agent 面板是真实可交互的：选择"演示模式（无需 Key）"后发送消息，会以真实 SSE 风格增量流式返回文本（`MockChatService`，逐块 28ms），UI 逐帧追加。
- 真实云端提供商（OpenAI/DeepSeek/Anthropic/Gemini/Ollama/OpenRouter/智谱/通义/Kimi/硅基流动/自定义）连接器已实现，但因本环境没有可用 API Key，**未做真实调用验证**，标记为「未验证」。
- 本地 LLamaSharp 推理路径已实现，但本机没有 GGUF 模型文件，**未验证**实际推理与 GPU 层卸载。

## 5. 已知问题

- 在本机多虚拟显示器 + 125% DPI 的环境下，外部截屏工具（GDI `CopyFromScreen`/`PrintWindow`）会因 DPI 虚拟化裁切窗口右侧；应用内 `RenderTargetBitmap` 截图正常。属于截图工具问题，不是布局问题。
- 当前 FPS 在均衡档约 37–45，玻璃卡片每次绘制都会新建 `SKPaint`/`SKShader`，存在明显优化空间（阶段 D 处理）。
- LLamaSharp CPU 后端原生库体积较大，单文件发布体积待测。

## 6. 下一步（阶段 B）

版本清单、客户端/资源下载、Java 检测、启动参数与进程管理接入 UI；注册 `list_instances` / `launch_instance` / `read_log` 等 Agent 工具。
