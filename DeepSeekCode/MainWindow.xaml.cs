using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using DeepSeekCode.Commands;
using DeepSeekCode.Models;
using DeepSeekCode.Services;
using DeepSeekCode.Tools;
using DeepSeekCode.UI;
using DeepSeekCode.Session;

namespace DeepSeekCode;

public partial class MainWindow : Window
{
    private readonly ServiceLocator _locator;
    private readonly EventBus _eventBus;
    private readonly ConfigService _configService;
    private readonly DeepSeekClient _client;
    private readonly ConversationManager _conversation;
    private readonly ToolRegistry _toolRegistry;
    private readonly PermissionManager _permissionManager;
    private readonly SlashCommandRegistry _commandRegistry;
    private readonly CommandContext _commandContext;
    private readonly ContextStrategyOrchestrator _contextOrchestrator;
    private readonly WorkspaceService _workspaceService;
    private readonly ProjectInstructionService _instructionService;
    private readonly Skills.SkillEngine _skillEngine;
    private readonly StatusViewModel _statusVm;
    private readonly TimingService _timingService;
    private readonly ChatRenderer _chatRenderer;
    private readonly ISessionStore _sessionStore;
    private readonly PlanModeService _planMode;
    private readonly MemoryService _memoryService;

    private CancellationTokenSource? _currentCancellation;
    private bool _isStreaming;
    /// <summary>Thinking 缓冲区（跨 partial 使用）</summary>
    public string _thinkingBuffer = "";
    private readonly StringBuilder _aiStreamBuffer = new();
    private Logger _logger = null!;

    // 输入历史
    private readonly List<string> _inputHistory = new();
    private int _historyIndex = -1;
    private string _savedInput = "";

    // 侧边栏状态
    private bool _sidebarVisible = true;
    private double _sidebarWidth = 260;

    // 工具卡片追踪
    private readonly HashSet<string> _activeToolCards = new();

    // 未保存变更追踪
    private bool _hasUnsavedChanges;

    // 每轮 while 迭代首次输出内容标记
    private bool _iterationFirstContent;

    // Thinking 面板状态
    private bool _thinkingActive;
    private DateTime _lastThinkChunk = DateTime.MinValue;

    public MainWindow(ServiceLocator locator)
    {
        InitializeComponent();

        _locator = locator;
        _eventBus = locator.Resolve<EventBus>();
        _configService = locator.Resolve<ConfigService>();
        _client = locator.Resolve<DeepSeekClient>();
        _conversation = locator.Resolve<ConversationManager>();
        _toolRegistry = locator.Resolve<ToolRegistry>();
        _permissionManager = locator.Resolve<PermissionManager>();
        _commandRegistry = locator.Resolve<SlashCommandRegistry>();
        _commandContext = locator.Resolve<CommandContext>();
        _contextOrchestrator = locator.Resolve<ContextStrategyOrchestrator>();
        _workspaceService = locator.Resolve<WorkspaceService>();
        _instructionService = locator.Resolve<ProjectInstructionService>();
        _skillEngine = locator.Resolve<Skills.SkillEngine>();
        _statusVm = locator.Resolve<StatusViewModel>();
        _timingService = locator.Resolve<TimingService>();
        _logger = locator.Resolve<Logger>();

        Title = $"DeepSeek Code - {_workspaceService.WorkspaceName}";
        WorkspaceLabel.Text = _workspaceService.WorkspacePath;

        _chatRenderer = new ChatRenderer(ChatViewer);
        SafeFireAndForget(_chatRenderer.InitializeAsync());

        _sessionStore = locator.Resolve<ISessionStore>();
        _planMode = locator.Resolve<PlanModeService>();
        _memoryService = locator.Resolve<MemoryService>();

        SubscribeToEvents();
        SetInitialSystemPrompt();
        _permissionManager.ResetSession();
        InitializeChat();
        UpdateStatusBar();
    }

    // ═══════════════════════════════════════════
    //  事件订阅
    // ═══════════════════════════════════════════

    private void SubscribeToEvents()
    {
        _eventBus.Subscribe<PermissionRequestEvent>(OnPermissionRequested);
        _eventBus.Subscribe<StreamErrorEvent>(e =>
            Dispatcher.Invoke(() =>
            {
                _logger.Error($"流式错误: {e.Message}");
                AppendSystemMessage($"错误: {e.Message}");
            }));
        _eventBus.Subscribe<StreamCancelledEvent>(_ =>
            Dispatcher.Invoke(() => AppendSystemMessage("已停止生成。")));
        _eventBus.Subscribe<ConfigChangedEvent>(e =>
            Dispatcher.Invoke(() =>
            {
                if (e.CredentialsChanged)
                {
                    _client.UpdateCredentials(_configService.Config.ApiKey, _configService.Config.ApiBaseUrl);
                    AppendSystemMessage("API 凭据已更新。");
                }
                if (e.Model != null)
                {
                    AppendSystemMessage($"模型已切换为: {e.Model}");
                    UpdateStatusBar();
                }
            }));
        _eventBus.Subscribe<UsageUpdatedEvent>(e =>
            Dispatcher.Invoke(() => UpdateTokenDisplay(e)));
        _eventBus.Subscribe<OpenSettingsRequestedEvent>(_ =>
            Dispatcher.Invoke(OpenSettingsWindow));
        _eventBus.Subscribe<WorkspaceChangedEvent>(e =>
            Dispatcher.Invoke(() =>
            {
                Title = $"DeepSeek Code - {Path.GetFileName(e.Path.TrimEnd(Path.DirectorySeparatorChar))}";
                WorkspaceLabel.Text = e.Path;
                _sessionStore.SetWorkspace(e.Path);
                _conversation.ClearConversation();
                _permissionManager.ResetSession();
                _ = _chatRenderer.ClearChat();
                AppendSystemMessage($"工作区已切换: {e.Path}");
                SetInitialSystemPrompt();
            }));
        _eventBus.Subscribe<TodosUpdatedEvent>(e =>
            Dispatcher.Invoke(() => UpdateTodoPanel(e.Todos)));
        _eventBus.Subscribe<SubagentStartedEvent>(e =>
            Dispatcher.BeginInvoke(() => OnSubagentStarted(e)));
        _eventBus.Subscribe<SubagentCompletedEvent>(e =>
            Dispatcher.BeginInvoke(() => OnSubagentCompleted(e)));
    }

    private async void OnPermissionRequested(PermissionRequestEvent e)
    {
        var toolName = e.ToolName ?? "";
        var command = e.Command ?? toolName;

        var decision = await Dispatcher.InvokeAsync(() =>
        {
            var dialog = new PermissionDialog(toolName, command) { Owner = this };
            dialog.ShowDialog();
            return dialog.Decision;
        });

        if (decision == PermissionDecision.Deny)
        {
            _currentCancellation?.Cancel();
            e.UserDecision = false;
        }
        else
        {
            e.UserDecision = true;
        }
    }

    // ═══════════════════════════════════════════
    //  系统提示词
    // ═══════════════════════════════════════════

    private void SetInitialSystemPrompt()
    {
        var ws = _workspaceService.WorkspacePath;
        var wsName = _workspaceService.WorkspaceName;

        var prompt = $"""
You are DeepSeek Code, a desktop AI coding assistant powered by DeepSeek V4 Pro.

You are an interactive agent that helps users with software engineering tasks.

IMPORTANT: Assist with authorized security testing, defensive security, CTF challenges, and educational contexts. Refuse requests for destructive techniques, DoS attacks, mass targeting, supply chain compromise, or detection evasion for malicious purposes. Dual-use security tools (code injection, process manipulation, network attacks) require clear authorization context: pentesting engagements, CTF competitions, security research, or defensive use cases.

# Harness
 - Text you output outside of tool use is displayed to the user as GitHub-flavored markdown in a WPF desktop application.
 - Tools run behind a three-button permission dialog (Deny / Allow Once / Allow All); a denied call means the user declined it — adjust, don't retry verbatim. A Deny cancels the current conversation turn entirely.
 - Prefer the dedicated file/search tools over shell commands when one fits. Independent tool calls can run in parallel in one response.
 - Reference code as `file_path:line_number` — it's clickable.

Write code that reads like the surrounding code: match its comment density, naming, and idiom.

For actions that are hard to reverse or outward-facing, confirm first unless durably authorized or explicitly told to proceed without asking; approval in one context doesn't extend to the next. Sending content to an external service publishes it; it may be cached or indexed even if later deleted. Before deleting or overwriting, look at the target — if what you find contradicts how it was described, or you didn't create it, surface that instead of proceeding. Report outcomes faithfully: if tests fail, say so with the output; if a step was skipped, say that; when something is done and verified, state it plainly without hedging.

# Session-specific guidance
 - When the user types `/<command-name>`, invoke it via the SlashCommand system. Only use commands listed in the available commands section — don't guess.
 - Available skills are listed in system context messages. Use the read_skill tool to load a skill's full instructions before relying on its guidance.

# Environment
You are running in the following environment:
 - Primary working directory: {ws}
 - Workspace: {wsName}
 - Is a git repository: true
 - Platform: win32
 - Shell: PowerShell 7+ (use PowerShell syntax — e.g., Test-Path not test, Remove-Item not rm)
 - Build: .NET 10 SDK, C# 13, MSBuild via Visual Studio at D:\VisualStudio\
 - Build command: `dotnet build` (target: 0 errors, 0 warnings)
  - The project is a WPF desktop application with WebView2 chat rendering, ServiceLocator DI, EventBus pub/sub, 13 tools, 10 slash commands.

# Context management
When the conversation grows beyond the context window limit, older messages are trimmed by a sliding window — the earliest conversation rounds are dropped while keeping system messages, recent messages, and the current task intact. Work can continue normally; you do not need to wrap up early or hand off mid-task. If you notice that earlier context may be missing, use read_file to re-check the current state of files rather than relying on memory of what was said.
""";

        _conversation.SetSystemPrompt(prompt);

        var instructionCtx = _instructionService.BuildInstructionContext();
        if (instructionCtx != null)
            _conversation.AppendSystemContext(instructionCtx);

        var skillsIndex = _skillEngine.GenerateSkillsIndex();
        if (!string.IsNullOrEmpty(skillsIndex))
            _conversation.AppendSystemContext(skillsIndex);

        var gitStatusContext = GetGitStatusContext();
        if (gitStatusContext != null)
            _conversation.AppendSystemContext(gitStatusContext);

        var memoryCtx = _memoryService.BuildMemoryContext();
        if (memoryCtx != null)
            _conversation.AppendSystemContext(memoryCtx);
    }

    private string? GetGitStatusContext()
    {
        var ws = _workspaceService.WorkspacePath;
        if (!Directory.Exists(Path.Combine(ws, ".git")))
            return null;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("gitStatus: This is the git status at the start of the conversation. Note that this status is a snapshot in time and will not update during the conversation.");
            sb.AppendLine();

            var branch = RunGitCommand(ws, "branch --show-current");
            if (!string.IsNullOrEmpty(branch))
                sb.AppendLine($"Current branch: {branch}");

            sb.AppendLine();

            var status = RunGitCommand(ws, "status --short");
            if (!string.IsNullOrEmpty(status))
            {
                var statusLines = status.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var truncated = statusLines.Take(20);
                sb.AppendLine("Status:");
                foreach (var line in truncated)
                    sb.AppendLine($" {line.TrimEnd()}");
                if (statusLines.Length > 20)
                    sb.AppendLine($" ...({statusLines.Length - 20} more files)");
            }
            else
            {
                sb.AppendLine("Status: (clean)");
            }

            sb.AppendLine();

            var log = RunGitCommand(ws, "log --oneline -3");
            if (!string.IsNullOrEmpty(log))
            {
                sb.AppendLine("Recent commits:");
                foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    sb.AppendLine($" {line.TrimEnd()}");
            }

            return sb.ToString().TrimEnd();
        }
        catch
        {
            return null;
        }
    }

    private static string? RunGitCommand(string workdir, string args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = args,
                    WorkingDirectory = workdir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    // ═══════════════════════════════════════════
    //  窗口级快捷键
    // ═══════════════════════════════════════════

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ClearScreen();
        }

        // Ctrl+B 切换侧边栏
        if (e.Key == Key.B && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ToggleSidebar();
        }
    }

    // ═══════════════════════════════════════════
    //  侧边栏切换
    // ═══════════════════════════════════════════

    private void SidebarToggle_Click(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar()
    {
        _sidebarVisible = !_sidebarVisible;

        if (_sidebarVisible)
        {
            SidebarColumn.MinWidth = 200;
            SidebarColumn.Width = new GridLength(_sidebarWidth);
            Sidebar.Visibility = Visibility.Visible;
            SidebarSplitter.Visibility = Visibility.Visible;
            SidebarToggleButton.Content = "☰";
            SidebarToggleButton.ToolTip = "隐藏侧边栏 (Ctrl+B)";
        }
        else
        {
            if (SidebarColumn.ActualWidth > 20)
                _sidebarWidth = SidebarColumn.ActualWidth;
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
            Sidebar.Visibility = Visibility.Collapsed;
            SidebarSplitter.Visibility = Visibility.Collapsed;
            SidebarToggleButton.Content = "▶";
            SidebarToggleButton.ToolTip = "显示侧边栏 (Ctrl+B)";
        }
    }

    private void SidebarSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (SidebarColumn.ActualWidth > 20)
            _sidebarWidth = SidebarColumn.ActualWidth;
    }

    private void InitializeChat()
    {
        _ = _chatRenderer.ClearChat();
        _ = _chatRenderer.AppendSystemMessage("DeepSeek Code 已就绪。输入 /help 获取帮助。");
    }

    // ═══════════════════════════════════════════
    //  输入处理
    // ═══════════════════════════════════════════

    private void SafeFireAndForget(Task task, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        task.ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                var msg = $"[MainWindow] {caller} 异步异常: {t.Exception.InnerException?.Message}";
                Debug.WriteLine(msg);
                _logger.Error(msg);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
        => await HandleInputAsync();

    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CommandPopup.IsOpen)
        {
            e.Handled = true;
            CommandPopup.IsOpen = false;
            return;
        }

        if (CommandPopup.IsOpen)
        {
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                e.Handled = true;
                if (e.Key == Key.Down)
                    CommandListBox.SelectedIndex = Math.Min(
                        CommandListBox.SelectedIndex + 1, CommandListBox.Items.Count - 1);
                else
                    CommandListBox.SelectedIndex = Math.Max(
                        CommandListBox.SelectedIndex - 1, 0);
                CommandListBox.ScrollIntoView(CommandListBox.SelectedItem);
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                e.Handled = true;
                SelectCommandFromList();
                return;
            }
        }

        // ── @ 文件引用补全键盘导航 ──
        if (e.Key == Key.Escape && FilePopup.IsOpen)
        {
            e.Handled = true;
            FilePopup.IsOpen = false;
            return;
        }

        if (FilePopup.IsOpen)
        {
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                e.Handled = true;
                if (e.Key == Key.Down)
                    FileListBox.SelectedIndex = Math.Min(
                        FileListBox.SelectedIndex + 1, FileListBox.Items.Count - 1);
                else
                    FileListBox.SelectedIndex = Math.Max(
                        FileListBox.SelectedIndex - 1, 0);
                FileListBox.ScrollIntoView(FileListBox.SelectedItem);
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                e.Handled = true;
                SelectFileFromList();
                return;
            }
        }

        if (e.Key == Key.Up || e.Key == Key.Down)
        {
            if (_inputHistory.Count == 0) return;

            if (_historyIndex == -1)
            {
                _savedInput = InputBox.Text;
            }

            if (e.Key == Key.Up)
            {
                if (_historyIndex < _inputHistory.Count - 1)
                    _historyIndex++;
            }
            else
            {
                if (_historyIndex > 0)
                    _historyIndex--;
                else if (_historyIndex == 0)
                {
                    _historyIndex = -1;
                    InputBox.Text = _savedInput;
                    InputBox.CaretIndex = InputBox.Text.Length;
                    e.Handled = true;
                    return;
                }
            }

            InputBox.Text = _inputHistory[^(1 + _historyIndex)];
            InputBox.CaretIndex = InputBox.Text.Length;
            e.Handled = true;
            return;
        }

        if (_historyIndex >= 0 && e.Key != Key.LeftShift && e.Key != Key.RightShift &&
            e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl &&
            e.Key != Key.LeftAlt && e.Key != Key.RightAlt)
        {
            _historyIndex = -1;
        }

        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await HandleInputAsync();
        }
    }

    private async Task HandleInputAsync()
    {
        if (_isStreaming) return;

        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (_inputHistory.Count == 0 || _inputHistory[^1] != text)
            _inputHistory.Add(text);
        _historyIndex = -1;

        InputBox.Text = "";
        SetInputEnabled(false);
        _isStreaming = true;
        _timingService.StartRound();
        _activeToolCards.Clear();
        StartStatusSpinner("AI 正在思考…");
        _thinkingBuffer = "";
        _thinkingActive = false;
        _lastThinkChunk = DateTime.MinValue;

        if (text.StartsWith('/'))
        {
            var result = await _commandRegistry.ExecuteAsync(text, _commandContext);
            if (result?.Handled == true)
            {
                // 自定义命令：用生成的 prompt 替代原始输入，继续发给 AI
                if (result.UserPrompt != null)
                {
                    text = result.UserPrompt;
                    if (result.DisplayMessage != null)
                        AppendSystemMessage(result.DisplayMessage);
                    // 继续往下走，当作普通消息发送
                }
                else
                {
                    if (result.DisplayMessage != null)
                        AppendSystemMessage(result.DisplayMessage);
                    if (result.RefreshUI)
                    {
                        _ = _chatRenderer.ClearChat();
                        InitializeChat();
                    }
                    if (text.Trim() == "/clear" || text.Trim().StartsWith("/save"))
                        _hasUnsavedChanges = false;
                    if (text.Trim() == "/clear")
                    {
                        _commandContext.CurrentSessionId = null;
                        _permissionManager.ResetSession();
                    }
                    SyncPlanMode();  // 命令可能改变了 Plan 模式状态
                    FinishStreaming();
                    return;
                }
            }
        }

        _hasUnsavedChanges = true;

        // 同步 Plan 模式：注入提示词 + 更新 UI 指示器
        SyncPlanMode();

        AppendUserMessage(text);
        _conversation.AddUserMessage(text);

        _currentCancellation = new CancellationTokenSource();

        try
        {
            await StreamConversationAsync(_currentCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            _eventBus.Publish(new StreamCancelledEvent());
        }
        catch (Exception ex)
        {
            _eventBus.Publish(new StreamErrorEvent { Message = ex.Message, Exception = ex });
        }
        finally
        {
            FinishStreaming();
        }
    }

    // ═══════════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════════

    private void ScrollChatToEnd() { }

    private void SetInputEnabled(bool enabled)
    {
        InputBox.IsEnabled = enabled;
        SendButton.IsEnabled = enabled;
    }

    private static string GetToolIcon(string toolName) => toolName switch
    {
        "read_file" => "📖",
        "edit_file" => "✏",
        "write_file" => "📝",
        "glob" => "⚙",
        "grep" => "🔍",
        "shell" => "⚡",
        "webfetch" => "🌐",
        "git_diff" => "📋",
        "git_log" => "📜",
        "git_commit" => "✅",
        "todo_write" => "📋",
        "task" => "🤖",
        "read_skill" => "📚",
        _ => "🔧"
    };

    private static string GetToolParamSummary(string toolName, Dictionary<string, object?> args)
    {
        return toolName switch
        {
            "read_file" or "edit_file" or "write_file" =>
                args.TryGetValue("filePath", out var fp) ? fp?.ToString() ?? "" : "",
            "glob" or "grep" =>
                args.TryGetValue("pattern", out var p) ? p?.ToString() ?? "" : "",
            "shell" =>
                args.TryGetValue("command", out var cmd) ? TruncateParam(cmd?.ToString(), 60) : "",
            "webfetch" =>
                args.TryGetValue("url", out var url) ? TruncateParam(url?.ToString(), 50) : "",
            "git_diff" => "--staged",
            "git_log" => "-5",
            "git_commit" =>
                args.TryGetValue("message", out var msg) ? TruncateParam(msg?.ToString(), 40) : "",
            "task" =>
                args.TryGetValue("description", out var desc) ? TruncateParam(desc?.ToString(), 40) : "",
            "read_skill" =>
                args.TryGetValue("name", out var sn) ? sn?.ToString() ?? "" : "",
            _ => ""
        };
    }

    private static string TruncateLines(string text, int maxLines)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var lines = text.Split('\n');
        if (lines.Length <= maxLines) return text.Length > 600 ? text[..600] + $"\n··· 共 {text.Length} 字符" : text;
        return string.Join('\n', lines.Take(maxLines)) + $"\n··· 共 {lines.Length} 行";
    }

    private static string TruncateParam(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLen ? text : text[..maxLen] + "...";
    }

    // ═══════════════════════════════════════════
    //  流式结束与状态
    // ═══════════════════════════════════════════

    private void FinishStreaming()
    {
        _isStreaming = false;
        StopStatusSpinner();
        StatusTimingLabel.Text = _timingService.GetRoundSummary();
        SetInputEnabled(true);
        _currentCancellation = null;
        InputBox.Focus();
    }

    private void UpdateStatusBar()
    {
        _statusVm.Model = _configService.Config.Model;
        StatusModelLabel.Text = _statusVm.Model;
        StatusTokenLabel.Text = _statusVm.TokenDisplay;
        UpdatePlanIndicator();
    }

    private void SyncPlanMode()
    {
        if (_planMode.IsActive)
            _conversation.SetExtraSystemContext(_planMode.GenerateSystemPrompt());
        else
            _conversation.SetExtraSystemContext(null);
        UpdatePlanIndicator();
    }

    private void UpdatePlanIndicator()
    {
        if (_planMode.IsActive)
        {
            PlanStatusLabel.Text = "📋 Plan 模式";
            PlanStatusLabel.Foreground = FindResource("WarningOrangeBrush") as System.Windows.Media.Brush;
        }
        else
        {
            PlanStatusLabel.Text = "🤖 自动模式";
            PlanStatusLabel.Foreground = FindResource("PrimaryLightBrush") as System.Windows.Media.Brush;
        }
        PlanStatusLabel.Visibility = Visibility.Visible;
    }

    private void UpdateTokenDisplay(UsageUpdatedEvent e)
    {
        _statusVm.TokenCount = e.TotalTokens;
        _statusVm.CacheHitTokens = e.CacheHitTokens;
        _statusVm.CacheMissTokens = e.CacheMissTokens;
        _statusVm.ReasoningTokens = e.ReasoningTokens;
        _statusVm.ContextTokens = e.PromptTokens;

        StatusTokenLabel.Text = _statusVm.TokenDisplay;
        StatusContextLabel.Text = _statusVm.ContextDisplay;

        if (e.CacheHitTokens > 0)
        {
            var hitRatio = e.PromptTokens > 0
                ? (double)e.CacheHitTokens / e.PromptTokens * 100
                : 0;
            StatusCacheLabel.Text = $"缓存命中率: {hitRatio:F0}% ({e.CacheHitTokens:N0} tokens)";
        }
        else
        {
            StatusCacheLabel.Text = "";
        }
    }

    // ═══════════════════════════════════════════
    //  窗口关闭
    // ═══════════════════════════════════════════

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_hasUnsavedChanges && _conversation.Messages.Count > 1)
        {
            var result = MessageBox.Show(
                "当前对话尚未保存，是否在退出前保存？",
                "DeepSeek Code",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                var sessionId = _commandContext.CurrentSessionId ?? Guid.NewGuid().ToString("N")[..8];
                var title = GenerateSessionTitle();
                var metadata = new SessionMetadata
                {
                    Id = sessionId,
                    Title = title,
                    MessageCount = _conversation.Messages.Count,
                    Model = _configService.Config.Model
                };
                _commandContext.CurrentSessionId = sessionId;
                var messagesJson = _conversation.SerializeSession();
                _sessionStore.SaveAsync(metadata, messagesJson).GetAwaiter().GetResult();
            }
        }

        base.OnClosing(e);
    }

    private string GenerateSessionTitle()
    {
        var firstUserMsg = _conversation.Messages
            .FirstOrDefault(m => m.Role == "user")?.Content;

        if (string.IsNullOrWhiteSpace(firstUserMsg))
        {
            var workspaceName = Path.GetFileName(_workspaceService.WorkspacePath.TrimEnd(Path.DirectorySeparatorChar));
            return $"{workspaceName}_{DateTime.Now:MMdd_HHmm}";
        }

        var title = firstUserMsg.Length > 30
            ? firstUserMsg[..30] + "..."
            : firstUserMsg;

        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();
        return title;
    }
}
