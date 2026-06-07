# AGENTS.md

面向 AI Agent 的 dcode 项目开发指南。

## 环境

- **平台**: Windows (pwsh), 也兼容 Linux/macOS
- **Node.js**: 18+
- **TypeScript**: 5.4+, strict mode
- **Shell 命令**: PowerShell `pwsh` 优先，`cmd` 作为回退
- **编码**: 所有文件 UTF-8

## 构建与运行

```bash
npm install          # 安装依赖
npm run build        # tsc 编译到 dist/
npm run dev          # tsc --watch 监听编译
npm run start        # node dist/index.js
```

## 代码规范

### 标识符
- 所有代码标识符（变量、函数、类、方法、属性、命名空间）必须使用规范英文单词
- 禁止中文/拼音作为标识符

### 注释与文档
- 代码注释使用简体中文
- 提交信息使用简体中文，遵循 Conventional Commits
- UI 文字使用简体中文

### 提交格式

```
feat: 描述
fix: 描述
chore: 描述
perf: 描述

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>
```

**禁止 `git push --force`**。push 被拒时用 `git pull --rebase`。

## 项目架构

```
src/cli/app.tsx       ← Ink TUI 组件（修改入口）
src/cli/interface.ts  ← REPL/单次模式分发
src/cli/output.ts     ← 流式输出缓冲区

src/agent/loop.ts     ← Agent 主循环（不要直接改）
src/agent/types.ts    ← 类型定义
src/agent/system-prompt.ts ← 系统提示词

src/api/client.ts     ← DeepSeek API 封装（openai SDK）
src/api/message-builder.ts ← 消息格式转换
src/api/retry.ts      ← 重试策略

src/tools/*.ts        ← 工具实现
src/tools/registry.ts ← 工具注册中心
src/tools/path-utils.ts ← 工作区安全边界

src/context/manager.ts ← 上下文管理
src/config/loader.ts  ← 配置加载
```

### 关键接口

```typescript
// Agent 循环配置
LoopConfig {
  client: DeepSeekClient
  softLimit: number        // 提示阈值
  hardLimit: number        // 强制停止
  toolRegistry: ToolRegistry
  initialMessages?: ConversationMessage[]
  onReasoningChunk?: (text: string) => void
  onContentChunk?: (text: string) => void
  onToolCall?: (name: string, args: string) => void
  onRoundExceeded?: (round: number) => Promise<boolean>
}

// 工具注册
class ToolRegistry {
  register(tool: RegisteredTool): void
  getDefinitions(): ToolDefinition[]
  execute(toolCall: ToolCall): Promise<string>
}

// 上下文管理
class ContextManager {
  addMessage(msg: ConversationMessage): void
  getMessages(): ConversationMessage[]  // 自动压缩
  reset(): void
}
```

## DeepSeek API 关键参数

```typescript
// 思考模式
extra_body: { thinking: { type: "enabled" } }
reasoning_effort: "high" | "max"

// strict 模式（/beta 端点）
strict: true  // ClientConfig 中设置

// 流式
stream: true
stream_options: { include_usage: true }
```

## 已知限制

1. **Ink `useInput` 不支持 Windows 中文 IME** → 改用 `readline` 处理输入
2. **思考模式不支持 `tool_choice: "required"`** → 靠系统提示词引导
3. **strict 模式所有参数必须 required** → 用 `0`/`""` 作哨兵值表示"用默认值"
4. **`finalChatCompletion()` 在某些 openai SDK 版本不可用** → 从流式 chunk 取 `usage`

## TUI 开发注意事项

- 输入层使用 Node.js 原生 `readline` (`terminal: true`)
- 渲染层使用 Ink (React)
- 不要使用 `useInput` hook（IME 不兼容）
- 状态通过 React `useState` 管理，`setState` 触发 Ink 重渲染
- `readline` 的 prompt 出现在 Ink 渲染区域下方

## 测试

```bash
# 编译
npm run build

# 单次模式（不需要 TTY）
node dist/index.js "1+1等于几"

# REPL 模式（需要真实终端）
node dist/index.js
```
