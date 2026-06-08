using System.IO;
using System.Windows;

using DeepSeekCode.Commands;
using DeepSeekCode.Commands.BuiltIn;
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

        if (string.IsNullOrWhiteSpace(configService.Config.ApiKey))
        {
            var apiKeyWindow = new ApiKeyWindow(configService);
            if (apiKeyWindow.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        // ── 2. 初始化服务容器 ──
        var locator = new ServiceLocator();

        // 基础设施
        var eventBus = new EventBus();
        locator.RegisterInstance(eventBus);

        locator.RegisterInstance(configService);

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
            workspaceService, configService, eventBus);
        toolRegistry.Register(new TaskTool(subagentRunner));
        locator.RegisterInstance(subagentRunner);

        locator.RegisterInstance(toolRegistry);

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
            TokenEstimator = async text => await deepSeekClient.EstimateTokenCount(text)
        };
        var contextOrchestrator = new ContextStrategyOrchestrator(contextOptions)
            .AddStrategy(new SlidingWindowStrategy());
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
            SkillEngine = skillEngine
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
        locator.RegisterInstance(commandRegistry);
        locator.RegisterInstance(commandContext);

        // 自动加载技能
        var projectSkillsDir = Path.Combine(workspaceService.WorkspacePath, ".deepseek-code", "skills");
        skillEngine.LoadFromDirectory(projectSkillsDir, projectLevel: true);

        var userSkillsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".deepseek-code", "skills");
        skillEngine.LoadFromDirectory(userSkillsDir, projectLevel: false);

        // ── 7. 状态栏 ViewModel ──
        var statusVm = new StatusViewModel { Model = configService.Config.Model };
        locator.RegisterInstance(statusVm);

        // ── 8. 启动主窗口 ──
        var mainWindow = new MainWindow(locator);
        mainWindow.Show();
    }
}
