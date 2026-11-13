# 后续路线图

按优先级排列，每项都标注了当前状态与验收标准。

## P0 — 待补完

1. **Microsoft 登录实测**
   - 当前：设备码 → XBL → XSTS → Minecraft Services 全链路已实现，未用真实账户验证
   - 验收：真实账户登录、刷新令牌、过期自动刷新、错误码（无 Xbox 档案 / 未成年）提示正确

2. **云端 LLM 实测**
   - 当前：OpenAI 兼容与 Anthropic 连接器已实现，无 Key
   - 验收：至少 3 家提供商（DeepSeek / OpenAI / Ollama）真实流式对话 + 工具调用

3. **Fabric / Quilt 安装实测**
   - 当前：profile JSON 下载 + 库安装已实现
   - 验收：1.21.1 + Fabric 完整安装并启动

## P1 — 功能补齐

4. **Forge 安装实测**
   - 与 NeoForge 共用安装器逻辑，但未跑通
   - 验收：1.20.1 + Forge 安装启动

5. **整合包**
   - `.mrpack` 解析（Modrinth pack 格式：`modrinth.index.json` + overrides）
   - CurseForge API（需 API Key，遵守其条款）
   - 验收：导入一个整合包，依赖完整下载，启动成功

6. **实例导出**
   - 导出为 zip（含 instance.json、mods、config、saves 可选）
   - 导出为 `.mrpack`
   - 验收：导出后可被本启动器重新导入

7. **本地模型实测**
   - 下载一个小 GGUF（如 Qwen2.5-0.5B-Instruct Q4）验证加载与推理
   - 验证 `GpuLayerCount` 对显存/速度的影响
   - 验收：CPU 与（若有）Vulkan 后端均能响应

## P2 — 体验与健壮性

8. **一键基准**
   - 运行 5 秒合成负载（多玻璃卡片 + 动画），给出档位建议
   - 验收：基准结果写入日志，建议可一键应用

9. **玻璃管线优化**
   - 缓存 `SKPaint`/`SKShader`，避免每帧分配
   - 尝试 `SKRuntimeEffect` 实现真正的边缘折射
   - 验收：均衡档在集成显卡上稳定 60 FPS

10. **下载故障注入测试**
    - 断网、超时、403/404、磁盘写满、SHA-1 不匹配
    - 验收：每种场景有明确错误提示与可恢复路径

11. **实例导入实测**
    - 在真实 PCL2 / HMCL / Prism 安装目录上验证扫描与导入
    - 验收：存档、Mod、配置完整迁移，源目录零改动

12. **Java 运行时管理**
    - 自动下载并管理 Adoptium Temurin（按版本需求）
    - 验收：无 Java 的机器可一键安装并启动

## P3 — 生态

13. **Mod 更新检查**
    - 通过 Modrinth 版本 API 对比本地文件哈希
    - 验收：可一键更新并保留旧版本备份

14. **账户与皮肤**
    - 皮肤预览、账户切换器、离线账户批量管理
    - 验收：账户页可预览并切换

15. **多语言**
    - 当前仅简体中文
    - 验收：en-US 完整翻译

---

## 已知技术债

- `GlassCard` 使用 `Decorator.Padding` 隐藏基类成员，应改为 `new` 或自定义命名。
- Avalonia 12 的 `TextBox.Watermark` 已弃用，应迁移到 `PlaceholderText`。
- `SKPaint.FilterQuality` 已弃用，应迁移到 `SKSamplingOptions`。
- `Bitmap.Save(Stream, int?)` 已弃用，应迁移到 `BitmapEncoderOptions`。
- `NativeLibraryConfig.Instance` 已弃用，应改用 `NativeLibraryConfig.All`。
- NeoForge 镜像重写依赖安装器内部 JSON 结构，升级安装器版本时需回归测试。
