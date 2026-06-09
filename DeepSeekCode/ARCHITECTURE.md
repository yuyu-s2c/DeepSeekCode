# 架构设计

## 分层架构

```
┌─────────────────────────────────────────────────┐
│                   UI 层                          │
│  MainWindow │ SettingsWindow │ 侧边栏 │ 状态栏   │
├─────────────────────────────────────────────────┤
│                事件总线                           │
│  StreamStarted │ Chunk │ ToolCall │ Permission   │
├──────────────┬──────────────┬───────────────────┤
│  对话引擎     │  工具系统     │  权限系统          │
│  DeepSeekClient│ ITool      │  PermissionManager │
│  Conversation │  ToolRegistry│                   │
│  Context      │  ToolPipeline│                   │
│  SubagentRunner│ MCP适配器   │                   │
│  PlanModeService│             │                   │
├──────────────┼──────────────┼───────────────────┤
│  会话管理     │  配置体系     │  扩展系统           │
│  ISessionStore│ ConfigService│  SkillEngine      │
│  MemoryService│ PlanModeService│ McpService     │
│               │              │  CustomCommandService│
├──────────────┴──────────────┴───────────────────┤
│              服务容器                             │
│  ServiceLocator（统一生命周期管理）                 │
└─────────────────────────────────────────────────┘
```

## 模块职责

### UI 层
- **MainWindow**：主界面，WebView2 对话区 + 左侧侧边栏（Todo + 子代理）+ Thinking 状态栏 + 输入栏 + 历史弹窗 + 命令补全
- **SettingsWindow**：五 Tab 设置窗口（账户/模型/Thinking/生成参数/Beta）
- **PermissionDialog**：三按钮权限确认弹窗（拒绝/允许本次/允许所有）
- **StatusViewModel**：状态栏数据绑定（Token/缓存命中/上下文占用 ctx:N%/模式指示）

### 事件总线
- 解耦 UI ↔ 业务逻辑，通过发布/订阅通信
- 事件类型：流式输出（Started/Chunk/Completed/Cancelled/Error）、工具调用（Request/Result）、权限询问、会话切换、配置变更（含 CredentialsChanged）

### 对话引擎
- **DeepSeekClient**：封装 DeepSeek API（SSE 流式、thinking: adaptive、reasoning_effort、运行时 UpdateCredentials）
- **ConversationManager**：消息列表管理、上下文窗口控制（900K/1M token）、额外 system context 动态注入
- **ContextStrategy**：SmartCompress（AI 摘要旧轮次）→ SlidingWindow（暴力裁剪兜底）→ FileInjection（项目文件注入）
- **PlanModeService**：Plan 模式状态管理 + 5 阶段提示词生成
- **ChatRenderer**：WebView2 渲染引擎，marked.js + highlight.js + KaTeX

### 工具系统
- **ITool**：工具接口（Name + Description + Parameters + ExecuteAsync + ToDefinition）
- **ToolRegistry**：工具注册 + 撤销 + OpenAI 格式函数定义生成 + 调用分发
- **ToolPipeline**：工具执行管线，前置/后置过滤器链
- 15 个内置工具：read_file, edit_file, write_file, glob, grep, shell, webfetch, read_skill, git_diff, git_log, git_commit, todo_write, task, enter_plan_mode, exit_plan_mode
- **McpToolAdapter**：MCP 工具 → ITool 适配器

### MCP 协议
- **McpClient**：JSON-RPC 2.0 over stdio 客户端（握手 → 发现工具 → 调用工具）
- **McpService**：服务器管理器（加载 mcp-servers.json 配置、后台启动连接、动态注册工具）
- 工具名前缀 `mcp_{serverName}_{toolName}` 防止冲突
- `/mcp` 命令查看状态

### Plan 模式
- **PlanModeService**：管理 Plan/自动双模式切换
- **EnterPlanModeTool / ExitPlanModeTool**：AI 可通过工具主动进入/退出
- **PlanCommand**：`/plan` / `/plan off` 用户手动切换
- 进入时动态注入 5 阶段工作流 system 消息
- 状态栏常驻显示当前模式（🤖 自动 / 📋 Plan）

### 权限系统
- **PermissionManager**：Allow / Deny / Ask 三级权限 + 会话级"允许所有"临时覆盖
- 三按钮弹窗：拒绝（立即停止对话）、允许本次、允许所有
- Deny 规则（危险命令）始终生效；命令名精确匹配（首词），防止子串误伤
- Plan 模式下所有写工具自动 Deny（计划文件除外）

### Slash 命令
- **ISlashCommand**：命令接口
- **SlashCommandRegistry**：注册 + 解析 + 分发 + 撤销
- **CustomCommandService**：用户自定义命令（~/.deepseek-code/custom-commands.json），支持 {args} 模板替换
- **CommandResult.UserPrompt**：自定义命令生成的 prompt 替代原始输入
- 内置命令：help / clear / model / save / load / config / compact / settings / workspace / skills / mcp / plan（12 个）
- 默认自定义命令：review / explain / refactor（3 个）

### 配置体系
- **ConfigService**：用户级配置（~/.deepseek-code/config.json），DPAPI 加密 API Key
- **ProjectConfig**：项目级配置（.deepseek-code/project.json）
- 合并策略：项目级 > 用户级

### Memory 系统
- **MemoryService**：跨会话记忆（~/.deepseek-code/memory.md）
- 启动时自动注入到 system 消息最末尾
- AI 通过 edit_file/write_file 更新，用户也可手工编辑

### 会话管理
- **ISessionStore**：会话持久化接口 + SetWorkspace 工作区切换
- **FileSessionStore**：JSON 文件实现，存储于 {workspace}/.deepseek-code/sessions/（物理隔离）
- 💬 历史弹窗：键盘导航，退出未保存提示

### 技能引擎
- **SkillEngine**：扫描 + 加载 Markdown 技能文件 → name+description 全量注入系统提示词
- 正文通过 read_skill 工具按需加载
- 支持用户级和项目级技能

### 子代理系统
- **SubagentRunner**：独立对话循环，线程池隔离（Task.Run）
- **TaskTool**：explore（只读，Flash，无 Thinking）/ general（完整权限，继承主模型，Thinking 开）
- 并行调度：Task.WhenAll，取消传播

### Diff 预览
- **DiffRenderer**：LCS 行级 diff + 智能裁剪 + FormatTextDiff（max 30 行）
- edit_file / write_file 自动生成 diff 渲染到对话区

### Todo 追踪
- **TodoWriteTool**：完整替换任务列表
- 左侧侧边栏常驻显示：状态图标（○/⏳/✔/✘）+ 优先级标记（⚡）

### 上下文显示
- 状态栏实时显示 `📐 ctx:45%(450k/1M)`，从 API 返回的 prompt_tokens 读取
- 工具卡片结果限制 5 行，超出显示 `···共N行`

### 日志系统
- **Logger**：文件日志，实现 MCP.ILogger 接口
- **CrashHandler**：注册 AppDomain.UnhandledException

### 系统提示词架构
- **参考 Claude Code Harness 风格**：英文 Markdown，分 Harness / Session / Memory / Environment / Context management 五个板块
- **工具 Description 微文档化**：每个工具描述 4-8 行（When to use / Constraints / Tips）
- **Skills 全量注入**：所有已启用 Skill 的 description 注入 system 消息
- **Git 状态注入**：启动时自动捕获 git branch / status / recent commits
- **Memory 注入**：cross-session memory.md 内容自动注入
- **Context management 通知**：告知模型 SmartCompress 摘要机制

## 渲染技术栈
- **Markdown**：marked.js（离线本地化）→ 流式实时渲染
- **语法高亮**：highlight.js（github 亮色主题）
- **数学公式**：KaTeX + protectMath/restoreMath 预处理
- **DOM 管理**：appendChild + createElement，无 innerHTML 拼接

## 数据流

### 一次完整的对话请求

```
用户输入
  │
  ├─ /开头 → SlashCommandRegistry 解析
  │     ├─ 内置命令 → 执行（可能改变 Plan 模式/配置等状态）
  │     └─ 自定义命令 → {args} 替换模板 → 生成 prompt → 继续往下
  │
  └─ 普通文本
      │
      ├─ SyncPlanMode() — 检测 Plan 模式 → 注入/清除 system context
      ├─ 追加到 UI（用户消息气泡）
      ├─ 追加到 ConversationManager
      ├─ ContextStrategy 编排器
      │     ├─ SmartCompress: AI 摘要旧轮次（超 900K 时）
      │     ├─ SlidingWindow: 暴力裁剪兜底
      │     └─ FileInjection: 注入项目关键文件
      ├─ 注入 extraSystemContext（Plan 模式提示词 / Memory 等）
      ├─ EventBus.Publish(StreamStarted)
      │
      ├─ DeepSeekClient.StreamChatAsync(tools, messages)
      │     ├─ reasoning_content → Thinking 面板
      │     ├─ content → WebView2 流式渲染
      │     └─ tool_calls → 累积工具调用
      │
      ├─ 流结束 → EventBus.Publish(StreamCompleted)
      │
      ├─ 有 tool_calls？
      │     ├─ 权限检查 → ToolPipeline 执行
      │     ├─ enter_plan_mode / exit_plan_mode → PlanModeService 状态变更
      │     ├─ task 子代理 → 线程池并行
      │     └─ 其他工具 → 顺序执行
      │     ├─ 结果回传 → 工具卡片渲染（5 行截断）
      │     └─ 继续 StreamChatAsync（循环）
      │
      └─ 对话结束 → 状态栏更新（Token/上下文占用/模式）
```

## 设计决策

| 决策 | 理由 |
|------|------|
| WPF 而非 Blazor/Web | Windows 原生，零 Web 服务器依赖，GPU 加速渲染 |
| WebView2 + marked.js/highlight.js/KaTeX | 成熟 JS 生态，实时流式渲染，离线可用 |
| 内置 HTTP 而非 OpenAI SDK | DeepSeek 是 OpenAI 兼容格式，SDK 反而增加抽象层 |
| 自定义 DI 而非 Microsoft.Extensions.DI | 减少依赖，桌面应用不需要重量级容器 |
| 事件总线而非直接调用 | 工具调用、流式输出、权限询问全部解耦 |
| 会话工作区物理隔离 | JSON 文件存 {workspace}/.deepseek-code/sessions/ |
| JSON-RPC 2.0 over stdio for MCP | 业界标准传输方式，跨平台兼容 |
| SmartCompress 优先于 SlidingWindow | AI 摘要保留关键信息，避免暴力丢轮次 |
| Plan Mode 通过 extraSystemContext 注入 | 不污染永久消息列表，退出自动清理 |

## 扩展点

| 扩展点 | 方式 |
|--------|------|
| 新增工具 | 实现 `ITool` → `ToolRegistry.Register()` |
| 新增命令 | 实现 `ISlashCommand` → `SlashCommandRegistry.Register()`（或 custom-commands.json） |
| 新增 MCP 服务器 | 在 `mcp-servers.json` 添加配置项 |
| 新增权限规则 | `PermissionManager.AddRule()` |
| 新增上下文策略 | 实现 `IContextStrategy` → `ContextStrategyOrchestrator.AddStrategy()` |
| 新增技能 | 放置 `.md` 文件到技能目录 |
| 支持其他 AI 厂商 | 替换 `DeepSeekClient` 实现（OpenAI 兼容格式通用） |
