# DeepSeek API 特性摘要

> 来源：官方 API 文档 `api-docs.deepseek.com`
> 状态：对照实现

---

## 一、模型列表

| 模型 ID | 说明 | Thinking | 上下文 | 最大输出 |
|---------|------|----------|--------|---------|
| `deepseek-v4-pro` | Pro 模型 | ✅ 支持 | 1M | 384K |
| `deepseek-v4-flash` | Flash 模型 | ✅ 支持 | 1M | 384K |
| `deepseek-chat` | 旧名，= v4-flash 非 Thinking 模式 | ❌ | — | 2026/07/24 废弃 |
| `deepseek-reasoner` | 旧名，= v4-flash Thinking 模式 | ✅ | — | 2026/07/24 废弃 |

> **结论**：直接用 `deepseek-v4-pro` 或 `deepseek-v4-flash` + `thinking` 参数控制模式。

---

## 二、Thinking 模式

### 2.1 开关控制

```json
{
  "thinking": { "type": "enabled" }
}
// 或
{
  "thinking": { "type": "disabled" }
}
```

- 默认值：**`enabled`**

### 2.2 推理强度

| 参数 | 值 | 说明 |
|------|-----|------|
| `reasoning_effort` | `high` | 默认（普通请求） |
| `reasoning_effort` | `max` | Agent 场景自动启用（如 Claude Code） |
| `low` / `medium` | — | 兼容性映射到 `high` |
| `xhigh` | — | 兼容性映射到 `max` |

### 2.3 输出格式

| 字段 | 说明 |
|------|------|
| `content` | 最终回答 |
| `reasoning_content` | 思考过程（与 content 同级返回） |

### 2.4 Thinking 模式限制

> Thinking 模式下 `temperature`、`top_p`、`presence_penalty`、`frequency_penalty` 无效。
> 传入不报错但无效果。

---

## 三、多轮对话与 reasoning_content 传递规则

| 场景 | reasoning_content 处理 |
|------|----------------------|
| 无工具调用的 assistant 回复 | **不需要**传回 API，下一轮会被忽略 |
| 有工具调用的 assistant 回复 | **必须**在所有后续请求中传回 API，否则 400 |
| 新的一轮 user 消息 | 上一轮（无工具调用）的 reasoning_content 不需要拼接 |

### 3.1 正确拼接方式

```json
// 助手消息（含工具调用时）
{
  "role": "assistant",
  "content": "...",
  "reasoning_content": "必须保留的思考内容",
  "tool_calls": [...]
}
```

---

## 四、模型功能矩阵

| 功能 | v4-pro | v4-flash |
|------|--------|----------|
| JSON Output | ✅ | ✅ |
| Tool Calls | ✅ | ✅ |
| Chat Prefix Completion (Beta) | ✅ | ✅ |
| FIM Completion (Beta) | 非 Thinking 模式 | 非 Thinking 模式 |
| Context Caching | ✅ | ✅ |

---

## 五、Context Caching

- **默认启用**，无需代码修改
- 磁盘缓存，匹配请求的前缀部分
- 缓存命中 token 计入 `prompt_cache_hit_tokens`，费用更低
- 缓存未命中 token 计入 `prompt_cache_miss_tokens`
- 缓存自动清除（数小时到数天）

### 缓存命中条件

1. **请求边界持久化**：user 输入结束位置 + 模型输出结束位置形成缓存单元
2. **公共前缀检测**：多请求共享前缀自动持久化
3. **固定 token 间隔**：长输入/输出定期分块持久化

---

## 六、计费（USD / 1M tokens）

| 模型 | Cache Hit 输入 | Cache Miss 输入 | 输出 |
|------|---------------|----------------|------|
| deepseek-v4-flash | $0.0028 | $0.14 | $0.28 |
| deepseek-v4-pro | $0.003625 | $0.435 | $0.87 |

---

## 七、本项目已适配 vs 待适配

| 特性 | 状态 | 备注 |
|------|------|------|
| `thinking` 参数 | ✅ | DeepSeekClient 已传 `{"thinking": {"type": "enabled/disabled"}}` |
| `reasoning_effort` | ✅ | 已支持 high/max |
| `reasoning_content` 回传 | ✅ | ChatMessage 已加字段，工具调用轮次正确回传 |
| Thinking 模式下屏蔽无效参数 | ✅ | temperature/top_p 仅在非 Thinking 模式发送 |
| JSON Output | ✅ | `response_format: { type: "json_object" }` 已加，受 EnableJsonOutput 控制 |
| Chat Prefix Completion | ✅ | Beta 功能，`prefix` 参数已加，受 EnablePrefixCompletion + PrefixContent 控制 |
| FIM Completion | ✅ | Beta 功能，DeepSeekClient.CompleteAsync() 支持 /v1/completions 端点 |
| Cache 状态读取 | ✅ | TokenUsage 模型解析 usage，含 prompt_cache_hit_tokens。状态栏显示缓存命中率 |
| V4 模型迁移 | ✅ | 默认模型已是 `deepseek-v4-pro`，旧名 deepseek-chat 已全部替换 |
