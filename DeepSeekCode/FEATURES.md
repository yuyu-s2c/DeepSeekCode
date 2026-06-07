# DeepSeek Code — 功能规划与进度跟踪

> 对标 Claude Code，为目标架构提供路线图和实现状态跟踪。

---

## 一、项目概况

| 项 | 详情 |
|----|------|
| 技术栈 | C# + .NET 10 + WPF |
| 目标 | 100% 适配 DeepSeek V4 Pro 的本地 AI Agent |
| 核心优势 | Windows 原生桌面应用，零依赖分发 |

---

## 二、架构分层

```
┌─────────────────────────────────────────────────────┐
│                   UI 层 (WPF)                        │
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
| A02 | **权限系统** | Allow / Deny / Ask 三级权限。危险命令默认 Deny，可信任标记 Always。已接入 ToolPipeline | ✅ 已完成 | 🔴 P0 |
| A03 | **配置体系** | 用户级 `~/.deepseek-code/config.json` + 项目级 `.deepseek-code/project.json`。12 项配置、四 Tab 设置窗口 | ✅ 已完成 | 🔴 P0 |
| A04 | **上下文策略** | 滑动窗口 + 智能裁剪 + 系统提示词注入 + 项目文件引用。已接入 ConversationManager | ✅ 已完成 | 🟡 P1 |
| A05 | **工作区服务** | 自动检测项目根目录（.git/.sln/.csproj），点击切换 + /workspace 命令 | ✅ 已完成 | — |
| A06 | **Skills 系统** | Markdown 驱动技能文件，按需加载（仅注入索引，正文通过 read_skill 工具获取）。34 个 Claude Code 技能已迁移 | ✅ 已完成 | 🟡 P1 |
| A07 | **子代理 (Subagent)** | 并行分派独立任务（线程池 + Task.Run），独立 V4 Flash 模型，explore/general 双模式，带取消令牌和超时保护 | ✅ 已完成 | 🟢 P2 |

### 3.2 核心功能层（Features）

| 编号 | 功能 | 描述 | 状态 | 优先级 |
|------|------|------|------|--------|
| F01 | **DeepSeek API 流式对话** | 支持 chat 和 reasoner 模型，SSE 流式解析，thinking/reasoning_content 渲染 | ✅ 已完成 | — |
| F02 | **工具系统 (Function Calling)** | 13 个工具：read_file, edit_file, write_file, glob, grep, shell, webfetch, read_skill, git_diff, git_log, git_commit, todo_write, task | ✅ 已完成 | — |
| F03 | **Markdown 渲染** | Markdig → FlowDocument，支持标题、代码块、列表、引用、粗斜体、链接 | ✅ 已完成 | — |
| F04 | **Thinking 面板** | 右侧可折叠面板，实时展示 reasoning_content | ✅ 已完成 | — |
| F05 | **Slash 命令系统** | /help /clear /model /save /load /settings /workspace /config /compact /skills 共 10 个命令 | ✅ 已完成 | 🔴 P0 |
| F06 | **会话持久化** | 自动保存/恢复对话历史，/save /load 命令，JSON 文件存储 | ✅ 已完成 | 🟡 P1 |
| F13 | **DeepSeek Thinking 集成** | thinking 开关、reasoning_effort、reasoning_content 回传、参数屏蔽 | ✅ 已完成 | — |
| F07 | **Git 集成** | 自动 commit、diff 预览、log 查看 | ✅ 已完成 | 🟢 P2 |
| F08 | **Web 抓取 (webfetch)** | 抓取网页文档内容（Markdown/Text/HTML） | ✅ 已完成 | 🟡 P1 |
| F09 | **项目指令** | 自动读取项目根目录的 `DEEPSEEK.md` 并注入上下文 | ✅ 已完成 | 🟡 P1 |
| F10 | **Todo 追踪** | AI 自动创建/更新任务列表，可视化进度面板（进度条 + 状态图标 + 优先级标记） | ✅ 已完成 | 🟢 P2 |
| F11 | **代码 Diff 预览** | 文件编辑前后对比，LCS 行级 diff + 绿色(+)/红色(-) 渲染，支持 edit_file 和 write_file | ✅ 已完成 | 🟢 P2 |
| F12 | **代码语法高亮** | 对话区代码块语法着色（C#、JS、Python、Go 等 30+ 语言） | ✅ 已完成 | 🟡 P1 |

### 3.3 UI/UX 层（Experience）

| 编号 | 功能 | 描述 | 状态 | 优先级 |
|------|------|------|------|--------|
| U01 | **状态栏** | Token 消耗、缓存命中率、模型名称、会话状态 | ✅ 已完成 | 🟢 P2 |
| U01.5 | **工作区指示条** | 顶部常驻路径显示 + 点击弹出原生文件夹选择 | ✅ 已完成 | — |
| U02 | **输入历史** | ↑↓ 方向键翻历史消息 | ✅ 已完成 | 🟡 P1 |
| U03 | **进度指示** | 状态栏 spinner 动画（⣾⣽⣻⢿⡿⣟⣯⣷），AI 思考中/工具执行中/完成 三阶段状态切换 | ✅ 已完成 | 🟢 P2 |
| U04 | **消息右键菜单** | 用户消息气泡右键「复制消息」「重新发送」，非用户区右键「复制」选中文本 | ✅ 已完成 | 🟢 P2 |
| U05 | **命令自动补全** | 输入 `/` 弹出命令列表，↑↓ 选择，Enter/Tab 填入 | ✅ 已完成 | — |
| U06 | **系统托盘** | 最小化到托盘，常驻后台 | ❌ 待实现 | 🟢 P3 |
| U07 | **快捷键** | Ctrl+Enter 发送、Ctrl+L 清屏（Window.PreviewKeyDown 全局注册） | ✅ 已完成 | 🟢 P2 |
| U08 | **UI 全面重构** | 白底天蓝终端风格，消息全左对齐，工具卡片化带耗时追踪，子代理4状态动画，权限内联确认 | ✅ 已完成 | 🟢 P3 |

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
```

### P3 — 锦上添花（远期）

```
20. U06 系统托盘 ❌
21. U08 主题切换 ❌
22. 打包分发（single exe）❌
```

---

## 五、当前项目结构

```
DeepSeekCode/
├── App.xaml / App.xaml.cs              # 应用入口，服务初始化 + DI 注册
├── MainWindow.xaml / MainWindow.xaml.cs # 主界面（对话、Thinking、命令补全）
├── SettingsWindow.xaml / SettingsWindow.xaml.cs # 设置窗口（模型/Thinking/参数/Beta）
├── ApiKeyWindow.xaml / ApiKeyWindow.cs  # API Key 配置窗口
├── Models/
│   ├── AppConfig.cs                    # 应用配置（模型、Thinking、API 参数）
│   ├── ChatMessage.cs                  # 消息模型（含 reasoning_content、ToolCall）
│   ├── StreamChunk.cs                  # SSE 流块解析（含 TokenUsage）
│   ├── ToolDefinition.cs               # 工具定义（支持嵌套对象/数组 schema）
│   ├── TodoItem.cs                     # 任务项模型（TodoStatus / TodoPriority 枚举 + 自定义 JSON 转换器）
│   └── ProjectConfig.cs                # 项目级配置
├── Services/
│   ├── DeepSeekClient.cs               # API 客户端（流式 SSE + Thinking + FIM）
│   ├── ConversationManager.cs          # 对话管理 + 多 system 消息上下文裁剪
│   ├── EventBus.cs                     # 全局事件总线（22 个事件类型）
│   ├── ConfigService.cs                # 配置读写（用户级 + 项目级）
│   ├── ContextStrategy.cs              # 上下文策略（滑动窗口 + 智能压缩骨架）
│   ├── WorkspaceService.cs             # 工作区检测 + 切换
│   ├── ProjectInstructionService.cs    # DEEPSEEK.md 项目指令读取注入
│   ├── SubagentRunner.cs               # 子代理执行引擎（独立对话循环 + 线程池 + Flash 模型）
│   └── ServiceLocator.cs               # 轻量 DI 容器
├── Tools/
│   ├── ITool.cs                        # 工具接口
│   ├── ToolRegistry.cs                 # 工具注册 + 分发
│   ├── ToolPipeline.cs                 # 管线过滤器（权限/日志/超时）
│   ├── FileTools.cs                    # read_file / edit_file / write_file / glob / grep
│   ├── ShellTool.cs                    # Shell 命令执行（pwsh）
│   ├── WebFetchTool.cs                 # Web 页面抓取（HtmlAgilityPack 正文提取）
│   ├── GitTools.cs                     # git_diff / git_log / git_commit
│   ├── TodoWriteTool.cs                # todo_write 工具（创建/更新任务列表）
│   ├── TaskTool.cs                     # task 工具（启动子代理，线程池并行）
│   └── ReadSkillTool.cs                # read_skill 按需加载技能
├── Diff/
│   └── DiffRenderer.cs                 # LCS 行级 diff + 绿色(+)/红色(-) 渲染
├── Permission/
│   └── PermissionManager.cs            # Allow/Deny/Ask 三级权限
├── Commands/
│   ├── SlashCommands.cs                # 命令接口 + 注册中心 + CommandContext
│   └── BuiltInCommands.cs              # 10 个内置命令（/help /clear /model /save /load ...）
├── Session/
│   └── SessionStore.cs                 # 会话持久化（~\.deepseek-code\sessions\）
├── Skills/
│   └── SkillEngine.cs                  # Markdown+Frontmatter 技能引擎
├── Markdown/
│   ├── MarkdownRenderer.cs             # Markdig → WPF FlowDocument
│   └── SyntaxHighlighter.cs            # ColorCode + HtmlAgilityPack 语法高亮
├── UI/
│   └── StatusViewModel.cs              # 状态栏 MVVM（Token/缓存/模型）
├── Controls/
└── NativeFolderPicker.cs
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
