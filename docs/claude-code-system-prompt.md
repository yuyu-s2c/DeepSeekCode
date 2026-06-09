# Claude Code 系统提示词分析

> 来源：通过 Fiddler 抓包 `POST https://api.deepseek.com/anthropic/v1/messages` 请求体。
> 模型：`deepseek-v4-pro`，thinking: adaptive，effort: max。
> 日期：2026-06-08

---

## 1. 请求概览

| 字段 | 值 |
|------|-----|
| 端点 | `POST https://api.deepseek.com/anthropic/v1/messages?beta=true` |
| 模型 | `deepseek-v4-pro` |
| max_tokens | 32000 |
| thinking | `{"type":"adaptive"}` |
| effort | `max` |
| stream | true |
| User-Agent | `claude-cli/2.1.168` |
| Anthropic Beta Headers | 8 个 beta 特性 |

---

## 2. 消息结构

Claude Code 使用了 **Anthropic Messages API 格式**（非 OpenAI Chat Completions），消息分为三层：

```
messages[0] — user role, content 数组
  ├─ text: <system-reminder> (CLAUDE.md 注入 + currentDate)
  └─ text: "你好" (用户实际消息, cache_control: ephemeral)

system[0] — "You are Claude Code, Anthropic's official CLI for Claude."
system[1] — 完整 Harness 系统提示词 (数千行)
  ├─ # Harness (输出格式、工具规则、代码引用格式)
  ├─ # Session-specific guidance
  ├─ # Memory (持久化文件记忆系统)
  ├─ # Environment (环境信息 — 工作区、Git、平台、模型)
  └─ # Context management (上下文管理、gitStatus、Recent commits)

tools[] — 22+ 个工具，每个 description 都是微型规范文档
```

### 关键发现：CLAUDE.md 注入方式

CLAUDE.md 内容并不是 system 消息，而是作为 **user 消息** 的一部分通过 `<system-reminder>` 标签注入：

```xml
<system-reminder>
As you answer the user's questions, you can use the following context:
# claudeMd
Codebase and user instructions are shown below. Be sure to adhere to these instructions.
IMPORTANT: These instructions OVERRIDE any default behavior and you MUST follow them exactly as written.

Contents of %USERPROFILE%\.claude\CLAUDE.md (user's private global instructions for all projects):
...（完整 CLAUDE.md 内容）...

# currentDate
Today's date is 2026/06/08.

IMPORTANT: this context may or may not be relevant to your tasks.
You should not respond to this context unless it is highly relevant to your task.
</system-reminder>
```

---

## 3. 完整 System Prompt (system[1])

### 3.0 身份声明

```
You are Claude Code, Anthropic's official CLI for Claude.
```

### 3.1 Harness 规则

```
You are an interactive agent that helps users with software engineering tasks.

IMPORTANT: Assist with authorized security testing, defensive security, CTF challenges,
and educational contexts. Refuse requests for destructive techniques, DoS attacks,
mass targeting, supply chain compromise, or detection evasion for malicious purposes.
Dual-use security tools require clear authorization context.

# Harness
 - Text you output outside of tool use is displayed to the user as Github-flavored
   markdown in a terminal.
 - Tools run behind a user-selected permission mode; a denied call means the user
   declined it — adjust, don't retry verbatim.
 - <system-reminder> tags in messages and tool results are injected by the harness,
   not the user. Hooks may intercept tool calls; treat hook output as user feedback.
 - Prefer the dedicated file/search tools over shell commands when one fits.
   Independent tool calls can run in parallel in one response.
 - Reference code as `file_path:line_number` — it's clickable.

Write code that reads like the surrounding code: match its comment density, naming,
and idiom.

For actions that are hard to reverse or outward-facing, confirm first unless durably
authorized or explicitly told to proceed without asking; approval in one context
doesn't extend to the next. Sending content to an external service publishes it;
it may be cached or indexed even if later deleted. Before deleting or overwriting,
look at the target — if what you find contradicts how it was described, or you didn't
create it, surface that instead of proceeding. Report outcomes faithfully: if tests
fail, say so with the output; if a step was skipped, say that; when something is done
and verified, state it plainly without hedging.
```

### 3.2 Session-specific Guidance

```
# Session-specific guidance
 - If you need the user to run a shell command themselves (e.g., an interactive
   login like `gcloud auth login`), suggest they type `! <command>` in the prompt
   — the `!` prefix runs the command in this session so its output lands directly
   in the conversation.
 - When the user types `/<skill-name>`, invoke it via Skill. Only use skills
   listed in the user-invocable skills section — don't guess.
```

### 3.3 Memory 系统

```
# Memory

You have a persistent file-based memory at `%USERPROFILE%\.claude\projects\...`
Each memory is one file holding one fact, with frontmatter:

```markdown
---
name: <short-kebab-case-slug>
description: <one-line summary>
metadata:
  type: user | feedback | project | reference
---

<the fact>
```

In the body, link to related memories with `[[name]]`.
After writing the file, add a one-line pointer in `MEMORY.md`.
Before saving, check for an existing file that already covers it.
```

### 3.4 Environment

```
# Environment
 - Primary working directory: D:\测试专用工作区
 - Is a git repository: true
 - Platform: win32
 - Shell: bash (use Unix shell syntax)
 - OS Version: Windows 11 Home China 10.0.26200
 - You are powered by the model deepseek-v4-pro
 - The most recent Claude model family is Claude 4.X
```

### 3.5 Context Management + Git 状态

```
# Context management
When the conversation grows long, some or all of the current context is summarized;
the summary, along with any remaining unsummarized context, is provided in the next
context window so work can continue.

gitStatus: This is the git status at the start of the conversation.
Note that this status is a snapshot in time.

Current branch: master
Main branch: main
Git user: yu9929

Status:
 D config.json
  D data/test-data.txt
  D scripts/run-test.bat
  D src/utils/helper.cs

Recent commits:
b4f7465 test: 更新测试状态
7c1237a test: 第二轮功能测试
ab23538 test: 功能测试
```

---

## 4. 工具定义分析

Claude Code 定义了 22+ 个工具，每个工具的 `description` 极其详细：

| 工具 | description 长度 | 特点 |
|------|-----------------|------|
| Agent | **~3 页** | 列出所有 agent 类型、何时使用、参数说明、并行策略 |
| AskUserQuestion | ~1 页 | 详细的 when to use / when not to use |
| Bash | ~0.5 页 | Git 规则、Co-Authored-By 签名格式 |
| CronCreate | **~2 页** | cron 表达式指南、去峰策略 (避 :00/:30)、durable vs session |
| Edit | ~0.3 页 | 必须先 Read、replace_all 说明 |
| EnterPlanMode | **~1.5 页** | 7 种必须用/4 种不用 的场景，各有示例 |
| Glob | ~0.2 页 | 简洁 |
| Grep | ~0.3 页 | 强调 ripgrep 语法 |
| Read | ~0.3 页 | PDF/图片/notebook 支持 |
| Skill | ~0.3 页 | BLOCKING REQUIREMENT 规则 |
| WebFetch | ~0.3 页 | 缓存 15 分钟 |
| WebSearch | ~0.2 页 | US-only |
| Workflow | **~6 页** | 完整 DSL 文档：meta/phases/pipeline/parallel/budget/patterns |
| Write | ~0.2 页 | 必须先 Read |

### 工具 description 的写法模式

每个工具 description 都采用以下结构：

```
工具简短用途。

## When to Use (何时使用)
- 明确的使用场景列表
- 每个场景有具体示例
- Good ✓ 和 Bad ✗ 示例

## When NOT to Use (何时不用)
- 反例和误用场景

## Usage notes (使用说明)
- 参数约束
- 行为细节
- 边界情况

## Examples (示例)
- JSON 格式的调用示例

## Tips (技巧)
- 进阶用法
- 常见陷阱
```

---

## 5. Skill 注入机制

Claude Code 将所有已安装 Skill 的 **name + description** 全量注入到 system 消息中：

```
The following skills are available for use with the Skill tool:

- brainstorming: You MUST use this before any creative work...
- docker-expert: You are an advanced Docker containerization expert...
- documentation-writer: Diátaxis Documentation Expert...
- docx: Use this skill whenever the user wants to create, read, edit...
- gh-cli: GitHub CLI comprehensive reference...
- git-commit: Execute git commit with conventional commit message analysis...
- pdf: Use this skill whenever the user wants to do anything with PDF files...
- pptx: Use this skill any time a .pptx file is involved...
- prd: Generate high-quality Product Requirements Documents...
- tailwindcss-best-practices: Tailwind CSS v4.x utility-first CSS framework...
- test-driven-development: Use when implementing any feature or bugfix...
- webapp-testing: Toolkit for interacting with and testing local web applications...
- writing-plans: Use when you have a spec or requirements...
- xlsx: Use this skill any time a spreadsheet file is the primary input...
... (共 30+ 条 Skill)
```

**与你的项目的对比**：
- 你的 WPF 应用：只贴 Skill 名称 + 一行简介 → 需要 `read_skill` 按需加载
- Claude Code：每个 Skill 2-5 句 description 全量注入 → 模型立即知道何时该用哪个

---

## 6. 值得借鉴的设计

### 6.1 提示词层级

```
system[0]: 身份声明 (1 句)
system[1]: Harness + 行为规则 (最核心，数千行)
system[1] 后半: Environment + Git 状态 (动态注入)
```

### 6.2 已借鉴的 (你的应用现在也有了)

- DEEPSEEK.md 注入 (对应 CLAUDE.md)
- 工具使用策略 (先读后写、先搜后答、批量并行)
- 语气与风格约束
- 安全规则

### 6.3 尚未借鉴的 (改进空间)

| 特性 | Claude Code 怎么做 | 你的应用怎么做 |
|------|-------------------|-------------|
| ~~工具 description 详细度~~ | ~~每个工具微型文档 (数百字)~~ | ✅ **已实现** — 每个工具 Description 4-8 行微型文档，覆盖 When to Use / Constraints / Tips |
| ~~Skill 注入量~~ | ~~全量 name+description 贴进 system~~ | ✅ **已实现** — `SkillEngine.GenerateSkillsIndex()` 将 name + description 全部注入 system 消息 |
| ~~Git 状态注入~~ | ~~git status + recent commits 实时注入 system~~ | ✅ **已实现** — `MainWindow.GetGitStatusContext()` 注入 branch、status、recent commits |
| Memory 系统 | 持久化文件记忆 + frontmatter 索引 | ✅ **已实现** — `~\.deepseek-code\memory.md`，单文件轻量方案，启动时自动注入 |
| ~~Context summary~~ | ~~自动摘要续接长对话~~ | ✅ **已实现** — SmartCompress 优先摘要旧轮次，SlidingWindow 兜底 |
| Plan mode | EnterPlanMode/ExitPlanMode 工具强制先规划 | ✅ **已实现** — 5 阶段工作流，`/plan` 命令 + AI 工具，状态栏常驻指示 |
| ~~环境信息注入~~ | ~~平台/Shell/OS/模型版本 全注入 system~~ | ✅ **已实现** — system prompt 含 platform、shell、workspace、git 状态等完整环境信息 |
| Hooks 系统 | PreToolUse / PostToolUse / Notification 钩子 | ❌ 未实现（有 ToolPipeline 内部过滤器但用户不可配置） |
| 自定义 Slash 命令 | 用户可自定义命令模板 | ✅ **已实现** — `~\.deepseek-code\custom-commands.json`，支持 `{args}` 模板替换 |
| MCP 协议 | 动态接入外部 MCP 服务器工具 | ✅ **已实现** — `MCP/McpClient.cs`，JSON-RPC 2.0 over stdio，`mcp-servers.json` 配置 |
| 侧边栏 / 常驻面板 | TODO / Subagent 进度常驻显示 | ✅ **已实现** — 左侧可折叠/拖拽侧边栏，任务列表 + 子代理进度动画 |
| 账户设置页内配置 | API Key 在设置 Tab 中管理 | ✅ **已实现** — SettingsWindow 新增"账户"标签页 |

---

## 7. 建议的下一步改进

按优先级排列：

1. **Hooks 系统** — PreToolUse / PostToolUse 钩子，用户可配置自定义脚本在工具执行前后介入。

2. **系统托盘** — 最小化到托盘常驻后台。

3. **主题切换** — 目前写死了白底天蓝配色，后续可支持暗色/自定义主题。

4. **自动 lint / typecheck** — AI 修改代码后自动运行 linter 和类型检查器，即时反馈。
