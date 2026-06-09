using System.IO;
using System.Threading.Tasks;
using System.Windows;

using DeepSeekCode.Commands;
using DeepSeekCode.Commands.BuiltIn;
using DeepSeekCode.MCP;
using DeepSeekCode.Services;
using DeepSeekCode.Session;
using DeepSeekCode.Skills;
using DeepSeekCode.Tools;
using DeepSeekCode.UI;

namespace DeepSeekCode;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── 1. 配置服务 ──
        var configService = new ConfigService();

        // ── 2. 初始化服务容器 ──
        var locator = new ServiceLocator();

        // 基础设施
        var eventBus = new EventBus();
        locator.RegisterInstance(eventBus);

        locator.RegisterInstance(configService);

        // 日志系统
        var logger = new Logger();
        locator.RegisterInstance(logger);
        CrashHandler.Initialize(logger);
        logger.Info($"DeepSeek Code 启动");

        // 工作区
        var workspaceService = new WorkspaceService(configService, eventBus);
        locator.RegisterInstance(workspaceService);

        // 耗时追踪
        var timingService = new TimingService();
        locator.RegisterInstance(timingService);

        // DeepSeek 客户端（依赖配置）
        var deepSeekClient = new DeepSeekClient(configService.Config);
        locator.RegisterInstance(deepSeekClient);

        // ── 3. 权限系统 ──
        var permissionManager = new PermissionManager();
        locator.RegisterInstance(permissionManager);

        // ── 4. 工具系统 ──
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new FileReadTool());
        toolRegistry.Register(new FileEditTool());
        toolRegistry.Register(new FileWriteTool());
        toolRegistry.Register(new GlobTool());
        toolRegistry.Register(new GrepTool());
        toolRegistry.Register(new ShellTool());
        toolRegistry.Register(new WebFetchTool());
        toolRegistry.Register(new GitDiffTool());
        toolRegistry.Register(new GitLogTool());
        toolRegistry.Register(new GitCommitTool());
        toolRegistry.Register(new TodoWriteTool(eventBus));

        // 子代理系统
        var subagentRunner = new SubagentRunner(
            deepSeekClient, toolRegistry, permissionManager,
            workspaceService, configService, eventBus, logger);
        toolRegistry.Register(new TaskTool(subagentRunner));
        locator.RegisterInstance(subagentRunner);

        locator.RegisterInstance(toolRegistry);

        // ── 4.5 MCP 服务（提前创建供 CommandContext 引用） ──
        var mcpService = new McpService(toolRegistry, logger);
        locator.RegisterInstance(mcpService);

        // ── 4.6 Plan 模式服务 ──
        var planModeService = new PlanModeService(workspaceService);
        locator.RegisterInstance(planModeService);
        toolRegistry.Register(new EnterPlanModeTool(planModeService));
        toolRegistry.Register(new ExitPlanModeTool(planModeService));

        // ── 4.7 Memory 系统 ──
        MemoryService.EnsureExists();
        var memoryService = new MemoryService();
        locator.RegisterInstance(memoryService);

        // ── 5. 对话管理 ──
        var conversationManager = new ConversationManager(deepSeekClient);
        locator.RegisterInstance(conversationManager);

        // 项目指令
        var instructionService = new ProjectInstructionService(workspaceService);
        locator.RegisterInstance(instructionService);

        // 技能引擎（提前创建，CommandContext 需要引用）
        var skillEngine = new SkillEngine();
        locator.RegisterInstance(skillEngine);

        // read_skill 工具（依赖 SkillEngine，注册在此处）
        toolRegistry.Register(new ReadSkillTool(skillEngine));

        // 上下文策略（V4 Pro/Flash 均 1M 上下文窗口）
        var contextOptions = new ContextStrategyOptions
        {
            MaxTokens = 900_000,  // 1M 窗口留 100K 给输出 + 工具定义
            TokenEstimator = async text => await deepSeekClient.EstimateTokenCount(text),
            ProjectRoot = workspaceService.WorkspacePath,
            Summarizer = async text =>
            {
                var summaryMessages = new List<Models.ChatMessage>
                {
                    Models.ChatMessage.CreateSystem("You are a conversation summarizer. Produce a concise summary that captures all critical information: key decisions, files modified, steps completed, and pending items. Output only the summary, no preamble."),
                    Models.ChatMessage.CreateUser(text)
                };
                var summaryConfig = new Models.AppConfig
                {
                    Model = "deepseek-v4-flash",
                    MaxTokens = 1024,
                    ThinkingEnabled = false
                };
                var result = "";
                await foreach (var chunk in deepSeekClient.StreamChatAsync(
                    new(), summaryMessages, summaryConfig))
                {
                    if (chunk.Choices?.Count > 0)
                    {
                        var delta = chunk.Choices[0].Delta;
                        if (delta?.Content != null)
                            result += delta.Content;
                    }
                }
                return result.Trim();
            }
        };
        var contextOrchestrator = new ContextStrategyOrchestrator(contextOptions)
            .AddStrategy(new SmartCompressStrategy())
            .AddStrategy(new SlidingWindowStrategy())
            .AddStrategy(new FileInjectionStrategy());
        locator.RegisterInstance(contextOrchestrator);

        // 注入到 ConversationManager
        conversationManager.SetContextStrategy(contextOrchestrator);

        // ── 6. Slash 命令系统 ──
        var sessionStore = new FileSessionStore();
        sessionStore.SetWorkspace(workspaceService.WorkspacePath);
        locator.RegisterInstance<ISessionStore>(sessionStore);

        var commandRegistry = new SlashCommandRegistry();

        var commandContext = new CommandContext
        {
            Conversation = conversationManager,
            Config = configService,
            EventBus = eventBus,
            SessionStore = sessionStore,
            CommandRegistry = commandRegistry,
            WorkspaceService = workspaceService,
            SkillEngine = skillEngine,
            DeepSeekClient = deepSeekClient,
            McpService = mcpService,
            PlanMode = planModeService
        };

        commandRegistry.Register(new HelpCommand());
        commandRegistry.Register(new ClearCommand());
        commandRegistry.Register(new ModelCommand());
        commandRegistry.Register(new SaveCommand());
        commandRegistry.Register(new LoadCommand());
        commandRegistry.Register(new ConfigCommand());
        commandRegistry.Register(new CompactCommand());
        commandRegistry.Register(new SettingsCommand());
        commandRegistry.Register(new WorkspaceCommand());
        commandRegistry.Register(new SkillsCommand());
        commandRegistry.Register(new McpCommand());
        commandRegistry.Register(new PlanCommand());
        locator.RegisterInstance(commandRegistry);
        locator.RegisterInstance(commandContext);

        // 自动加载技能
        var projectSkillsDir = Path.Combine(workspaceService.WorkspacePath, ".deepseek-code", "skills");
        skillEngine.LoadFromDirectory(projectSkillsDir, projectLevel: true);

        var userSkillsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".deepseek-code", "skills");
        skillEngine.LoadFromDirectory(userSkillsDir, projectLevel: false);

        // ── 6.5 自定义命令 ──
        EnsureDefaultCustomCommands();
        var customCommandService = new CustomCommandService(commandRegistry);
        customCommandService.LoadAndRegister();
        locator.RegisterInstance(customCommandService);
        logger.Info("已加载自定义命令");

        // ── 6.6 MCP 服务（后台异步启动） ──
        Task.Run(async () =>
        {
            try
            {
                EnsureDefaultMcpConfig();
                await mcpService.StartAllAsync();
            }
            catch (Exception ex)
            {
                logger.Warn($"MCP 服务启动异常: {ex.Message}");
            }
        });

        // ── 7. 状态栏 ViewModel ──
        var statusVm = new StatusViewModel { Model = configService.Config.Model };
        locator.RegisterInstance(statusVm);

        // ── 8. 启动主窗口 ──
        var mainWindow = new MainWindow(locator);
        mainWindow.Show();
    }

    // ═══════════════════════════════════════════
    //  默认配置初始化
    // ═══════════════════════════════════════════

    private static void EnsureDefaultMcpConfig()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".deepseek-code", "mcp-servers.json");

        if (File.Exists(configPath)) return;

        var defaultConfig = new Models.McpConfig
        {
            Servers = new()
        };

        McpService.SaveConfig(defaultConfig);
    }

    private static void EnsureDefaultCustomCommands()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".deepseek-code", "custom-commands.json");

        if (File.Exists(configPath)) return;

        var defaultConfig = new Models.CustomCommandConfig
        {
            Commands = new()
            {
                new Models.CustomCommand
                {
                    Name = "explain",
                    Description = "解释选中的代码或概念",
                    Prompt = "请详细解释以下内容，用通俗易懂的语言说明：\n\n{args}"
                },
                new Models.CustomCommand
                {
                    Name = "review",
                    Description = "代码审查：检查 Bug、性能问题和改进建议",
                    Prompt = "请审查以下代码，识别潜在的 Bug、性能问题、安全漏洞，并给出改进建议：\n\n{args}"
                },
                new Models.CustomCommand
                {
                    Name = "refactor",
                    Description = "重构指定代码，保持功能不变",
                    Prompt = "请重构以下代码，保持功能完全一致，但提高可读性、可维护性和性能。重构后给出逐条说明：\n\n{args}"
                }
            }
        };

        CustomCommandService.SaveConfig(defaultConfig);
    }
}
