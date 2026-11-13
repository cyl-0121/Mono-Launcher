# Agent 提供商配置

GlassCraft 的 Agent 支持两种协议：

- **OpenAI 兼容**（`/chat/completions`，支持流式 SSE 与工具调用）
- **Anthropic Messages**（原生协议）

所有提供商的 API Key 通过 Windows DPAPI 加密存储在 `%APPDATA%\GlassCraft\config\secrets.bin`，**不会**写入配置文件、日志或崩溃报告。

---

## 预设提供商

| ID | 名称 | Base URL | 默认模型 | 需要 Key |
|---|---|---|---|---|
| `openai` | OpenAI | `https://api.openai.com/v1` | `gpt-4o-mini` | 是 |
| `azure-openai` | Azure OpenAI | 自填（含 deployment） | `gpt-4o-mini` | 是 |
| `anthropic` | Anthropic Claude | `https://api.anthropic.com/v1` | `claude-sonnet-4-5` | 是 |
| `gemini` | Google Gemini | `https://generativelanguage.googleapis.com/v1beta/openai` | `gemini-2.5-flash` | 是 |
| `deepseek` | DeepSeek | `https://api.deepseek.com/v1` | `deepseek-chat` | 是 |
| `openrouter` | OpenRouter | `https://openrouter.ai/api/v1` | `openai/gpt-4o-mini` | 是 |
| `zhipu` | 智谱 GLM | `https://open.bigmodel.cn/api/paas/v4` | `glm-4-flash` | 是 |
| `dashscope` | 通义千问 | `https://dashscope.aliyuncs.com/compatible-mode/v1` | `qwen-plus` | 是 |
| `moonshot` | 月之暗面 Kimi | `https://api.moonshot.cn/v1` | `kimi-k2-0905-preview` | 是 |
| `siliconflow` | 硅基流动 | `https://api.siliconflow.cn/v1` | `Qwen/Qwen2.5-7B-Instruct` | 是 |
| `ollama` | Ollama（本地） | `http://localhost:11434/v1` | `qwen2.5:7b` | 否 |
| `lmstudio` | LM Studio（本地） | `http://localhost:1234/v1` | `local-model` | 否 |
| `custom` | 自定义兼容端点 | 自填 | 自填 | 可选 |

---

## 添加自定义 OpenAI 兼容端点

在 Agent 页面选择「自定义 OpenAI 兼容端点」，填写：

1. **Base URL**：到 `/v1` 为止，不要带 `/chat/completions`
   - 例：`https://api.example.com/v1`
2. **模型名**：服务端要求的 model 字段
3. **API Key**：可留空（某些本地服务不需要）
4. **额外请求头**：部分服务要求 `X-Api-Version` 等，可在配置文件中添加

或直接编辑 `%APPDATA%\GlassCraft\config\agent.json`：

```json
{
  "ProviderId": "custom",
  "Model": "my-model",
  "BaseUrl": "https://api.example.com/v1"
}
```

示例文件见 [`providers.example.json`](providers.example.json)（不含真实 Key）。

---

## 兼容性要求

自定义端点需要满足：

- `POST {baseUrl}/chat/completions`
- 请求体：`model`、`messages`、`stream`、`temperature`、`max_tokens`
- 流式响应：`text/event-stream`，每行 `data: {...}`，以 `data: [DONE]` 结束
- 工具调用：`tools`（function schema）与 `tool_calls` 增量

不支持工具调用的模型仍可对话，只是无法驱动启动器操作。

---

## 本地模型（LLamaSharp）

本地推理不经过 HTTP，直接加载 GGUF：

- 模型放到 `%APPDATA%\GlassCraft\data\models\*.gguf`
- 在设置中配置 GPU 层数、上下文长度、线程数、批大小
- 加载在后台线程进行，UI 显示状态，不阻塞启动

未验证项：本机没有 GGUF 模型，实际推理与 GPU 卸载未测试。

---

## 安全边界

- 高风险工具（`install_version`、`launch_instance`、`delete_instance`、`install_mod`）必须由用户在原生对话框中显式确认。
- 拒绝后 Agent 不会重试。
- 密钥在日志、命令行、崩溃报告中均被 `SecretRedactor` 脱敏。
