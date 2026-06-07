# dcode CLI 设计文档

> 深度适配 DeepSeek V4 Pro 的编码 CLI 助手

## 项目概述

- **命令名**：`dcode`
- **npm 包名**：`dcode-cli`
- **内部代号**：DeepCode
- **技术栈**：TypeScript / Node.js
- **API**：DeepSeek 官方 API（OpenAI 兼容格式）
- **阶段**：MVP，仅自用

---

## 一、背景与目标

### 1.1 动机

Claude Code 对 DeepSeek V4 Pro 存在全链路兼容性问题（系统提示词、工具调用格式、上下文管理、输出解析等均不匹配），需要一个从零为 DeepSeek 深度优化的替代品。

### 1.2 MVP 范围

| 包含 | 暂不做 |
|------|--------|
| Agent 对话循环（思考→工具调用→观察→继续） | Git 操作 |
| 文件读写/精确编辑 | 子代理系统 |
| Shell 命令执行 | 技能系统 |
| 代码搜索（grep/glob） | MCP 协议 |
| 流式输出 + 会话持久化 | 图片/多模态 |
| DeepSeek 专属系统提示词 + 工具格式适配 | 插件/扩展 |

---

## 二、项目结构

```
dcode-cli/
├── src/
│   ├── index.ts              # 入口，解析 CLI 参数（commander.js）
│   ├── cli/
│   │   ├── interface.ts      # 交互式 REPL 界面（readline + chalk + ora）
│   │   └── output.ts         # 流式输出渲染（分层：reasoning / content / tool）
│   ├── agent/
│   │   ├── loop.ts           # Agent 主循环（核心）
│   │   ├── system-prompt.ts  # DeepSeek 专属系统提示词
│   │   └── types.ts          # Agent 相关类型定义
│   ├── tools/
│   │   ├── registry.ts       # 工具注册中心
│   │   ├── read_file.ts      # 文件读取（行偏移+行数限制）
│   │   ├── write_file.ts     # 文件写入/覆盖
│   │   ├── edit_file.ts      # 精确编辑（字符串替换，含回滚）
│   │   ├── run_shell.ts      # Shell 命令执行（需用户确认）
│   │   ├── search_content.ts # 内容正则搜索（等价 grep）
│   │   └── search_files.ts   # 文件名 Glob 匹配
│   ├── api/
│   │   ├── client.ts         # DeepSeek API 客户端（封装 openai SDK）
│   │   ├── retry.ts          # 重试策略（429/5xx/rate_limit/busy）
│   │   └── message-builder.ts # 消息格式构建（含 reasoning_content 管理）
│   ├── context/
│   │   ├── manager.ts        # 上下文管理器（KV 缓存感知的追加策略）
│   │   ├── compressor.ts     # 上下文压缩（追加摘要，不破坏前缀）
│   │   └── tokenizer.ts      # Token 计数辅助
│   └── config/
│       ├── loader.ts         # 配置加载（环境变量 + JSON）
│       └── defaults.ts       # 默认配置
├── package.json
├── tsconfig.json
└── README.md
```

**设计原则**：
- 每个目录只暴露一个公共入口（`index.ts`），内部模块各司其职
- `agent/loop.ts` 不直接调用 `openai`，通过 `api/client.ts` 隔离
- 工具全部通过 `tools/registry.ts` 注册和调用，Agent 不硬编码工具列表

---

## 三、Agent 循环

### 3.1 核心流程

```typescript
async function agentLoop(userMessage: string, context: AgentContext) {
  let messages = buildMessages(userMessage, context);
  let round = 0;

  while (round < context.maxRounds) {
    // 1. 调用 API（思考模式始终开启）
    const response = await apiClient.chat(messages, {
      tools: registry.getDefinitions(),
      signal: abortController.signal,
      onReasoningChunk: (text) => output.renderReasoning(text),
      onContentChunk:  (text) => output.renderContent(text),
      onToolCall:      (call) => output.renderToolCall(call),
    });

    // 2. 将 assistant 消息（含 reasoning_content）整体追加
    messages.push(response.message);

    // 3. 有工具调用 → 执行 → 结果回送 → 继续循环
    if (response.message.tool_calls?.length) {
      for (const tc of response.message.tool_calls) {
        const result = await registry.execute(tc.function.name, tc.function.arguments);
        messages.push({
          role: "tool",
          tool_call_id: tc.id,
          content: result,
        });
      }
      round++;

      // 软上限检查
      if (round >= context.softLimit) {
        const shouldContinue = await cli.confirm(`已执行 ${round} 轮，继续？`);
        if (!shouldContinue) break;
      }
      continue;
    }

    // 4. 无工具调用 → 最终回复
    return response.message.content;
  }

  throw new MaxRoundsExceededError(round);
}
```

### 3.2 轮次上限三层策略

| 层级 | 默认值 | 行为 |
|------|--------|------|
| 软上限 | 50 轮 | 提示用户确认是否继续 |
| 硬上限 | 200 轮 | 强制停止，防止无限循环 |
| 压缩触发 | Token 用量达 80% | 自动追加摘要消息，释放空间 |

可通过 `--max-rounds` 或配置文件调整。

### 3.3 DeepSeek 独有特性适配

| 特性 | 处理方式 |
|------|----------|
| `reasoning_content` | 始终保留在 assistant 消息中，提高 KV 缓存命中率 |
| `thinking: {type: "enabled"}` | 始终开启，通过 `extra_body` 传入 |
| `reasoning_effort: "high"` | Agent 场景自动升为 `max` |
| 不支持 temperature | 思考模式下不传该参数 |

---

## 四、API 客户端

### 4.1 接口定义

```typescript
// lib/openai 包封装
class DeepSeekClient {
  constructor(config: {
    apiKey: string;
    baseUrl?: string;   // 默认 https://api.deepseek.com
    model?: string;     // 默认 deepseek-v4-pro
  });

  async chat(messages: Message[], options?: ChatOptions): Promise<ChatResponse>;

  async countTokens(messages: Message[]): Promise<number>;
}

interface ChatOptions {
  tools?: ToolDefinition[];
  maxTokens?: number;
  signal?: AbortSignal;
  onReasoningChunk?: (text: string) => void;
  onContentChunk?:  (text: string) => void;
  onToolCall?:      (call: ToolCall) => void;
}

interface ChatResponse {
  message: {
    role: "assistant";
    content: string | null;
    reasoning_content: string | null;
    tool_calls: ToolCall[] | null;
  };
  usage: {
    promptTokens: number;
    completionTokens: number;
    cacheHitTokens: number;
    cacheMissTokens: number;
  };
  finishReason: "stop" | "tool_calls" | "length";
}
```

### 4.2 流式解析要点

- 使用 `openai` SDK 的 `stream: true`，配合 `stream_options: { include_usage: true }` 获取 usage
- `delta.reasoning_content` 是 DeepSeek 特有字段，需手动聚合
- `delta.tool_calls` 遵循 OpenAI 分片格式，按 index 聚合
- 最后一帧携带 `usage`，用于记录缓存命中情况

### 4.3 重试策略

| 参数 | 值 |
|------|-----|
| 最大重试 | 3 次 |
| 基础延迟 | 1000ms，指数退避 |
| 最大延迟 | 30000ms |
| 可重试状态码 | 429, 500, 502, 503 |
| 可重试错误码 | rate_limit, server_error, busy |
| 不可重试 | 400 (含 reasoning_content 缺失) |

---

## 五、上下文管理

### 5.1 核心策略：KV 缓存感知

DeepSeek 的 KV 硬盘缓存自动开启，缓存命中依赖**前缀匹配**。因此：

```
规则：永远只追加，不修改前面的消息

正确（可命中缓存）：              错误（缓存失效）：
┌──────────────────┐            ┌──────────────────┐
│ system (固定)     │ ← 缓存单元  │ system (动态修改)  │
│ user 消息1        │            │ user 消息1        │
│ assistant 回复1   │            │ assistant 回复1   │
│ user 消息2        │            │ user 消息2        │
│ assistant 回复2   │            │ assistant 回复2   │
│ ...              │  ← 追加    │ [摘要替换了历史]   │ ← 前缀变了
└──────────────────┘            └──────────────────┘
```

### 5.2 压缩策略

当 Token 用量超过 80% 时触发压缩：

1. 不删除早期消息（会破坏缓存前缀）
2. 在 system 消息之后、历史消息之前，**固定位置**插入摘要消息
3. 摘要消息 role 为 `user`，内容为历史对话的结构化总结
4. 后续请求仍可命中缓存（前缀不变）

### 5.3 reasoning_content 生命周期

- 有 `tool_calls` 的 assistant 消息：`reasoning_content` 必须在后续所有请求中回传
- 无 `tool_calls` 的 assistant 消息：`reasoning_content` 可丢弃
- **决策**：统一保留全部 `reasoning_content`，提高 KV 缓存命中率，放弃微小的 token 节省

---

## 六、工具系统

### 6.1 注册中心

```typescript
class ToolRegistry {
  private tools = new Map<string, RegisteredTool>();

  register(tool: RegisteredTool): void;
  getDefinitions(): ToolDefinition[];       // 传给 API
  execute(name: string, args: object): Promise<string>;
}

interface ToolDefinition {
  type: "function";
  function: {
    name: string;
    description: string;
    parameters: JSONSchema;
    strict?: boolean;
  };
}
```

### 6.2 MVP 工具列表

| 工具 | 功能 | 关键参数 | 确认 |
|------|------|----------|------|
| `read_file` | 读取文件 | `filePath`, `offset?`, `limit?` | - |
| `write_file` | 创建/覆盖文件 | `filePath`, `content` | - |
| `edit_file` | 精确字符串替换 | `filePath`, `oldString`, `newString` | - |
| `run_shell` | 执行 Shell 命令 | `command`, `workdir?`, `timeout?` | **需要用户确认** |
| `search_content` | 正则搜索 | `pattern`, `path?`, `include?` | - |
| `search_files` | Glob 匹配 | `pattern`, `path?` | - |

### 6.3 工具执行要求

- `run_shell`：执行前需用户确认，`workdir` 默认当前目录，超时默认 120s
- `edit_file`：返回 diff 预览，文件不存在时自动创建，替换失败时回滚
- `search_content`：结果截断（最多 500 行），超出时提示缩小范围
- `search_files`：返回匹配文件路径列表，按修改时间排序

---

## 七、CLI 界面

### 7.1 命令格式

```bash
dcode                              # 进入 REPL 交互模式
dcode "消息内容"                     # 单次问答
dcode --file bug.ts "分析这个 bug"   # 带文件上下文
dcode --resume                     # 恢复上次会话
dcode -v "消息"                    # 显示思考链
dcode --max-rounds 100 "复杂任务"   # 调整轮次上限
```

### 7.2 流式渲染分层

| 层级 | 内容 | 显示方式 |
|------|------|----------|
| `reasoning_content` | 思维链 | 暗灰色，默认折叠，`-v` 展开 |
| `content` | 正式回复 | 标准白色，逐字流式打印 |
| `tool_call` | 工具调用 | 蓝色高亮，工具名+参数摘要 |
| `tool_result` | 工具结果 | 缩进显示，长内容截断 |

### 7.3 技术选型

**纯 readline + chalk + ora**，不引入 Ink/React。MVP 不需要复杂 TUI，ANSI 控制足够。

---

## 八、配置管理

参考 Claude Code 模式：

- `DEEPSEEK_API_KEY` 环境变量（必需）
- `.dcode.json` 项目级配置（排除规则、轮次上限等）
- 首次运行无 API Key 时提示配置

默认配置：

```json
{
  "model": "deepseek-v4-pro",
  "maxRounds": 50,
  "softLimit": 50,
  "hardLimit": 200,
  "compressThreshold": 0.8,
  "maxTokens": 8192,
  "excludePatterns": ["node_modules", ".git", "dist", "build"]
}
```

---

## 九、数据流总览

```
用户输入
  │
  ▼
┌─────────────┐   buildMessages()   ┌──────────────┐
│  CLI 界面    │ ──────────────────▶ │ Agent 循环    │
│ readline     │                    │ loop.ts      │
└─────────────┘                    └──────┬───────┘
       ▲                                 │
       │ 流式回调                         │ chat()
       │ onReasoningChunk                 ▼
       │ onContentChunk          ┌──────────────┐
       │ onToolCall              │  API 客户端   │
       │                         │  client.ts   │
       │                         └──────┬───────┘
       │                                │ openai SDK
       │                                ▼
       │                         ┌──────────────┐
       │                         │ DeepSeek API │
       │                         │ /chat/       │
       │                         │ completions  │
       │                         └──────┬───────┘
       │                                │
       │  返回结果                      │ 响应
       │ ◄──────────────────────────────┘
       │
       │  最终回复 (content)
       │ ◄──────────────────────────────
```

---

## 十、实现顺序

| 阶段 | 模块 | 预估工作量 |
|------|------|-----------|
| 1 | 项目脚手架 + 配置加载 | 小 |
| 2 | API 客户端 + 流式解析 | 中 |
| 3 | Agent 循环 + 消息构建 | 中 |
| 4 | 工具系统（6 个工具） | 中 |
| 5 | CLI 界面 + 流式渲染 | 小 |
| 6 | 上下文压缩 + 会话持久化 | 小 |
| 7 | 系统提示词优化 | 持续 |

**建议按 1→2→3→4→5→6→7 顺序实现**，其中 3 和 4 是核心，需要最多调试时间。

---

## 附：DeepSeek API 参考要点

- **Base URL**：`https://api.deepseek.com`
- **模型**：`deepseek-v4-pro`（主力）、`deepseek-v4-flash`
- **思考模式**：`extra_body: { thinking: { type: "enabled" } }`，`reasoning_effort: "high"`
- **工具调用**：OpenAI 兼容格式，支持并行调用
- **strict 模式**：Beta 端点 `https://api.deepseek.com/beta`，需设置 `additionalProperties: false`
- **KV 缓存**：自动开启，通过 `usage.prompt_cache_hit_tokens` 查看命中情况
- **不支持的参数**（思考模式下）：`temperature`、`top_p`、`presence_penalty`、`frequency_penalty`
