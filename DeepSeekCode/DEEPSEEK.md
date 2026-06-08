# 项目技术栈

- C# 13 + .NET 10 + WPF 桌面 AI Agent 应用
- WebView2 + marked.js + highlight.js + KaTeX 渲染聊天
- 轻量 DI（ServiceLocator）+ 事件总线（EventBus）解耦
- 13 个工具 + 10 个 Slash 命令
- 子代理线程池并行（SubagentRunner）
- 900K token 上下文滑动窗口
- reasoning_effort 默认 max

# 构建与验证

- 构建命令：`dotnet build`（目标 0 Error 0 Warning）
- 项目文件：`D:\DeepSeek CLI\DeepSeekCode\DeepSeekCode.csproj`
- 解决方案：`D:\DeepSeek CLI\DeepSeekCode\DeepSeekCode.sln`

# 项目规范

## 文件组织

- 领域代码位于 `Services/`：DeepSeekClient、ConversationManager、SubagentRunner、ConfigService 等
- 工具代码位于 `Tools/`：ITool 接口 + ToolRegistry + ToolPipeline + 各具体工具
- 命令代码位于 `Commands/`：ISlashCommand 接口 + SlashCommandRegistry + BuiltInCommands
- 模型代码位于 `Models/`：ChatMessage、StreamChunk、AppConfig、ToolDefinition 等
- UI 代码位于 `UI/`：ChatRenderer、StatusViewModel
- 权限代码位于 `Permission/`：PermissionManager、PermissionDialog
- 会话存储位于 `Session/`：SessionStore
- 技能引擎位于 `Skills/`：SkillEngine
- Markdown 渲染位于 `Markdown/`：MarkdownRenderer、SyntaxHighlighter
- Diff 渲染位于 `Diff/`：DiffRenderer

## 架构约束

- DI 注册集中在 `App.xaml.cs` 的 `OnStartup()` 方法中
- 工具执行通过 `ToolPipeline` 过滤器链（Permission → Logging → Timeout）
- 会话存储通过 `ISessionStore` 接口访问，实现为 `FileSessionStore`
- 配置读写通过 `ConfigService` 统一入口
- 跨模块通信使用 `EventBus` 发布/订阅，禁止直接调用 UI 方法
- 所有工具必须实现 `ITool` 接口并在 `App.xaml.cs` 中注册
- 所有命令必须实现 `ISlashCommand` 接口并在 `App.xaml.cs` 中注册

## 命名约定

- 公共 API 使用 XML 文档注释（中文）
- 私有字段使用 `_` 前缀
- 命名空间统一为 `DeepSeekCode.*`

## 安全约束

- 任何写操作必须经过 `PermissionManager` 检查
- 危险命令（rm/del/format）在权限管理器中设为 Deny
- 子代理模式下 Allow/Ask 自动通过，Deny 始终拦截
- 禁止在代码或提交中暴露 API Key 和密钥

## 不要做的事情

- 不要 git push --force
- 不要修改 App.xaml.cs 中的 DI 注册顺序（有依赖关系）
- 不要在非 UI 线程直接操作 WPF 控件
- 不要使用 innerHTML 拼接用户输入（使用 createElement + appendChild）
