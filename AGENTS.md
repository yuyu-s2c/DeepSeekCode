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

1. **思考模式不支持 `tool_choice: "required"`** → 靠系统提示词引导
2. **strict 模式所有参数必须 required** → 用 `0`/`""` 作哨兵值表示"用默认值"
3. **`finalChatCompletion()` 在某些 openai SDK 版本不可用** → 从流式 chunk 取 `usage`

## TUI 开发注意事项

- **绝对禁止：修改代码时破坏已有功能。** 每次改动必须维护上下文，确认不影响其他模块。改输入不要弄坏渲染，修错误不要绕掉正常逻辑。同一个问题反复出 bug 须复盘根因，禁止打补丁式修复。
- 输入层使用 `readline.emitKeypressEvents` + raw mode 自定义键盘处理（替代 readline prompt），通过 Ink `keypress` 事件逐字符捕获输入并渲染在 Ink 组件内
- 渲染层使用 Ink (React)，外层 `borderStyle="round"` 圆角边框
- 底部栏（输入行、模式行、ctx 行）通过 `flexShrink={0}` 固定，内容区通过 `flexGrow={1}` 填充剩余空间
- 内容区按行渲染，取最新 N 行显示，`overflowY="hidden"` 裁剪溢出
- 思考状态在内容区与分隔线之间固定显示，不随内容滚动
- 使用 `readline` 的 `keypress` 事件处理输入（`key.name` / `key.ctrl` 判断按键），比原始 `data` 事件更可靠
- 中文 IME 通过 `readline.emitKeypressEvents` 的终端模式管理正常支持
- 粘贴通过 bracketed paste 序列（`\x1b[200~` / `\x1b[201~`）检测，`emitKeypressEvents` 内部处理
- 多行输入用 `Ctrl+J` 插入换行。粘贴大段文本 (>5 行) 自动折叠为 `[N 行, K 字符]` 计数栏，手动输入不受影响
- 通过按键间隔检测（< 40ms）区分粘贴/手动，`pasteNewlineRef` 跳过 Windows `\r\n` 中的 `\n` 避免重复换行

## 模式切换

- **Tab** 键切换 `auto` / `plan` 模式
- Plan Mode 三层保护：
  1. 系统提示词注入 "Plan Mode" 指令（`buildSystemPrompt(mode)` 动态生成）
  2. `ToolRegistry.setPlanMode(true)` 黑名单过滤 `write_file`/`edit_file`/`run_shell`
  3. `setApprovalCallback(() => mode === "auto")` 拒绝 shell 审批
- `useCallback` deps 必须包含 `mode`，否则闭包过期导致系统提示词不更新

## Agent 中止

- `AbortController` 串联 `app.tsx` → `LoopConfig.signal` → `client.chat({ signal })`
- Ctrl+C 在 `status==="thinking"` 时调用 `abortRef.current?.abort()`
- loop 中捕获 `"用户中断"` 返回 `"已中止。"` 而非错误前缀

## Token 统计

- `LoopConfig.onRoundUsage` 每轮 API 返回后推送累计 `{ promptTokens, completionTokens }`
- ctx 行实时刷新百分比，不再等整个对话结束
- 思考中 `thinkingTokens` 用 `chunk.length / 3` 估算，完成后用 API 返回的精确值

## 工具执行状态

- `LoopConfig.onToolResult(name, result)` 每个工具执行完触发
- 指示器在工具执行时显示 `⠋ 执行工具: bash`，下一轮流式开始自动切回 Think 模式
- 通过 `isToolPhaseRef` 追踪当前处于思考还是工具执行阶段

## ContentArea 行数

- 弃用 `rows - 12` 硬编码，改为动态计算：
  ```
  contentLines = (rows - 3) - 4 - effectiveInputLines - indicatorLines
  ```
- `effectiveInputLines` 在粘贴折叠时为 1，正常时按 `\n` 计数

## 欢迎页

- `ContentArea` 在无消息且无流式内容时渲染快捷键帮助面板
- 有消息后自动消失

## 测试

```bash
# 编译
npm run build

# 单次模式（不需要 TTY）
node dist/index.js "1+1等于几"

# REPL 模式（需要真实终端）
node dist/index.js
```
