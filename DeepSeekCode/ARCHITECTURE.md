# 架构设计

## 分层架构

```
┌─────────────────────────────────────────────────┐
│                   UI 层                          │
│  MainWindow │ ApiKeyWindow │ Commands │ StatusBar│
├─────────────────────────────────────────────────┤
│                事件总线                           │
│  StreamStarted │ Chunk │ ToolCall │ Permission   │
├──────────────┬──────────────┬───────────────────┤
│  对话引擎     │  工具系统     │  权限系统          │
│  DeepSeekClient│ ITool      │  PermissionManager │
│  Conversation │  ToolRegistry│                   │
│  Context      │  ToolPipeline│                   │
│  SubagentRunner│              │                   │
├──────────────┼──────────────┼───────────────────┤
│  会话管理     │  配置体系     │  技能引擎           │
│  ISessionStore│ ConfigService│  SkillEngine      │
├──────────────┴──────────────┴───────────────────┤
│              服务容器                             │
│  ServiceLocator（统一生命周期管理）                 │
└─────────────────────────────────────────────────┘
```

## 模块职责

### UI 层
- **MainWindow**：主界面，WebView2 对话区 + Thinking 面板 + 思考状态栏 + 输入栏 + 历史弹窗 + 命令补全 Popup
- **ApiKeyWindow**：API Key 配置弹窗
- **SettingsWindow**：四 Tab 设置窗口（模型/Thinking/生成参数/Beta）
- **PermissionDialog**：三按钮权限确认弹窗（拒绝/允许本次/允许所有）
- **StatusViewModel**：状态栏数据绑定（MVVM）

### 事件总线
- 解耦 UI ↔ 业务逻辑，通过发布/订阅通信
- 事件类型：流式输出（Started/Chunk/Completed/Cancelled/Error）、工具调用（Request/Result）、权限询问、会话切换、配置变更

### 对话引擎
- **DeepSeekClient**：封装 DeepSeek API（SSE 流式 HTTP、thinking/reasoning_content 解析、reasoning_effort: max）
- **ConversationManager**：消息列表管理、上下文窗口控制（900K token 滑动窗口）
- **ContextStrategy**：上下文策略接口 + 实现（滑动窗口裁剪、智能压缩预留、文件注入）
- **ChatRenderer**：WebView2 渲染引擎，marked.js + highlight.js + KaTeX 实时 Markdown/代码/数学渲染

### 工具系统
- **ITool**：工具接口（Name + Description + Parameters + ExecuteAsync）
- **ToolRegistry**：工具注册 + OpenAI 格式函数定义生成 + 调用分发
- **ToolPipeline**：工具执行管线，前置/后置过滤器链
- 13 个内置工具：read_file, edit_file, write_file, glob, grep, shell, webfetch, read_skill, git_diff, git_log, git_commit, todo_write, task

### 权限系统
- **PermissionManager**：Allow / Deny / Ask 三级权限 + 会话级"允许所有"临时覆盖
- 三按钮弹窗：拒绝（立即停止对话）、允许本次、允许所有（本轮后续不再询问）
- Deny 规则（危险命令）始终生效，不受"允许所有"影响
- 默认规则：只读工具 Allow、危险命令 Deny、写操作 Ask

### Slash 命令
- **ISlashCommand**：命令接口
- **SlashCommandRegistry**：注册 + 解析 + 分发
- 内置命令：help / clear / model / save / load / config / compact / settings / workspace / skills（10 个）
- /save 自动从第一句用户消息生成标题；/load 仅显示当前工作区的会话

### 配置体系
- **ConfigService**：用户级配置（`~\.deepseek-code\config.json`）
- **ProjectConfig**：项目级配置（`.deepseek-code\project.json`）
- 合并策略：项目级 > 用户级

### 会话管理
- **ISessionStore**：会话持久化接口 + SetWorkspace 工作区切换
- **FileSessionStore**：JSON 文件实现，存储于 `{workspace}/.deepseek-code/sessions/`（物理隔离）
- **SessionMetadata**：会话元数据（ID、标题、时间、消息数、模型、工作区路径）
- 支持覆盖保存（同 ID 覆盖）、删除（Delete 键/按钮）、智能标题生成
- 💬 历史弹窗：↑↓/Enter/Esc 键盘导航，退出未保存提示

### 技能引擎
- **SkillEngine**：扫描 + 加载 Markdown 技能文件 → 注入系统提示词
- 支持用户级和项目级技能

### 子代理系统
- **SubagentRunner**：独立对话循环，线程池隔离（Task.Run）
- **TaskTool**：task 工具，explore（只读，V4 Flash，Thinking 关）/ general（完整权限，继承主模型，Thinking 开）双模式
- 并行调度：`Task.WhenAll` 同时执行多个子代理
- 取消传播：主 CancelToken 注入参数，停止按钮一并取消子代理

### Diff 预览
- **DiffRenderer**：LCS 行级 diff 算法 + WPF Section 彩色渲染
- 绿色(+) 新增行、红色(-) 删除行、深色背景区分
- 智能裁剪：只展示变更区域 + 最多 8 行上下文

### Todo 追踪
- **TodoWriteTool**：todo_write 工具，完整替换任务列表
- UI 面板：进度条 + 状态图标（○/⏳/✔/✘）+ 优先级标记（⚡）

### 思考意图提取
- 流式输出中从 `reasoning_content` 尾部提取意图型句子（Let me/I'll/我先/接下来...）
- 显示在思考状态栏，让用户实时了解 AI 下一步计划

## 渲染技术栈
- **Markdown**：marked.js（CDN v12）→ 流式实时渲染
- **语法高亮**：highlight.js（@highlightjs/cdn-assets，github 亮色主题）
- **数学公式**：KaTeX（CDN v0.16）+ protectMath/restoreMath 预处理防 marked.js 破坏 LaTeX
- **DOM 管理**：全部使用 appendChild + createElement，无 innerHTML 拼接（避免 DOM 引用丢失）

## 数据流

### 一次完整的对话请求

```
用户输入
  │
  ├─ /开头 → SlashCommandRegistry 解析 → 执行命令（不调用 AI）
  │
  └─ 普通文本
      │
      ├─ 追加到 UI（用户消息气泡）
      ├─ 追加到 ConversationManager
      ├─ ContextStrategy 裁剪上下文
      ├─ EventBus.Publish(StreamStarted)
      │
      ├─ DeepSeekClient.StreamChatAsync(tools, messages)
      │     │
      │     ├─ 普通 content → EventBus.Publish(StreamChunk) → UI 追加
      │     ├─ reasoning_content → 追加到 Thinking 面板
      │     └─ tool_calls → 累积工具调用
      │
      ├─ 流结束 → EventBus.Publish(StreamCompleted)
      │
      ├─ 有 tool_calls？
      │     │
      │     ├─ 是 → PermissionManager.Check()
      │     │     ├─ Allow → ToolPipeline.ExecuteAsync()
      │     │     ├─ task 工具 → 线程池启动 SubagentRunner
      │     │     │     ├─ 多 task 并行 → Task.WhenAll
      │     │     │     └─ 结果回传
      │     │     └─ 其他工具 → 顺序执行
      │     ├─ Ask → 弹窗确认
      │     └─ Deny → 拦截
      │     │
      │     ├─ 结果回传 → 继续 StreamChatAsync（循环）
      │     └─ 无 → 本轮结束，MarkdownRenderer 渲染最终结果
      │
      └─ 对话结束
```

## 设计决策

| 决策 | 理由 |
|------|------|
| WPF 而非 Blazor/Web | Windows 原生，零 Web 服务器依赖，GPU 加速渲染 |
| WebView2 + marked.js/highlight.js/KaTeX | 成熟 JS 生态，实时流式渲染，支持 30+ 语言高亮 + LaTeX 数学 |
| 内置 HTTP 而非 OpenAI SDK | DeepSeek 是 OpenAI 兼容格式，SDK 反而增加抽象层 |
| 自定义 DI 而非 Microsoft.Extensions.DI | 减少依赖，桌面应用不需要重量级容器 |
| 事件总线而非直接调用 | 工具调用、流式输出、权限询问全部解耦 |
| 会话工作区物理隔离 | JSON 文件存 `{workspace}/.deepseek-code/sessions/`，文件系统级安全保障 |
| MVP 优先，接口预留 | 核心跑通再扩展，不提前过度设计 |

## 扩展点

| 扩展点 | 方式 |
|--------|------|
| 新增工具 | 实现 `ITool` → `ToolRegistry.Register()` |
| 新增命令 | 实现 `ISlashCommand` → `SlashCommandRegistry.Register()` |
| 新增权限规则 | `PermissionManager.AddRule()` |
| 新增上下文策略 | 实现 `IContextStrategy` → `ContextStrategyOrchestrator.AddStrategy()` |
| 新增工具过滤器 | 实现 `IToolPipelineFilter` → `ToolPipeline.AddFilter()` |
| 新增技能 | 放置 `.md` 文件到技能目录 |
| 支持其他 AI 厂商 | 替换 `DeepSeekClient` 实现（OpenAI 兼容格式通用） |
