# DeepSeek Code — 功能规划与进度跟踪

> 对标 Claude Code，为目标架构提供路线图和实现状态跟踪。

---

## 一、项目概况

| 项 | 详情 |
|----|------|
| 技术栈 | C# 13 + .NET 10 + WPF + WebView2（marked.js/highlight.js 实时渲染） |
| 目标 | 100% 适配 DeepSeek V4 Pro 的本地 AI Agent |
| 核心优势 | Windows 原生桌面应用，Win11 自带 WebView2 Runtime |
| NuGet 包 | Markdig + Markdig.Wpf + Microsoft.Web.WebView2 + HtmlAgilityPack + ColorCode.Core + FileSystemGlobbing |

---

## 二、架构分层

```
┌─────────────────────────────────────────────────────┐
│                   UI 层 (WPF + WebView2)               │
│  MainWindow │ Slash命令栏 │ 状态栏 │ 对话框组件       │
├─────────────────────────────────────────────────────┤
│                事件总线 (EventBus)                    │
│     解耦 UI ↔ 核心逻辑，支持订阅/发布模式              │
├──────────────┬──────────────┬───────────────────────┤
│  对话引擎     │  工具系统     │  权限系统              │
│  (Conversation│  (Tools)     │  (Permissions)        │
│   Streaming)  │              │                       │
├──────────────┼──────────────┼───────────────────────┤
│  会话管理     │  配置体系     │  上下文策略             │
│  (Sessions)   │  (Config)    │  (Context)            │
├──────────────┴──────────────┴───────────────────────┤
│              基础设施                                  │
│  DeepSeekClient │ MarkdownRenderer │ SkillEngine     │
│  Glob/Grep      │ Shell            │ FileTools       │
└─────────────────────────────────────────────────────┘
```

---

## 三、功能清单与进度

### 3.1 架构层（Framework）

| 编号 | 功能 | 描述 | 状态 | 优先级 |
|------|------|------|------|--------|
| A01 | **事件总线** | 全局 EventBus，支持 URL ↔ 核心逻辑松耦合通信。22 个事件类型 | ✅ 已完成 | 🔴 P0 |
| A02 | **权限系统** | Allow / Deny / Ask 三级权限。危险命令默认 Deny（命令名精确匹配），可信任标记 Always。已接入 ToolPipeline | ✅ 已完成 | 🔴 P0 |
| A03 | **配置体系** | 用户级 `~/.deepseek-code/config.json` + 项目级 `.deepseek-code/project.json`。12 项配置、四 Tab 设置窗口（含账户标签页） | ✅ 已完成 | 🔴 P0 |
| A04 | **上下文策略** | 三级分层策略（TruncateToolResults → SmartCompress → SlidingWindow），缓存友好设计（摘要注入 user 消息保持 KV Cache 前缀），Token 中英文分离估算（0.3/0.6），900K/1M 窗口 | ✅ 已完成 | 🔴 P0 |
| A05 | **工作区服务** | 自动检测项目根目录（.git/.sln/.csproj），点击切换 + /workspace 命令 | ✅ 已完成 | — |
| A06 | **Skills 系统** | Markdown 驱动技能文件，全量 description 注入系统提示词（Claude Code 风格），正文通过 read_skill 工具获取 | ✅ 已完成 | 🟡 P1 |
| A07 | **子代理 (Subagent)** | 并行分派独立任务（线程池 + Task.Run），独立 V4 Flash 模型，explore/general 双模式，带取消令牌和超时保护。左下侧边栏常驻显示 | ✅ 已完成 | 🟢 P2 |
| A08 | **系统提示词优化** | 英文 Claude Code Harness 风格，含 Git 状态注入、Context management 通知、工具 Description 微文档化、Skills 全量注入 | ✅ 已完成 | 🔴 P0 |
| A09 | **日志系统** | 文件日志 + 全局异常捕获，写入 `~\.deepseek-code\logs\`，异步写入不阻塞主线程。覆盖流式错误、异步异常、工具执行、子代理、崩溃 | ✅ 已完成 | 🔴 P0 |
| A10 | **MCP 协议** | JSON-RPC 2.0 over stdio 客户端，动态发现和注册外部 MCP 服务器工具。`~\.deepseek-code\mcp-servers.json` 配置，`/mcp` 命令管理 | ✅ 已完成 | 🟡 P1 |
| A11 | **Plan 模式** | 5 阶段先规划后执行（探索→设计→审查→定稿→审批）。`/plan` 命令 + `enter_plan_mode`/`exit_plan_mode` AI 工具。状态栏常驻模式指示 | ✅ 已完成 | 🟡 P1 |
| A12 | **Memory 系统** | 跨会话记忆文件 `~\.deepseek-code\memory.md`，启动时自动注入 system 消息。AI 通过 edit_file/write_file 更新 | ✅ 已完成 | 🟡 P1 |
| A13 | **自定义命令** | 用户自定义 Slash 命令（`~\.deepseek-code\custom-commands.json`），支持 `{args}` 模板替换。默认内置 /review /explain /refactor | ✅ 已完成 | 🟡 P1 |

### 3.2 核心功能层（Features）

| 编号 | 功能 | 描述 | 状态 | 优先级 |
|------|------|------|------|--------|
| F01 | **DeepSeek API 流式对话** | 支持 chat 和 reasoner 模型，SSE 流式解析，thinking: adaptive 自适应思考，reasoning_effort 控制 | ✅ 已完成 | — |
| F02 | **工具系统 (Function Calling)** | 15 个工具（均英文 Description + 微文档化参数说明）：read_file, edit_file, write_file, glob, grep, shell, webfetch, read_skill, git_diff, git_log, git_commit, todo_write, task, enter_plan_mode, exit_plan_mode | ✅ 已完成 | — |
| F03 | **Markdown 渲染** | Markdig → FlowDocument，支持标题、代码块、列表、引用、粗斜体、链接 | ✅ 已完成 | — |
| F04 | **Thinking 面板** | 右侧可折叠面板，实时展示 reasoning_content | ✅ 已完成 | — |
| F05 | **Slash 命令系统** | 12 个内置命令 + 自定义命令，/help /clear /model /save /load /settings /workspace /config /compact /skills /mcp /plan | ✅ 已完成 | 🔴 P0 |
| F06 | **会话持久化** | 自动保存/恢复对话历史，/save /load 命令，JSON 文件存储 | ✅ 已完成 | 🟡 P1 |
| F13 | **DeepSeek Thinking 集成** | thinking 开关、reasoning_effort、reasoning_content 回传、参数屏蔽 | ✅ 已完成 | — |
| F07 | **Git 集成** | 自动 commit、diff 预览、log 查看 | ✅ 已完成 | 🟢 P2 |
| F08 | **Web 抓取 (webfetch)** | 抓取网页文档内容（Markdown/Text/HTML） | ✅ 已完成 | 🟡 P1 |
| F09 | **项目指令** | 自动读取项目根目录的 `DEEPSEEK.md` 并注入上下文 | ✅ 已完成 | 🟡 P1 |
| F10 | **Todo 追踪** | AI 自动创建/更新任务列表，可视化进度面板（进度条 + 状态图标 + 优先级标记） | ✅ 已完成 | 🟢 P2 |
| F11 | **代码 Diff 预览** | 文件编辑前后对比，LCS 行级 diff + 绿色(+)/红色(-) 渲染。edit_file / write_file 工具自动生成 diff 并渲染到对话区 | ✅ 已完成 | 🟢 P2 |
| F12 | **代码语法高亮** | 对话区代码块语法着色（C#、JS、Python、Go 等 30+ 语言） | ✅ 已完成 | 🟡 P1 |

### 3.3 UI/UX 层（Experience）

| 编号 | 功能 | 描述 | 状态 | 优先级 |
|------|------|------|------|--------|
| U01 | **状态栏** | Token 消耗、🔥 缓存命中率、模型名称、上下文占用 ctx:N%、模式指示（🤖自动/📋Plan） | ✅ 已完成 | 🟢 P2 |
| U01.5 | **工作区指示条** | 顶部常驻路径显示 + 点击弹出原生文件夹选择 | ✅ 已完成 | — |
| U02 | **输入历史** | ↑↓ 方向键翻历史消息 | ✅ 已完成 | 🟡 P1 |
| U03 | **进度指示** | 状态栏 spinner 动画（⣾⣽⣻⢿⡿⣟⣯⣷），AI 思考中/工具执行中/完成 三阶段状态切换 | ✅ 已完成 | 🟢 P2 |
| U04 | **消息右键菜单** | 用户消息气泡右键「复制消息」「重新发送」，非用户区右键「复制」选中文本 | ✅ 已完成 | 🟢 P2 |
| U05 | **命令自动补全** | 输入 `/` 弹出命令列表，↑↓ 选择，Enter/Tab 填入 | ✅ 已完成 | — |
| U06 | **系统托盘** | 最小化到托盘，常驻后台 | ➖ 不做 | 🟢 P3 |
| U07 | **快捷键** | Ctrl+Enter 发送、Ctrl+L 清屏、Ctrl+B 切换侧边栏 | ✅ 已完成 | 🟢 P2 |
| U08 | **左侧侧边栏** | 任务列表 + 子代理进度常驻显示，可拖拽宽度，可折叠。运行中项带脉冲动画/进度条 | ✅ 已完成 | 🟢 P2 |
| U09 | **工具卡片截断** | 工具执行结果卡片限制 5 行，超出显示"···共N行"或"···共N字符" | ✅ 已完成 | 🟢 P3 |

### 3.4 AI 工具层（AI Tools）

| 编号 | 功能 | 描述 | 状态 | 优先级 |
|------|------|------|------|--------|
| T01 | `read_file` | 读取文件内容（支持 offset/limit） | ✅ 已完成 | — |
| T02 | `edit_file` | 精确替换文件内容 | ✅ 已完成 | — |
| T03 | `write_file` | 写入/创建文件 | ✅ 已完成 | — |
| T04 | `glob` | 按 glob 模式搜索文件 | ✅ 已完成 | — |
| T05 | `grep` | 正则搜索代码内容 | ✅ 已完成 | — |
| T06 | `shell` | Shell 命令执行（pwsh） | ✅ 已完成 | — |
| T07 | `webfetch` | Web 页面内容抓取 | ✅ 已完成 | 🟡 P1 |
| T07.5 | `read_skill` | 按需加载技能全文（on-demand skill loading） | ✅ 已完成 | 🟡 P1 |
| T08 | `git_diff` | 显示 Git 工作区变更 | ✅ 已完成 | 🟢 P2 |
| T09 | `git_log` | 显示 Git 提交历史 | ✅ 已完成 | 🟢 P2 |
| T10 | `git_commit` | 执行 Git 提交 | ✅ 已完成 | 🟢 P2 |
| T11 | `todo_write` | 创建/更新任务列表（完整替换模式），支持 pending/in_progress/completed/cancelled | ✅ 已完成 | 🟢 P2 |
| T12 | `task` | 启动子代理处理独立任务。支持 explore（只读）/ general（完整权限）两种模式，V4 Flash 模型，线程池并行执行 | ✅ 已完成 | 🟢 P2 |

---

## 四、优先级排序与开发计划

### P0 — 架构骨架 ✅ 已完成

```
1. A03 配置体系 — 多模型 / 参数 / 用户级 + 项目级配置合并
2. A01 事件总线 — 解耦 UI 与核心逻辑
3. A02 权限系统 — Allow / Deny / Ask 三级 + 工具白名单
4. F05 Slash 命令系统 — 核心命令框架 + /help /model /config
```

### P1 — 关键功能 ✅ 已完成

```
5. F06 会话持久化 — 保存/恢复/多会话
6. A04 上下文策略 — 滑动窗口 + 完整裁剪逻辑
7. F09 项目指令 — DEEPSEEK.md 自动注入上下文
8. F12 代码语法高亮 — ColorCode + 30+ 语言
9. F08 Web 抓取 — webfetch 工具（HtmlAgilityPack 正文提取）
10. U02 输入历史 — ↑↓ 翻历史
11. A06 Skills 系统 — Markdown + Frontmatter 技能文件自动加载
```

### P2 — 体验提升 ✅ 已完成

```
12. F07 Git 集成 ✅
13. F10 Todo 追踪 ✅
14. F11 Diff 预览 ✅
15. U01 状态栏 ✅
16. U03 进度指示 ✅
17. U04 右键菜单 ✅
18. U07 快捷键 ✅
19. A07 子代理系统 ✅
20. WebView2 渲染节流（50ms + requestAnimationFrame）✅
```

### P3 — 锦上添花

```
21. U06 系统托盘 ➖ 不做
22. 主题切换 ➖ 不做
23. Hooks 系统 ➖ 不做
24. 打包分发（单文件 63MB exe）✅ 已完成
```

---

## 五、当前项目结构

```
DeepSeekCode/
├── App.xaml / App.xaml.cs              # 应用入口，服务初始化 + DI 注册
├── MainWindow.xaml / MainWindow.xaml.cs # 主界面（对话、Plan模式、侧边栏、命令补全）
├── MainWindow.Conversation.cs          # 流式对话核心 + 工具管线 + 工具卡片渲染
├── MainWindow.Commands.cs              # 命令自动补全 + 历史会话 + 辅助 UI
├── MainWindow.Panels.cs                # 左侧侧边栏：Todo 面板 + 子代理面板 + 进度指示
├── SettingsWindow.xaml / SettingsWindow.xaml.cs # 五 Tab 设置（账户/模型/Thinking/参数/Beta）
├── NativeFolderPicker.cs               # Windows Shell API 原生文件夹选择器
├── Models/
│   ├── AppConfig.cs                    # 应用配置（API Key、模型、Thinking、Beta 等）
│   ├── ChatMessage.cs                  # 消息模型（含 reasoning_content、ToolCall、FunctionCall）
│   ├── CustomCommand.cs                # 自定义命令模型
│   ├── McpServerConfig.cs              # MCP 服务器配置
│   ├── ProjectConfig.cs                # 项目级配置
│   ├── StreamChunk.cs                  # SSE 流块解析（含 TokenUsage、Cache 命中）
│   ├── TodoItem.cs                     # 任务项模型（TodoStatus / TodoPriority 枚举）
│   └── ToolDefinition.cs               # 工具定义（OpenAI Function Calling 格式）
├── Services/
│   ├── ConfigService.cs                # 配置读写（DPAPI 加密）+ 自动迁移
│   ├── ContextStrategy.cs              # 上下文策略：SmartCompress + SlidingWindow + FileInjection
│   ├── ConversationManager.cs          # 对话管理 + 多 system 消息 + 额外上下文注入
│   ├── DeepSeekClient.cs               # API 客户端（SSE + Thinking + FIM + 运行中更新凭据）
│   ├── EventBus.cs                     # 全局事件总线（22+ 事件类型 + ConfigChanged.CredentialsChanged）
│   ├── Logger.cs                       # 文件日志 + 全局异常捕获（实现 MCP.ILogger）
│   ├── MemoryService.cs                # 跨会话 Memory（~/.deepseek-code/memory.md）
│   ├── PlanModeService.cs              # Plan 模式状态管理 + 提示词生成
│   ├── ProjectInstructionService.cs    # DEEPSEEK.md 项目指令读取注入
│   ├── SecureStorage.cs                # Windows DPAPI 敏感数据加密封装
│   ├── ServiceLocator.cs               # 轻量 DI 容器
│   ├── SubagentRunner.cs               # 子代理引擎（线程池 + Flash 模型）
│   ├── TimingService.cs                # 工具执行耗时追踪 + 本轮汇总
│   └── WorkspaceService.cs             # 工作区检测 + 切换 + 持久化
├── MCP/
│   ├── McpClient.cs                    # JSON-RPC 2.0 over stdio 客户端
│   └── McpService.cs                   # MCP 服务管理器（加载配置/连接/发现/注册工具）
├── Tools/
│   ├── ITool.cs                        # 工具接口
│   ├── ToolRegistry.cs                 # 工具注册 + 撤销 + 函数定义生成
│   ├── ToolPipeline.cs                 # 管线过滤器（权限/日志/超时）
│   ├── EnterPlanModeTool.cs            # 进入 Plan 模式（AI 工具）
│   ├── ExitPlanModeTool.cs             # 退出 Plan 模式（AI 工具）
│   ├── FileTools.cs                    # read_file / edit_file / write_file / glob / grep
│   ├── GitTools.cs                     # git_diff / git_log / git_commit（ArgumentList 安全传参）
│   ├── McpToolAdapter.cs               # MCP 工具 → ITool 适配器
│   ├── ReadSkillTool.cs                # read_skill 按需加载技能
│   ├── ShellTool.cs                    # Shell 命令执行（pwsh + 进程树强杀）
│   ├── TaskTool.cs                     # task 子代理启动工具
│   ├── TodoWriteTool.cs                # todo_write 任务列表管理
│   └── WebFetchTool.cs                 # webfetch 网页抓取（正文提取 + 内网拦截）
├── Commands/
│   ├── SlashCommands.cs                # ISlashCommand 接口 + CommandContext + SlashCommandRegistry
│   ├── BuiltInCommands.cs              # 13 个内置命令（含 /mcp /plan /workspace 等）
│   └── CustomCommandService.cs         # 用户自定义命令加载/注册
├── Permission/
│   └── PermissionManager.cs            # Allow/Deny/Ask 三级 + 13 条默认规则
├── Session/
│   └── SessionStore.cs                 # JSON 文件会话持久化（工作区物理隔离）
├── Skills/
│   └── SkillEngine.cs                  # Markdown + Frontmatter 技能引擎
├── Diff/
│   └── DiffRenderer.cs                 # LCS 行级 diff + 智能裁剪 + WPF 彩色渲染
├── Markdown/
│   └── SyntaxHighlighter.cs            # ColorCode + HtmlAgilityPack WPF 语法着色
├── UI/
│   ├── ChatRenderer.cs                 # WebView2 + marked.js/highlight.js/KaTeX 渲染
│   └── StatusViewModel.cs              # 状态栏 MVVM（Token/缓存/上下文占用/模式）
└── Resources/
    └── js/                             # 离线 JS 库（marked/highlight/KaTeX）
```

---

## 六、状态图例

| 标记 | 含义 |
|------|------|
| ❌ 待实现 | 尚未开始 |
| 🟡 半成品 | 基础可用，需完善 |
| ✅ 已完成 | 功能完整 |
| 🔴 P0 | 架构骨架，必须最先做 |
| 🟡 P1 | 关键功能，第二波 |
| 🟢 P2 | 体验提升，第三波 |
| 🟢 P3 | 锦上添花，远期 |
