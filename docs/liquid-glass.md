# Liquid Glass 实现说明

## 硬性边界

- **没有 WebView，没有 HTML/CSS，没有嵌入浏览器内核。** 项目内不引用 `WebView2`、`CefSharp`、`Microsoft.Web.WebView2`，也不加载任何 HTML。
- 全部绘制在 Avalonia 12 的 Skia 后端内完成，使用 `ICustomDrawOperation` + `ISkiaSharpApiLeaseFeature` 直接拿 `SKCanvas`。
- 不使用 `LiquidGlassAvaloniaUI` / `Fluid.Avalonia` / `CompositionMaterial.Avalonia` 的代码或包，只借鉴其公开 README 中的着色阶段划分。

## 管线阶段

`GlassCard.Render` 提交 `GlassDrawOperation`，在 Skia 画布上按顺序执行：

### 1. 背景降采样模糊
- `GlassBackdrop` 在启动/窗口尺寸变化时程序化生成一张极光壁纸（深色渐变 + 多个径向光斑 + 网格 + 噪点 + 暗角）。
- 同时生成一张 **1/2 降采样 + `SKImageFilter.CreateBlur(18)`** 的模糊图。所有玻璃卡片共享这张图，避免每张卡片重复模糊。
- 卡片按自己在窗口中的位置把 `srcRect` 映射到模糊图，`DrawImage` 采样。
- 高性能档额外叠加 `CreateBlur(10)` 实时模糊；均衡档叠加 `CreateBlur(4)`。

### 2. 色调叠加
- 半透明 `TintColor` 填充（默认 `#161E34`，透明度按卡片配置）。
- 再叠一层顶亮底暗的线性渐变，让卡片有体积感。

### 3. 圆角折射
- `ClipRoundRect` 裁剪后，沿圆角内侧绘制一圈模糊的渐变描边（`SKMaskFilter.CreateBlur`），模拟光线在玻璃边缘的折射亮边。
- 折射强度随 `Refraction` 属性缩放，并乘以 DPI 缩放，保证高 DPI 下不消失。

### 4. 顶部边缘高光
- 1px 渐变描边（左上亮 → 右下暗）。
- 顶部一条高亮的 specular 线。

### 5. 可选指针光照
- 鼠标进入卡片后，用 `SKBlendMode.Plus` 绘制径向渐变光斑。
- 受性能档位与「启用指针光照」开关控制。

## 性能档位差异

| 档位 | 背景 | 实时模糊 | 折射 | 高光 | 指针光 |
|---|---|---|---|---|---|
| 高性能 | 预模糊图 | `CreateBlur(10)` | 有 | 有 | 有 |
| 均衡 | 预模糊图 | `CreateBlur(4)` | 有 | 有 | 有 |
| 低功耗 | 原图 + 40 alpha | 无 | 无 | 有 | 无 |
| 关闭 | 纯色填充 | 无 | 无 | 无 | 无 |

档位切换即时生效：`PerformanceState.Apply` 触发后遍历视觉树对每个 `GlassCard` 调用 `InvalidateVisual()`。

## 帧率实测

环境：AMD Ryzen 5 5500U（Radeon 核显）、1536×864 @ 125% DPI、窗口 1160×720 逻辑像素。

| 场景 | FPS |
|---|---|
| 首页，均衡档 | 37–45 |
| 实例页，均衡档 | 37–56 |
| Agent 页面流式输出 | 45 |
| 发布版 EXE 首页 | 44 |

（FPS 来自 `TopLevel.RequestAnimationFrame` 统计，显示在顶栏。）

## 未采用 SKRuntimeEffect 的原因

原计划用 `SKRuntimeEffect` 写 SKSL 做更精确的边缘折射。SkiaSharp 3.119 的 `SKRuntimeEffect` API 与 2.x 差异较大，且当前环境缺少稳定的 SKSL 调试手段。为保证交付可用，先用 Skia 原生的 `SKImageFilter` + 渐变描边实现等效视觉效果；SKSL 版本列为路线图项。

这不影响「原生渲染、无 WebView」的硬性要求：所有像素都由 Skia 在 Avalonia 的渲染管线内产生。

## 无 WebView 证明

```bash
# 项目中不存在任何 WebView 依赖或引用
grep -ri "webview\|cefsharp\|webview2" src/ --include=*.cs --include=*.csproj --include=*.axaml
# 结果：无匹配（docs 中的说明文字除外）
```

`Directory.Packages.props` 中只有 Avalonia、SkiaSharp、Semantic Kernel、LLamaSharp、CommunityToolkit.Mvvm、Microsoft.Extensions.*、xunit。
