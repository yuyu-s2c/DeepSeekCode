# dcode CLI 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建深度适配 DeepSeek V4 Pro 的编码 CLI 助手，支持文件读写、Shell 执行、代码搜索的 Agent 循环。

**Architecture:** 单进程 Node.js CLI，分层架构：CLI 界面 → Agent 循环 → API 客户端 → DeepSeek API。工具通过注册中心统一管理，上下文采用 KV 缓存感知的追加策略。

**Tech Stack:** TypeScript, Node.js 18+, openai (npm SDK), commander.js, chalk, ora

---

## 文件结构总览

```
dcode-cli/
├── package.json                   # 项目配置
├── tsconfig.json                  # TypeScript 配置
└── src/
    ├── index.ts                   # CLI 入口
    ├── config/
    │   ├── defaults.ts            # 默认配置常量
    │   └── loader.ts              # 配置加载器
    ├── api/
    │   ├── client.ts              # DeepSeek API 客户端
    │   ├── message-builder.ts     # 消息格式构建
    │   └── retry.ts               # 重试策略
    ├── agent/
    │   ├── types.ts               # 类型定义
    │   ├── system-prompt.ts       # 系统提示词
    │   └── loop.ts                # Agent 主循环
    ├── tools/
    │   ├── registry.ts            # 工具注册中心
    │   ├── read_file.ts           # 读取文件
    │   ├── write_file.ts          # 写入文件
    │   ├── edit_file.ts           # 精确编辑
    │   ├── run_shell.ts           # Shell 执行
    │   ├── search_content.ts      # 内容搜索
    │   ├── search_files.ts        # 文件名搜索
    │   └── index.ts               # 工具统一导出
    ├── context/
    │   ├── tokenizer.ts           # Token 计数
    │   ├── compressor.ts          # 上下文压缩
    │   └── manager.ts             # 上下文管理器
    └── cli/
        ├── output.ts              # 流式输出渲染
        └── interface.ts           # REPL 交互界面
```

---

### Task 1: 项目脚手架与配置管理

**Files:**
- Create: `package.json`
- Create: `tsconfig.json`
- Create: `src/config/defaults.ts`
- Create: `src/config/loader.ts`
- Create: `src/index.ts`（骨架）

- [ ] **Step 1: 创建 package.json**

```json
{
  "name": "dcode-cli",
  "version": "0.1.0",
  "description": "深度适配 DeepSeek V4 Pro 的编码 CLI 助手",
  "main": "dist/index.js",
  "bin": {
    "dcode": "./dist/index.js"
  },
  "scripts": {
    "build": "tsc",
    "dev": "tsc --watch",
    "start": "node dist/index.js"
  },
  "dependencies": {
    "openai": "^4.0.0",
    "commander": "^12.0.0",
    "chalk": "^5.3.0",
    "ora": "^8.0.0"
  },
  "devDependencies": {
    "@types/node": "^20.0.0",
    "typescript": "^5.4.0"
  }
}
```

- [ ] **Step 2: 安装依赖**

```bash
npm install
```
Expected: 无错误，`node_modules` 创建成功。

- [ ] **Step 3: 创建 tsconfig.json**

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "Node16",
    "moduleResolution": "Node16",
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "esModuleInterop": true,
    "forceConsistentCasingInFileNames": true,
    "skipLibCheck": true,
    "declaration": true,
    "declarationMap": true,
    "sourceMap": true
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules", "dist"]
}
```

- [ ] **Step 4: 编译验证**

```bash
npm run build
```
Expected: 编译成功（无 .ts 文件时会报"无输入文件"，正常）。

- [ ] **Step 5: 创建 `src/config/defaults.ts`**

```typescript
export const DEFAULT_CONFIG = {
  model: "deepseek-v4-pro",
  baseUrl: "https://api.deepseek.com",
  maxRounds: 50,
  softLimit: 50,
  hardLimit: 200,
  compressThreshold: 0.8,
  maxTokens: 8192,
  excludePatterns: ["node_modules", ".git", "dist", "build", ".next", "__pycache__"],
};
```

- [ ] **Step 6: 创建 `src/config/loader.ts`**

```typescript
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { DEFAULT_CONFIG } from "./defaults.js";

export interface AppConfig {
  model: string;
  baseUrl: string;
  maxRounds: number;
  softLimit: number;
  hardLimit: number;
  compressThreshold: number;
  maxTokens: number;
  excludePatterns: string[];
}

export function loadConfig(): AppConfig {
  const apiKey = process.env.DEEPSEEK_API_KEY;
  if (!apiKey) {
    console.error("错误：未设置 DEEPSEEK_API_KEY 环境变量");
    process.exit(1);
  }

  let projectConfig: Partial<AppConfig> = {};
  const configPath = resolve(process.cwd(), ".dcode.json");
  if (existsSync(configPath)) {
    try {
      projectConfig = JSON.parse(readFileSync(configPath, "utf-8"));
    } catch {
      console.warn(`警告：.dcode.json 解析失败，使用默认配置`);
    }
  }

  return { ...DEFAULT_CONFIG, ...projectConfig };
}
```

- [ ] **Step 7: 创建 `src/index.ts` 骨架**

```typescript
#!/usr/bin/env node
import { Command } from "commander";
import { loadConfig } from "./config/loader.js";

const program = new Command()
  .name("dcode")
  .description("深度适配 DeepSeek V4 Pro 的编码 CLI 助手")
  .version("0.1.0")
  .argument("[message]", "直接发送消息（省略则进入交互模式）")
  .option("-f, --file <path>", "附加文件到对话上下文")
  .option("-m, --model <name>", "模型名称")
  .option("-r, --max-rounds <n>", "最大对话轮次")
  .option("--resume", "恢复上次会话")
  .option("-v, --verbose", "显示思维链内容")
  .parse();

async function main() {
  const config = loadConfig();
  const options = program.opts();

  // 合并 CLI 参数到配置
  if (options.model) config.model = options.model;
  if (options.maxRounds) config.maxRounds = parseInt(options.maxRounds);

  const message = program.args[0];

  if (message) {
    // 单次执行模式（后续 Task 实现）
    console.log(`模型: ${config.model}`);
    console.log(`消息: ${message}`);
  } else {
    // 交互模式（后续 Task 实现）
    console.log(`dcode v0.1.0 — DeepSeek V4 Pro`);
    console.log(`输入 /help 查看帮助，/quit 退出`);
  }
}

main().catch((err) => {
  console.error("致命错误:", err.message);
  process.exit(1);
});
```

- [ ] **Step 8: 编译并验证骨架**

```bash
npm run build
```
Expected: 编译成功。

```bash
set DEEPSEEK_API_KEY=sk-test && node dist/index.js "你好"
```
Expected: 输出 `模型: deepseek-v4-pro` 和 `消息: 你好`。

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "chore: 项目脚手架与配置管理"
```

---

### Task 2: API 客户端

**Files:**
- Create: `src/api/retry.ts`
- Create: `src/api/message-builder.ts`
- Create: `src/api/client.ts`

- [ ] **Step 1: 创建 `src/api/retry.ts`**

```typescript
const RETRY_CONFIG = {
  maxRetries: 3,
  baseDelay: 1000,
  maxDelay: 30000,
  retryableStatuses: [429, 500, 502, 503],
  retryableErrors: ["rate_limit", "server_error", "busy"],
};

export function isRetryableError(error: unknown): boolean {
  if (error && typeof error === "object" && "status" in error) {
    const status = (error as { status: number }).status;
    if (RETRY_CONFIG.retryableStatuses.includes(status)) return true;
  }
  if (error && typeof error === "object" && "code" in error) {
    const code = (error as { code: string }).code;
    if (RETRY_CONFIG.retryableErrors.includes(code)) return true;
  }
  return false;
}

export function getRetryDelay(attempt: number): number {
  const delay = RETRY_CONFIG.baseDelay * Math.pow(2, attempt);
  return Math.min(delay, RETRY_CONFIG.maxDelay);
}

export async function withRetry<T>(
  fn: () => Promise<T>,
  attempt = 0
): Promise<T> {
  try {
    return await fn();
  } catch (error) {
    if (attempt >= RETRY_CONFIG.maxRetries || !isRetryableError(error)) {
      throw error;
    }
    const delay = getRetryDelay(attempt);
    await new Promise((resolve) => setTimeout(resolve, delay));
    return withRetry(fn, attempt + 1);
  }
}
```

- [ ] **Step 2: 创建 `src/agent/types.ts`**

```typescript
export interface ToolCall {
  id: string;
  type: "function";
  function: {
    name: string;
    arguments: string;
  };
}

export interface AssistantMessage {
  role: "assistant";
  content: string | null;
  reasoning_content: string | null;
  tool_calls: ToolCall[] | null;
}

export interface UserMessage {
  role: "user";
  content: string;
}

export interface SystemMessage {
  role: "system";
  content: string;
}

export interface ToolMessage {
  role: "tool";
  tool_call_id: string;
  content: string;
}

export type ConversationMessage =
  | SystemMessage
  | UserMessage
  | AssistantMessage
  | ToolMessage;

export interface ToolDefinition {
  type: "function";
  function: {
    name: string;
    description: string;
    parameters: Record<string, unknown>;
    strict?: boolean;
  };
}

export interface ChatResponse {
  message: AssistantMessage;
  usage: {
    promptTokens: number;
    completionTokens: number;
    cacheHitTokens: number;
    cacheMissTokens: number;
  };
  finishReason: "stop" | "tool_calls" | "length";
}
```

- [ ] **Step 3: 创建 `src/api/message-builder.ts`**

```typescript
import type { ConversationMessage } from "../agent/types.js";

interface OpenAIMessage {
  role: string;
  content?: string | null;
  reasoning_content?: string | null;
  tool_calls?: unknown;
  tool_call_id?: string;
}

export function toOpenAiMessages(
  messages: ConversationMessage[]
): OpenAIMessage[] {
  return messages.map((msg) => {
    switch (msg.role) {
      case "system":
        return { role: "system", content: msg.content };
      case "user":
        return { role: "user", content: msg.content };
      case "tool":
        return {
          role: "tool",
          tool_call_id: msg.tool_call_id,
          content: msg.content,
        };
      case "assistant": {
        const result: OpenAIMessage = {
          role: "assistant",
          content: msg.content,
        };
        // 始终携带 reasoning_content，提高 KV 缓存命中率
        if (msg.reasoning_content) {
          result.reasoning_content = msg.reasoning_content;
        }
        if (msg.tool_calls) {
          result.tool_calls = msg.tool_calls;
        }
        return result;
      }
    }
  });
}
```

- [ ] **Step 4: 创建 `src/api/client.ts`**

```typescript
import OpenAI from "openai";
import type { ToolCall, ChatResponse, AssistantMessage, ConversationMessage } from "../agent/types.js";
import type { ToolDefinition } from "../agent/types.js";
import { toOpenAiMessages } from "./message-builder.js";
import { withRetry } from "./retry.js";

export interface ClientConfig {
  apiKey: string;
  baseUrl: string;
  model: string;
  maxTokens: number;
}

export interface ChatOptions {
  tools?: ToolDefinition[];
  signal?: AbortSignal;
  onReasoningChunk?: (text: string) => void;
  onContentChunk?: (text: string) => void;
  onToolCall?: (call: ToolCall) => void;
}

export class DeepSeekClient {
  private client: OpenAI;
  private config: ClientConfig;

  constructor(config: ClientConfig) {
    this.config = config;
    this.client = new OpenAI({
      apiKey: config.apiKey,
      baseURL: config.baseUrl,
    });
  }

  async chat(
    messages: ConversationMessage[],
    options: ChatOptions = {}
  ): Promise<ChatResponse> {
    return withRetry(async () => {
      const openaiMessages = toOpenAiMessages(messages);

      const stream = await this.client.chat.completions.create({
        model: this.config.model,
        messages: openaiMessages as OpenAI.Chat.Completions.ChatCompletionMessageParam[],
        tools: options.tools?.length
          ? options.tools as OpenAI.Chat.Completions.ChatCompletionTool[]
          : undefined,
        stream: true,
        max_tokens: this.config.maxTokens,
        reasoning_effort: "high",
        extra_body: { thinking: { type: "enabled" } },
        stream_options: { include_usage: true },
      });

      let reasoningContent = "";
      let content = "";
      const toolCallMap = new Map<number, ToolCall>();

      for await (const chunk of stream) {
        if (options.signal?.aborted) {
          stream.controller.abort();
          throw new Error("用户中断");
        }

        const delta = chunk.choices?.[0]?.delta;

        // DeepSeek 特有：reasoning_content
        if (delta?.reasoning_content) {
          reasoningContent += delta.reasoning_content;
          options.onReasoningChunk?.(delta.reasoning_content);
        }

        // 普通内容
        if (delta?.content) {
          content += delta.content;
          options.onContentChunk?.(delta.content);
        }

        // 工具调用分片聚合
        if (delta?.tool_calls) {
          for (const tcDelta of delta.tool_calls) {
            const idx = tcDelta.index;
            if (!toolCallMap.has(idx)) {
              toolCallMap.set(idx, {
                id: "",
                type: "function",
                function: { name: "", arguments: "" },
              });
            }
            const tc = toolCallMap.get(idx)!;
            if (tcDelta.id) tc.id = tcDelta.id;
            if (tcDelta.function?.name) tc.function.name += tcDelta.function.name;
            if (tcDelta.function?.arguments) tc.function.arguments += tcDelta.function.arguments;
          }
        }
      }

      const toolCalls = toolCallMap.size > 0
        ? Array.from(toolCallMap.values())
        : null;

      // 通知工具调用
      if (toolCalls && options.onToolCall) {
        for (const tc of toolCalls) {
          options.onToolCall(tc);
        }
      }

      // 获取 usage（最后一帧）
      const streamCompletion = await stream.finalChatCompletion();
      const usage = streamCompletion.usage;

      const finishReason = (streamCompletion.choices[0]?.finish_reason || "stop") as ChatResponse["finishReason"];

      const message: AssistantMessage = {
        role: "assistant",
        content: content || null,
        reasoning_content: reasoningContent || null,
        tool_calls: toolCalls,
      };

      return {
        message,
        usage: {
          promptTokens: usage?.prompt_tokens ?? 0,
          completionTokens: usage?.completion_tokens ?? 0,
          cacheHitTokens: (usage as Record<string, unknown>)?.prompt_cache_hit_tokens as number ?? 0,
          cacheMissTokens: (usage as Record<string, unknown>)?.prompt_cache_miss_tokens as number ?? 0,
        },
        finishReason,
      };
    });
  }

  async countTokens(messages: ConversationMessage[]): Promise<number> {
    const openaiMessages = toOpenAiMessages(messages);
    let totalChars = 0;
    for (const msg of openaiMessages) {
      totalChars += JSON.stringify(msg).length;
    }
    // 粗略估算：中文约 1.5 字符/token，英文约 4 字符/token
    return Math.ceil(totalChars / 3);
  }
}
```

- [ ] **Step 5: 编译验证**

```bash
npm run build
```
Expected: 编译成功。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: 添加 DeepSeek API 客户端、重试策略与消息构建器"
```

---

### Task 3: Agent 循环

**Files:**
- Modify: `src/agent/types.ts`（已创建，需添加 AgentContext 类型）
- Create: `src/agent/system-prompt.ts`
- Create: `src/agent/loop.ts`

- [ ] **Step 1: 追加 `src/agent/types.ts` 的 AgentContext 类型**

在文件末尾追加：

```typescript
export interface AgentContext {
  systemPrompt: string;
  messages: ConversationMessage[];
  config: {
    model: string;
    maxRounds: number;
    softLimit: number;
    hardLimit: number;
    maxTokens: number;
  };
  toolDefinitions: ToolDefinition[];
}

export interface AgentInput {
  userMessage: string;
  attachedFiles?: { path: string; content: string }[];
}

export interface AgentResult {
  content: string;
  reasoningContent: string | null;
  totalRounds: number;
  usage: { promptTokens: number; completionTokens: number };
}
```

- [ ] **Step 2: 创建 `src/agent/system-prompt.ts`**

```typescript
export function buildSystemPrompt(): string {
  return `你是一个专业的软件工程师助手，运行在命令行环境中。你可以通过工具调用来读写文件、执行命令和搜索代码。

## 核心规则

1. **代码标识符**（变量名、函数名、类名等）必须使用规范的英文单词，禁止使用中文或拼音
2. **非代码内容**（注释、文档、提交信息）使用简体中文
3. 修改文件前先读取文件内容，确保了解当前状态
4. 编辑文件使用精确的字符串替换，不要重写整个文件
5. 执行 Shell 命令前考虑对系统的影响
6. 回复简洁直接，不需要冗长的解释
7. 如果无法完成任务，诚实说明原因并建议替代方案

## 可用工具

你可以通过函数调用来使用以下工具：
- read_file: 读取文件内容
- write_file: 创建或覆盖文件
- edit_file: 精确替换文件中的字符串
- run_shell: 执行 Shell 命令（危险操作会提示确认）
- search_content: 在文件中搜索匹配的内容
- search_files: 按文件名模式查找文件

## 工作流程

1. 理解用户的请求
2. 使用搜索工具了解代码库结构
3. 读取需要修改的文件
4. 使用 edit_file 进行精确修改
5. 必要时执行命令验证结果`;
}
```

- [ ] **Step 3: 创建 `src/agent/loop.ts`**

```typescript
import { DeepSeekClient } from "../api/client.js";
import type { ConversationMessage, AgentContext, AgentInput, AgentResult, AssistantMessage } from "./types.js";
import { buildSystemPrompt } from "./system-prompt.js";

export interface LoopConfig {
  client: DeepSeekClient;
  maxRounds: number;
  softLimit: number;
  hardLimit: number;
  maxTokens: number;
  toolDefinitions: ToolDefinition[];
  onRoundExceeded?: (round: number) => Promise<boolean>;
}

export async function runAgentLoop(
  input: AgentInput,
  config: LoopConfig
): Promise<AgentResult> {
  const systemPrompt = buildSystemPrompt();

  const messages: ConversationMessage[] = [
    { role: "system", content: systemPrompt },
  ];

  // 附加文件上下文
  if (input.attachedFiles?.length) {
    for (const file of input.attachedFiles) {
      messages.push({
        role: "user",
        content: `文件 ${file.path} 的内容：\n\`\`\`\n${file.content}\n\`\`\``,
      });
    }
  }

  // 用户消息
  messages.push({ role: "user", content: input.userMessage });

  let round = 0;
  const totalUsage = { promptTokens: 0, completionTokens: 0 };
  let lastContent = "";
  let lastReasoning = "";

  while (round < config.hardLimit) {
    const response = await config.client.chat(messages, {
      tools: config.toolDefinitions,
    });

    totalUsage.promptTokens += response.usage.promptTokens;
    totalUsage.completionTokens += response.usage.completionTokens;

    // 追加 assistant 消息
    messages.push(response.message);
    lastContent = response.message.content ?? "";
    lastReasoning = response.message.reasoning_content ?? "";

    if (!response.message.tool_calls?.length) {
      // 无工具调用，最终回复
      break;
    }

    // 执行工具调用
    for (const tc of response.message.tool_calls) {
      const result = await executeToolCall(tc);
      messages.push({
        role: "tool",
        tool_call_id: tc.id,
        content: result,
      });
    }

    round++;

    // 软上限检查
    if (round >= config.softLimit && config.onRoundExceeded) {
      const shouldContinue = await config.onRoundExceeded(round);
      if (!shouldContinue) break;
    }
  }

  if (round >= config.hardLimit) {
    lastContent = `已达到最大轮次 ${config.hardLimit}，任务未完成。`;
  }

  return {
    content: lastContent,
    reasoningContent: lastReasoning || null,
    totalRounds: round,
    usage: totalUsage,
  };
}

// 占位——Task 4 会替换为真实的工具注册中心调用
async function executeToolCall(tc: ToolCall): Promise<string> {
  return `工具 ${tc.function.name} 尚未注册，参数: ${tc.function.arguments}`;
}
```

注意：`src/agent/loop.ts` 顶部需添加 import：
```typescript
import type { ToolCall } from "./types.js";
```

- [ ] **Step 4: 编译验证**

```bash
npm run build
```
Expected: 编译成功。

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: 添加 Agent 循环、系统提示词与类型定义"
```

---

### Task 4: 工具系统

**Files:**
- Create: `src/tools/registry.ts`
- Create: `src/tools/read_file.ts`
- Create: `src/tools/write_file.ts`
- Create: `src/tools/edit_file.ts`
- Create: `src/tools/run_shell.ts`
- Create: `src/tools/search_content.ts`
- Create: `src/tools/search_files.ts`
- Create: `src/tools/index.ts`

- [ ] **Step 1: 创建 `src/tools/registry.ts`**

```typescript
import type { ToolDefinition, ToolCall } from "../agent/types.js";

export interface RegisteredTool {
  definition: ToolDefinition;
  execute: (args: Record<string, unknown>) => Promise<string>;
}

export class ToolRegistry {
  private tools = new Map<string, RegisteredTool>();

  register(tool: RegisteredTool): void {
    this.tools.set(tool.definition.function.name, tool);
  }

  getDefinitions(): ToolDefinition[] {
    return Array.from(this.tools.values()).map((t) => t.definition);
  }

  async execute(toolCall: ToolCall): Promise<string> {
    const tool = this.tools.get(toolCall.function.name);
    if (!tool) {
      return `错误：未知工具 ${toolCall.function.name}`;
    }
    try {
      const args = JSON.parse(toolCall.function.arguments);
      return await tool.execute(args);
    } catch (error) {
      return `错误：执行工具 ${toolCall.function.name} 失败 — ${error instanceof Error ? error.message : String(error)}`;
    }
  }
}
```

- [ ] **Step 2: 创建 `src/tools/read_file.ts`**

```typescript
import { readFileSync, existsSync } from "node:fs";
import type { RegisteredTool } from "./registry.js";

export const readFileTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "read_file",
      description: "读取指定文件的内容，支持指定行偏移和行数限制",
      parameters: {
        type: "object",
        properties: {
          filePath: { type: "string", description: "文件的绝对路径" },
          offset: { type: "integer", description: "起始行号（1-based），默认1" },
          limit: { type: "integer", description: "读取行数，默认2000" },
        },
        required: ["filePath"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const filePath = args.filePath as string;
    const offset = (args.offset as number) || 1;
    const limit = (args.limit as number) || 2000;

    if (!existsSync(filePath)) {
      return `错误：文件不存在 — ${filePath}`;
    }

    const content = readFileSync(filePath, "utf-8");
    const lines = content.split("\n");
    const start = offset - 1;
    const end = Math.min(start + limit, lines.length);
    const selected = lines.slice(start, end);

    const result = selected
      .map((line, i) => `${start + i + 1}: ${line}`)
      .join("\n");

    const header = `文件: ${filePath} (行 ${start + 1}-${end}，共 ${lines.length} 行)\n`;
    return header + result;
  },
};
```

- [ ] **Step 3: 创建 `src/tools/write_file.ts`**

```typescript
import { writeFileSync, existsSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";
import type { RegisteredTool } from "./registry.js";

export const writeFileTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "write_file",
      description: "创建新文件或覆盖已有文件的内容",
      parameters: {
        type: "object",
        properties: {
          filePath: { type: "string", description: "文件的绝对路径" },
          content: { type: "string", description: "要写入的文件内容" },
        },
        required: ["filePath", "content"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const filePath = args.filePath as string;
    const content = args.content as string;

    const dir = dirname(filePath);
    if (!existsSync(dir)) {
      mkdirSync(dir, { recursive: true });
    }

    const exists = existsSync(filePath);
    writeFileSync(filePath, content, "utf-8");

    return exists
      ? `文件已覆盖: ${filePath}`
      : `文件已创建: ${filePath}`;
  },
};
```

- [ ] **Step 4: 创建 `src/tools/edit_file.ts`**

```typescript
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import type { RegisteredTool } from "./registry.js";

export const editFileTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "edit_file",
      description:
        "精确替换文件中的指定字符串。oldString 必须在文件中唯一匹配。",
      parameters: {
        type: "object",
        properties: {
          filePath: { type: "string", description: "文件的绝对路径" },
          oldString: { type: "string", description: "要被替换的原始字符串" },
          newString: { type: "string", description: "替换后的新字符串" },
        },
        required: ["filePath", "oldString", "newString"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const filePath = args.filePath as string;
    const oldString = args.oldString as string;
    const newString = args.newString as string;

    if (!existsSync(filePath)) {
      return `错误：文件不存在 — ${filePath}`;
    }

    const original = readFileSync(filePath, "utf-8");
    const count = original.split(oldString).length - 1;

    if (count === 0) {
      return `错误：未找到匹配的字符串 — 文件中不存在指定的 oldString`;
    }
    if (count > 1) {
      return `错误：找到 ${count} 处匹配 — oldString 必须唯一，请提供更多上下文`;
    }

    // 备份原内容用于回滚
    const backup = original;
    try {
      const modified = original.replace(oldString, newString);
      writeFileSync(filePath, modified, "utf-8");

      // 生成简单 diff
      const oldLines = oldString.split("\n");
      const newLines = newString.split("\n");
      const diffLines: string[] = [];
      const maxLen = Math.max(oldLines.length, newLines.length);
      for (let i = 0; i < maxLen; i++) {
        if (i < oldLines.length && i < newLines.length) {
          if (oldLines[i] !== newLines[i]) {
            diffLines.push(`- ${oldLines[i]}`);
            diffLines.push(`+ ${newLines[i]}`);
          }
        } else if (i < oldLines.length) {
          diffLines.push(`- ${oldLines[i]}`);
        } else {
          diffLines.push(`+ ${newLines[i]}`);
        }
      }

      return `编辑成功: ${filePath}\n\`\`\`diff\n${diffLines.join("\n")}\n\`\`\``;
    } catch (error) {
      // 回滚
      writeFileSync(filePath, backup, "utf-8");
      return `错误：编辑失败，已回滚 — ${error instanceof Error ? error.message : String(error)}`;
    }
  },
};
```

- [ ] **Step 5: 创建 `src/tools/run_shell.ts`**

```typescript
import { execSync } from "node:child_process";
import type { RegisteredTool } from "./registry.js";

// 全局标志，用于交互式确认（后续 Task 5 连接 CLI 界面）
let approvalCallback: ((command: string) => Promise<boolean>) | null = null;

export function setApprovalCallback(cb: (command: string) => Promise<boolean>): void {
  approvalCallback = cb;
}

export const runShellTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "run_shell",
      description: "在终端中执行 Shell 命令。危险操作需要用户确认。",
      parameters: {
        type: "object",
        properties: {
          command: { type: "string", description: "要执行的 Shell 命令" },
          workdir: {
            type: "string",
            description: "工作目录，默认为当前目录",
          },
          timeout: {
            type: "integer",
            description: "超时时间（毫秒），默认120000",
          },
        },
        required: ["command"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const command = args.command as string;
    const workdir = (args.workdir as string) || process.cwd();
    const timeout = (args.timeout as number) || 120000;

    // 需要用户确认
    if (approvalCallback) {
      const approved = await approvalCallback(command);
      if (!approved) {
        return "用户取消了命令执行";
      }
    }

    try {
      const output = execSync(command, {
        cwd: workdir,
        timeout,
        encoding: "utf-8",
        maxBuffer: 10 * 1024 * 1024,
      });
      return output || "(命令执行成功，无输出)";
    } catch (error: unknown) {
      if (error && typeof error === "object" && "stdout" in error) {
        const execError = error as { stdout: string; stderr: string; message: string };
        return `命令执行失败:\nSTDOUT:\n${execError.stdout || "(无)"}\nSTDERR:\n${execError.stderr || "(无)"}\n${execError.message}`;
      }
      return `命令执行失败: ${error instanceof Error ? error.message : String(error)}`;
    }
  },
};
```

- [ ] **Step 6: 创建 `src/tools/search_content.ts`**

```typescript
import { readFileSync, existsSync } from "node:fs";
import { resolve, relative, extname } from "node:path";
import { globSync } from "node:fs";
import type { RegisteredTool } from "./registry.js";

const MAX_RESULTS = 500;

export const searchContentTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "search_content",
      description: "在文件中搜索匹配正则表达式的内容，返回文件路径和行号",
      parameters: {
        type: "object",
        properties: {
          pattern: { type: "string", description: "正则表达式搜索模式" },
          path: {
            type: "string",
            description: "搜索目录路径，默认为当前目录",
          },
          include: {
            type: "string",
            description: "文件类型过滤，如 *.ts, *.{js,ts}",
          },
        },
        required: ["pattern"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const pattern = args.pattern as string;
    const searchDir = (args.path as string) || process.cwd();
    const include = (args.include as string) || "*";

    let regex: RegExp;
    try {
      regex = new RegExp(pattern, "g");
    } catch {
      return `错误：无效的正则表达式 — ${pattern}`;
    }

    const absDir = resolve(searchDir);
    const excludeDirs = ["node_modules", ".git", "dist", "build", ".next", "__pycache__"];

    // 简单的递归文件搜索（避免引入额外依赖）
    const results: string[] = [];
    let count = 0;

    function walkDir(dir: string) {
      if (count >= MAX_RESULTS) return;

      const entries = readdirSyncSafe(dir);
      if (!entries) return;

      for (const entry of entries) {
        if (count >= MAX_RESULTS) return;
        const fullPath = resolve(dir, entry);

        if (excludeDirs.includes(entry)) continue;

        try {
          const stat = statSyncSafe(fullPath);
          if (stat?.isDirectory()) {
            walkDir(fullPath);
          } else if (stat?.isFile()) {
            if (!matchGlob(entry, include)) continue;
            try {
              const content = readFileSync(fullPath, "utf-8");
              const lines = content.split("\n");
              for (let i = 0; i < lines.length; i++) {
                if ((regex as RegExp).test(lines[i])) {
                  regex.lastIndex = 0;
                  const relPath = relative(process.cwd(), fullPath);
                  results.push(`${relPath}:${i + 1}: ${lines[i].trim().substring(0, 200)}`);
                  count++;
                  if (count >= MAX_RESULTS) break;
                }
              }
            } catch {
              // 跳过无法读取的文件
            }
          }
        } catch {
          // 跳过无法访问的文件
        }
      }
    }

    function readdirSyncSafe(dir: string): string[] | null {
      try {
        return require("node:fs").readdirSync(dir);
      } catch {
        return null;
      }
    }

    function statSyncSafe(path: string): { isDirectory(): boolean; isFile(): boolean } | null {
      try {
        return require("node:fs").statSync(path);
      } catch {
        return null;
      }
    }

    function matchGlob(filename: string, pattern: string): boolean {
      if (pattern === "*") return true;
      const regexStr = pattern
        .replace(/\./g, "\\.")
        .replace(/\*/g, ".*")
        .replace(/\{([^}]+)\}/g, (_, g) => `(${g.split(",").join("|")})`);
      return new RegExp(`^${regexStr}$`).test(filename);
    }

    walkDir(absDir);

    const header = `搜索 "${pattern}" 的结果 (${count} 条，上限 ${MAX_RESULTS}):\n`;
    return header + (results.length > 0 ? results.join("\n") : "(无结果)");
  },
};
```

- [ ] **Step 7: 创建 `src/tools/search_files.ts`**

```typescript
import { readdirSync, statSync } from "node:fs";
import { resolve, relative } from "node:path";
import type { RegisteredTool } from "./registry.js";

export const searchFilesTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "search_files",
      description: "按文件名 Glob 模式匹配文件，返回匹配的文件路径列表",
      parameters: {
        type: "object",
        properties: {
          pattern: { type: "string", description: "Glob 模式，如 **/*.ts, src/**/*.tsx" },
          path: { type: "string", description: "搜索起始目录，默认为当前目录" },
        },
        required: ["pattern"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const pattern = args.pattern as string;
    const startDir = (args.path as string) || process.cwd();

    const results: string[] = [];
    const excludeDirs = new Set(["node_modules", ".git", "dist", "build", ".next", "__pycache__"]);

    function globToRegex(glob: string): RegExp {
      const parts = glob.split("/");
      const regexParts = parts.map((part) => {
        if (part === "**") return ".*";
        return part
          .replace(/\./g, "\\.")
          .replace(/\*/g, "[^/]*")
          .replace(/\?/g, "[^/]")
          .replace(/\{([^}]+)\}/g, (_, g) => `(${g.split(",").join("|")})`);
      });
      const regexStr = regexParts.join("/");
      return new RegExp(`^${regexStr}$`);
    }

    let regex: RegExp;
    try {
      regex = globToRegex(pattern);
    } catch {
      return `错误：无效的 Glob 模式 — ${pattern}`;
    }

    function walk(dir: string, basePath: string) {
      let entries: string[];
      try {
        entries = readdirSync(dir);
      } catch {
        return;
      }

      for (const entry of entries) {
        if (excludeDirs.has(entry)) continue;

        const fullPath = resolve(dir, entry);
        let stat;
        try {
          stat = statSync(fullPath);
        } catch {
          continue;
        }

        if (stat.isDirectory()) {
          if (pattern.includes("**")) {
            walk(fullPath, basePath);
          }
        } else if (stat.isFile()) {
          const relPath = relative(basePath, fullPath);
          if (regex.test(relPath) || regex.test(entry)) {
            results.push(relPath);
          }
          if (results.length >= 500) return;
        }
      }
    }

    walk(startDir, startDir);

    const header = `匹配 "${pattern}" 的文件 (${results.length} 条):\n`;
    return header + (results.length > 0 ? results.join("\n") : "(无结果)");
  },
};
```

- [ ] **Step 8: 创建 `src/tools/index.ts`**

```typescript
export { ToolRegistry } from "./registry.js";
export { readFileTool } from "./read_file.js";
export { writeFileTool } from "./write_file.js";
export { editFileTool } from "./edit_file.js";
export { runShellTool, setApprovalCallback } from "./run_shell.js";
export { searchContentTool } from "./search_content.js";
export { searchFilesTool } from "./search_files.js";
```

- [ ] **Step 9: 更新 `src/agent/loop.ts`，集成 ToolRegistry**

修改 `runAgentLoop` 函数签名和内部调用：

将：
```typescript
export interface LoopConfig {
  client: DeepSeekClient;
  maxRounds: number;
  softLimit: number;
  hardLimit: number;
  maxTokens: number;
  toolDefinitions: ToolDefinition[];
  onRoundExceeded?: (round: number) => Promise<boolean>;
}
```

改为：
```typescript
import { ToolRegistry } from "../tools/index.js";

export interface LoopConfig {
  client: DeepSeekClient;
  maxRounds: number;
  softLimit: number;
  hardLimit: number;
  maxTokens: number;
  toolRegistry: ToolRegistry;
  onRoundExceeded?: (round: number) => Promise<boolean>;
}
```

将循环内的：
```typescript
const response = await config.client.chat(messages, {
  tools: config.toolDefinitions,
});
```

改为：
```typescript
const response = await config.client.chat(messages, {
  tools: config.toolRegistry.getDefinitions(),
});
```

将：
```typescript
for (const tc of response.message.tool_calls) {
  const result = await executeToolCall(tc);
  messages.push({
    role: "tool",
    tool_call_id: tc.id,
    content: result,
  });
}
```

改为：
```typescript
for (const tc of response.message.tool_calls) {
  const result = await config.toolRegistry.execute(tc);
  messages.push({
    role: "tool",
    tool_call_id: tc.id,
    content: result,
  });
}
```

删除 `executeToolCall` 函数。

- [ ] **Step 10: 编译验证**

```bash
npm run build
```
Expected: 编译成功。

- [ ] **Step 11: Commit**

```bash
git add -A && git commit -m "feat: 添加工具系统（文件读写编辑、Shell执行、代码搜索）"
```

---

### Task 5: CLI 界面与流式渲染

**Files:**
- Create: `src/cli/output.ts`
- Create: `src/cli/interface.ts`
- Modify: `src/index.ts`（集成所有模块）

- [ ] **Step 1: 创建 `src/cli/output.ts`**

```typescript
import chalk from "chalk";

let verboseMode = false;

export function setVerbose(v: boolean): void {
  verboseMode = v;
}

export function renderReasoning(text: string): void {
  if (verboseMode) {
    process.stdout.write(chalk.gray(text));
  }
}

export function renderContent(text: string): void {
  process.stdout.write(text);
}

export function renderToolCall(name: string, args: string): void {
  const shortArgs = args.length > 80 ? args.substring(0, 80) + "..." : args;
  process.stdout.write(chalk.blue(`\n  → ${name}(${shortArgs})`));
}

export function renderToolResult(success: boolean, summary: string): void {
  const marker = success ? chalk.green("[✓]") : chalk.red("[✗]");
  process.stdout.write(` ${marker}\n`);
  if (summary) {
    process.stdout.write(chalk.gray(`    ${summary.substring(0, 200)}\n`));
  }
}

export function renderThinkingStart(): void {
  if (verboseMode) {
    process.stdout.write(chalk.gray("── 思考中... ──\n"));
  }
}

export function renderThinkingEnd(): void {
  if (verboseMode) {
    process.stdout.write("\n");
  }
}

export function renderSeparator(): void {
  process.stdout.write("\n" + chalk.gray("─".repeat(60)) + "\n");
}

export function renderWelcome(model: string): void {
  console.log(chalk.bold(`dcode v0.1.0 — DeepSeek V4 Pro`));
  console.log(chalk.gray(`模型: ${model}`));
  console.log(chalk.gray(`输入 /help 查看帮助，/quit 退出\n`));
}

export function renderHelp(): void {
  console.log(`
${chalk.bold("命令:")}
  /help      显示此帮助
  /quit      退出程序
  /clear     清除对话历史
  /verbose   切换思维链显示
  /resume    恢复上次会话

${chalk.bold("快捷键:")}
  Ctrl+C     中断当前操作
  Ctrl+D     退出程序
`);
}
```

- [ ] **Step 2: 创建 `src/cli/interface.ts`**

```typescript
import * as readline from "node:readline";
import chalk from "chalk";
import { loadConfig } from "../config/loader.js";
import { DeepSeekClient } from "../api/client.js";
import { runAgentLoop } from "../agent/loop.js";
import {
  ToolRegistry,
  readFileTool,
  writeFileTool,
  editFileTool,
  runShellTool,
  setApprovalCallback,
  searchContentTool,
  searchFilesTool,
} from "../tools/index.js";
import {
  setVerbose,
  renderReasoning,
  renderContent,
  renderToolCall,
  renderToolResult,
  renderThinkingStart,
  renderThinkingEnd,
  renderSeparator,
  renderWelcome,
  renderHelp,
} from "./output.js";

export async function startRepl(): Promise<void> {
  const config = loadConfig();

  // 创建工具注册中心
  const registry = new ToolRegistry();
  registry.register(readFileTool);
  registry.register(writeFileTool);
  registry.register(editFileTool);
  registry.register(runShellTool);
  registry.register(searchContentTool);
  registry.register(searchFilesTool);

  // 创建 API 客户端
  const client = new DeepSeekClient({
    apiKey: process.env.DEEPSEEK_API_KEY!,
    baseUrl: config.baseUrl,
    model: config.model,
    maxTokens: config.maxTokens,
  });

  let verbose = false;
  setVerbose(verbose);

  // Shell 确认回调
  setApprovalCallback(async (command: string) => {
    return new Promise((resolve) => {
      console.log(chalk.yellow(`\n⚠ 即将执行命令: ${command}`));
      console.log(chalk.gray("确认执行？[y/N] "));
      // 简化处理：默认询问用户
      const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout,
      });
      rl.question("", (answer) => {
        rl.close();
        resolve(answer.toLowerCase() === "y" || answer.toLowerCase() === "yes");
      });
    });
  });

  renderWelcome(config.model);

  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
    prompt: chalk.cyan("> "),
    terminal: true,
  });

  // 对话历史（跨轮次保持）
  let conversationHistory: ConversationMessage[] = [];

  // 处理用户输入
  const processInput = async (input: string) => {
    const trimmed = input.trim();
    if (!trimmed) return;

    // 处理内置命令
    if (trimmed.startsWith("/")) {
      switch (trimmed) {
        case "/help":
          renderHelp();
          return;
        case "/quit":
          console.log(chalk.gray("再见！"));
          process.exit(0);
        case "/clear":
          conversationHistory = [];
          console.log(chalk.gray("对话历史已清除"));
          return;
        case "/verbose":
          verbose = !verbose;
          setVerbose(verbose);
          console.log(chalk.gray(`思维链显示: ${verbose ? "开启" : "关闭"}`));
          return;
        default:
          console.log(chalk.gray(`未知命令: ${trimmed}，输入 /help 查看帮助`));
          return;
      }
    }

    // 正常消息处理
    renderThinkingStart();

    const abortController = new AbortController();

    try {
      const result = await runAgentLoop(
        { userMessage: trimmed },
        {
          client,
          maxRounds: config.maxRounds,
          softLimit: config.softLimit,
          hardLimit: config.hardLimit,
          maxTokens: config.maxTokens,
          toolRegistry: registry,
          onRoundExceeded: async (round) => {
            return new Promise((resolve) => {
              const askRl = readline.createInterface({
                input: process.stdin,
                output: process.stdout,
              });
              askRl.question(
                chalk.yellow(`\n已执行 ${round} 轮，继续？[Y/n] `),
                (answer) => {
                  askRl.close();
                  resolve(answer.toLowerCase() !== "n");
                }
              );
            });
          },
        }
      );

      renderThinkingEnd();
      renderSeparator();
      console.log(result.content);
      console.log();

      if (verbose) {
        console.log(
          chalk.gray(
            `[${result.totalRounds} 轮 | 输入 ${result.usage.promptTokens} tokens | 输出 ${result.usage.completionTokens} tokens]`
          )
        );
      }
    } catch (error) {
      renderThinkingEnd();
      console.error(
        chalk.red(
          `错误: ${error instanceof Error ? error.message : String(error)}`
        )
      );
    }
  };

  rl.on("line", async (line) => {
    rl.pause();
    await processInput(line);
    rl.prompt();
    rl.resume();
  });

  rl.on("SIGINT", () => {
    console.log(chalk.gray("\n使用 Ctrl+D 或 /quit 退出"));
    rl.prompt();
  });

  rl.prompt();
}

export async function runSingleMessage(message: string): Promise<void> {
  const config = loadConfig();

  const registry = new ToolRegistry();
  registry.register(readFileTool);
  registry.register(writeFileTool);
  registry.register(editFileTool);
  registry.register(runShellTool);
  registry.register(searchContentTool);
  registry.register(searchFilesTool);

  const client = new DeepSeekClient({
    apiKey: process.env.DEEPSEEK_API_KEY!,
    baseUrl: config.baseUrl,
    model: config.model,
    maxTokens: config.maxTokens,
  });

  setApprovalCallback(async () => true); // 单次模式默认批准

  console.log(chalk.gray(`dcode > ${message}\n`));

  const result = await runAgentLoop(
    { userMessage: message },
    {
      client,
      maxRounds: config.maxRounds,
      softLimit: config.softLimit,
      hardLimit: config.hardLimit,
      maxTokens: config.maxTokens,
      toolRegistry: registry,
    }
  );

  console.log(result.content);
}
```

- [ ] **Step 3: 更新 `src/index.ts`，集成所有模块**

替换 `src/index.ts` 内容为：

```typescript
#!/usr/bin/env node
import { Command } from "commander";
import chalk from "chalk";
import { loadConfig } from "./config/loader.js";
import { startRepl, runSingleMessage } from "./cli/interface.js";

const program = new Command()
  .name("dcode")
  .description("深度适配 DeepSeek V4 Pro 的编码 CLI 助手")
  .version("0.1.0")
  .argument("[message]", "直接发送消息（省略则进入交互模式）")
  .option("-f, --file <path>", "附加文件到对话上下文")
  .option("-m, --model <name>", "模型名称")
  .option("-r, --max-rounds <n>", "最大对话轮次")
  .option("--resume", "恢复上次会话")
  .option("-v, --verbose", "显示思维链内容")
  .parse();

async function main() {
  // 提前验证 API Key
  if (!process.env.DEEPSEEK_API_KEY) {
    console.error(
      chalk.red("错误：未设置 DEEPSEEK_API_KEY 环境变量")
    );
    console.error(chalk.gray("请在环境变量中设置: set DEEPSEEK_API_KEY=sk-xxx"));
    process.exit(1);
  }

  const message = program.args[0];

  if (message) {
    await runSingleMessage(message);
  } else {
    await startRepl();
  }
}

main().catch((err) => {
  console.error(chalk.red(`致命错误: ${err.message}`));
  process.exit(1);
});
```

- [ ] **Step 4: 编译验证**

```bash
npm run build
```
Expected: 编译成功。

- [ ] **Step 5: 冒烟测试（需要有效 API Key）**

```bash
set DEEPSEEK_API_KEY=sk-test && node dist/index.js "1+1等于几？"
```
Expected: 看到流式输出回复。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: 添加 CLI 交互界面与流式渲染"
```

---

### Task 6: 上下文压缩与会话持久化

**Files:**
- Create: `src/context/tokenizer.ts`
- Create: `src/context/compressor.ts`
- Create: `src/context/manager.ts`

- [ ] **Step 1: 创建 `src/context/tokenizer.ts`**

```typescript
import type { ConversationMessage } from "../agent/types.js";

export function estimateTokens(messages: ConversationMessage[]): number {
  let totalChars = 0;
  for (const msg of messages) {
    if (msg.role === "system") {
      totalChars += msg.content.length;
    } else if (msg.role === "user") {
      totalChars += msg.content.length;
    } else if (msg.role === "tool") {
      totalChars += msg.content.length;
    } else if (msg.role === "assistant") {
      totalChars += (msg.content ?? "").length;
      totalChars += (msg.reasoning_content ?? "").length;
      if (msg.tool_calls) {
        totalChars += JSON.stringify(msg.tool_calls).length;
      }
    }
  }
  // 粗略估算：混合文本约 3 字符/token
  return Math.ceil(totalChars / 3);
}

// DeepSeek V4 Pro 上下文窗口约 128K tokens
export const MAX_CONTEXT_TOKENS = 120_000;
```

- [ ] **Step 2: 创建 `src/context/compressor.ts`**

```typescript
import type { ConversationMessage } from "../agent/types.js";

export interface CompressionResult {
  summaryMessage: ConversationMessage;
  compressedCount: number;
}

/**
 * 压缩对话历史——生成摘要消息追加到 system 后面。
 * 不删除原始消息，保持 KV 缓存前缀完整性。
 */
export function compressHistory(
  messages: ConversationMessage[],
  keepRecent: number = 5
): CompressionResult {
  // system message 永远保留
  const systemIndex = messages.findIndex((m) => m.role === "system");
  if (systemIndex === -1) {
    return {
      summaryMessage: {
        role: "user",
        content: "(无历史)",
      },
      compressedCount: 0,
    };
  }

  const systemMsg = messages[systemIndex];

  // 收集需要压缩的消息（system 和最近 N 条保留）
  const endIndex = messages.length - keepRecent;
  if (endIndex <= systemIndex + 1) {
    return {
      summaryMessage: {
        role: "user",
        content: "(对话历史较短，无需压缩)",
      },
      compressedCount: 0,
    };
  }

  const toCompress = messages.slice(systemIndex + 1, endIndex);
  const summaryItems: string[] = [];

  for (const msg of toCompress) {
    switch (msg.role) {
      case "user":
        summaryItems.push(`用户: ${msg.content.substring(0, 100)}`);
        break;
      case "assistant": {
        const text = msg.content ?? msg.reasoning_content ?? "";
        if (text) {
          summaryItems.push(`助手: ${text.substring(0, 200)}`);
        }
        if (msg.tool_calls) {
          const toolNames = msg.tool_calls.map((tc) => tc.function.name).join(", ");
          summaryItems.push(`  → 调用工具: ${toolNames}`);
        }
        break;
      }
      case "tool":
        summaryItems.push(`  → 工具结果: ${msg.content.substring(0, 100)}`);
        break;
    }
  }

  const summary = [
    `以下是与 DeepSeek 的历史对话摘要（共 ${toCompress.length} 条消息）：`,
    ...summaryItems,
    "--- 以上为历史摘要，以下为最近对话 ---",
  ].join("\n");

  return {
    summaryMessage: {
      role: "user",
      content: summary,
    },
    compressedCount: toCompress.length,
  };
}
```

- [ ] **Step 3: 创建 `src/context/manager.ts`**

```typescript
import type { ConversationMessage } from "../agent/types.js";
import { estimateTokens, MAX_CONTEXT_TOKENS } from "./tokenizer.js";
import { compressHistory } from "./compressor.js";

export class ContextManager {
  private messages: ConversationMessage[];
  private compressThreshold: number;

  constructor(messages: ConversationMessage[], compressThreshold = 0.8) {
    this.messages = messages;
    this.compressThreshold = compressThreshold;
  }

  addMessage(msg: ConversationMessage): void {
    this.messages.push(msg);
  }

  getMessages(): ConversationMessage[] {
    const tokens = estimateTokens(this.messages);
    if (tokens < MAX_CONTEXT_TOKENS * this.compressThreshold) {
      return this.messages;
    }

    // 需要压缩：在 system 后面插入摘要
    const result = compressHistory(this.messages);

    if (result.compressedCount > 0) {
      // 在 system 消息后面插入摘要
      const systemIndex = this.messages.findIndex((m) => m.role === "system");
      const newMessages = [...this.messages];
      newMessages.splice(systemIndex + 1, 0, result.summaryMessage);
      return newMessages;
    }

    return this.messages;
  }

  reset(): void {
    this.messages = [];
  }

  toJSON(): string {
    return JSON.stringify(this.messages);
  }

  static fromJSON(json: string): ContextManager {
    const messages = JSON.parse(json) as ConversationMessage[];
    return new ContextManager(messages);
  }
}
```

- [ ] **Step 4: 在 `src/cli/interface.ts` 中集成 ContextManager**

在 `startRepl` 函数中，将：
```typescript
let conversationHistory: ConversationMessage[] = [];
```

改为：
```typescript
import { ContextManager } from "../context/manager.js";
import type { ConversationMessage } from "../agent/types.js";

const contextManager = new ContextManager([]);
```

接下来修改 `src/agent/loop.ts`，让 `runAgentLoop` 接受初始 messages：

将 `src/agent/loop.ts` 的 `LoopConfig` 接口改为：

```typescript
import { ToolRegistry } from "../tools/index.js";

export interface LoopConfig {
  client: DeepSeekClient;
  maxRounds: number;
  softLimit: number;
  hardLimit: number;
  maxTokens: number;
  toolRegistry: ToolRegistry;
  initialMessages?: ConversationMessage[];
  onRoundExceeded?: (round: number) => Promise<boolean>;
}
```

将 `runAgentLoop` 函数的 messages 初始化逻辑改为：

```typescript
export async function runAgentLoop(
  input: AgentInput,
  config: LoopConfig
): Promise<AgentResult> {
  const systemPrompt = buildSystemPrompt();

  let messages: ConversationMessage[];
  if (config.initialMessages?.length) {
    messages = [...config.initialMessages];
  } else {
    messages = [{ role: "system", content: systemPrompt }];
  }

  // 附加文件上下文
  if (input.attachedFiles?.length) {
    for (const file of input.attachedFiles) {
      messages.push({
        role: "user",
        content: `文件 ${file.path} 的内容：\n\`\`\`\n${file.content}\n\`\`\``,
      });
    }
  }

  // 用户消息
  messages.push({ role: "user", content: input.userMessage });

  // ... 后续循环代码与之前相同
}
```

然后在 `src/cli/interface.ts` 的 `processInput` 函数中，调用 `runAgentLoop` 时传入初始 messages：

```typescript
const result = await runAgentLoop(
  { userMessage: trimmed },
  {
    client,
    maxRounds: config.maxRounds,
    softLimit: config.softLimit,
    hardLimit: config.hardLimit,
    maxTokens: config.maxTokens,
    toolRegistry: registry,
    initialMessages: contextManager.getMessages(),
    onRoundExceeded: async (round) => {
      // ... 与之前相同的确认逻辑
    },
  }
);

// 本轮结束后，将新消息追加到 contextManager
contextManager.addMessage({ role: "user", content: trimmed });
contextManager.addMessage({
  role: "assistant",
  content: result.content,
  reasoning_content: result.reasoningContent,
  tool_calls: null,
});
```

- [ ] **Step 5: 编译验证**

```bash
npm run build
```
Expected: 编译成功。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: 添加上下文压缩器与会话管理"
```

---

### Task 7: 系统提示词优化（持续迭代）

**Files:**
- Modify: `src/agent/system-prompt.ts`

- [ ] **Step 1: 初步增强系统提示词**

替换 `src/agent/system-prompt.ts` 内容为更完善的版本：

```typescript
export function buildSystemPrompt(): string {
  return `你是一个专业的软件工程师助手（dcode），运行在命令行环境中。你可以通过工具调用来读写文件、执行命令和搜索代码。

## 身份

- 你是 dcode，一个与 DeepSeek V4 Pro 深度适配的编码 CLI 工具
- 运行在终端环境，用户的请求和你的回复都在命令行中显示
- 你可以直接操作用户的文件系统和 Shell

## 核心规则

1. **代码标识符**（变量名、函数名、类名、方法名等）必须使用规范的英文单词，禁止使用中文或拼音
2. **非代码内容**（注释、UI 文字、文档、提交信息等）使用简体中文
3. 修改文件前必须先读取文件内容，确保了解当前状态
4. 使用 edit_file 工具进行精确的字符串替换，不要用 write_file 重写整个文件
5. 执行 Shell 命令时要评估对系统的影响，危险操作让用户确认
6. **回复简洁直接**，不需要冗长的解释和总结，除非用户明确要求
7. 如果无法完成任务，诚实说明原因并建议替代方案
8. 不要猜测或编造不存在的 API、库或文件路径
9. 不要使用 emoji，除非用户明确要求
10. 不要在代码中添加注释，除非用户明确要求

## 编辑文件注意事项

- 使用 edit_file 时，oldString 必须在文件中唯一匹配
- 如果匹配到多处，需要提供更多上下文使匹配唯一
- 编辑失败会自动回滚，重试前检查 oldString 是否准确

## Shell 命令注意事项

- 在 Windows 上使用 PowerShell (pwsh) 命令
- 提供清晰的命令描述和预期结果
- 避免使用会修改系统配置的危险命令

## 可用工具

- read_file: 读取文件内容（支持行偏移和行数限制）
- write_file: 创建或覆盖文件
- edit_file: 精确替换文件中的字符串（oldString → newString）
- run_shell: 执行 Shell 命令（危险操作需确认）
- search_content: 在文件中搜索匹配正则表达式的内容
- search_files: 按文件名 Glob 模式查找文件

## 工具使用策略

1. 先搜索（search_files / search_content）了解代码结构
2. 再读取（read_file）需要修改的文件
3. 使用 edit_file 进行精确修改
4. 必要时 run_shell 验证结果

## 禁止的行为

- 不要在不知道文件内容的情况下编辑文件
- 不要假设文件存在或目录结构
- 不要执行用户未明确要求的 Shell 命令
- 不要暴露或记录 API 密钥、密码等敏感信息`;
}
```

- [ ] **Step 2: 编译验证**

```bash
npm run build
```
Expected: 编译成功。

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: 增强系统提示词，完善行为规范与工具使用策略"
```

---

## 验证计划

完成所有 Task 后，执行以下验证：

```bash
# 1. 编译
npm run build

# 2. 基础功能（需要有效 API Key）
set DEEPSEEK_API_KEY=sk-xxx
node dist/index.js "你好，请用一句话介绍自己"

# 3. 文件工具
node dist/index.js "读取 package.json 的内容"

# 4. 搜索工具
node dist/index.js "在 src 目录下搜索所有 .ts 文件"

# 5. Shell 工具（会确认）
node dist/index.js "列出当前目录的文件"

# 6. REPL 模式
node dist/index.js
> /help
> /quit

# 7. 编辑测试
echo "hello world" > test.txt
node dist/index.js "把 test.txt 中的 hello 改成 hi"
type test.txt
del test.txt
```
