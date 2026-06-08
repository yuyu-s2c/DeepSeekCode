using System;
using System.Collections.Generic;
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

    private CancellationTokenSource? _currentCancellation;
    private bool _isStreaming;
    private string _thinkingBuffer = "";
    private readonly StringBuilder _aiStreamBuffer = new();  // 流式累积全文，逐块实时渲染

    // 输入历史
    private readonly List<string> _inputHistory = new();
    private int _historyIndex = -1;
    private string _savedInput = "";

    // 工具卡片追踪（仅用于耗时更新）
    private readonly HashSet<string> _activeToolCards = new();

    // 未保存变更追踪
    private bool _hasUnsavedChanges;

    // 每轮 while 迭代首次输出内容标记
    private bool _iterationFirstContent;

    // 进度指示器
    private DispatcherTimer? _spinnerTimer;
    private int _spinnerIndex;
    private static readonly string[] SpinnerFrames = ["⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷"];
    private string _toolProgressText = "";

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

        Title = $"DeepSeek Code - {_workspaceService.WorkspaceName}";
        WorkspaceLabel.Text = _workspaceService.WorkspacePath;

        _chatRenderer = new ChatRenderer(ChatViewer);
        _ = _chatRenderer.InitializeAsync(); // fire-and-forget，WebView2 初始化需要一点时间

        _sessionStore = locator.Resolve<ISessionStore>();

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
            Dispatcher.Invoke(() => AppendSystemMessage($"错误: {e.Message}")));
        _eventBus.Subscribe<StreamCancelledEvent>(_ =>
            Dispatcher.Invoke(() => AppendSystemMessage("已停止生成。")));
        _eventBus.Subscribe<ConfigChangedEvent>(e =>
            Dispatcher.Invoke(() =>
            {
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
            // 拒绝 → 取消本轮对话
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
        // ═══ 短系统提示词 — 仅核心行为规则 ═══
        var prompt = $@"你是 DeepSeek Code。工作区: {_workspaceService.WorkspacePath}

规则：了解代码可以用工具探索，修改代码需用户明确授权。查代码用 grep/read_file 给出准确回答，不猜测。用户消息是唯一任务来源，不自行创造需求。回复简洁直接。";

        _conversation.SetSystemPrompt(prompt);

        // ═══ 项目指令 — 独立 system 消息（参考 Claude Code 的 CLAUDE.md 模式） ═══
        var instructionCtx = _instructionService.BuildInstructionContext();
        if (instructionCtx != null)
            _conversation.AppendSystemContext(instructionCtx);

        // ═══ 技能索引 — 仅名称+描述（正文通过 read_skill 按需加载） ═══
        var skillsIndex = _skillEngine.GenerateSkillsIndex();
        if (!string.IsNullOrEmpty(skillsIndex))
            _conversation.AppendSystemContext(skillsIndex);
    }

    // ═══════════════════════════════════════════
    //  窗口级快捷键
    // ═══════════════════════════════════════════

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+L → 清屏
        if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ClearScreen();
        }
    }

    private void InitializeChat()
    {
        _ = _chatRenderer.ClearChat();
        _ = _chatRenderer.AppendSystemMessage("DeepSeek Code 已就绪。输入 /help 获取帮助。");
    }

    // ═══════════════════════════════════════════
    //  输入处理
    // ═══════════════════════════════════════════

    private async void SendButton_Click(object sender, RoutedEventArgs e)
        => await HandleInputAsync();

    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Escape → 关闭命令列表
        if (e.Key == Key.Escape && CommandPopup.IsOpen)
        {
            e.Handled = true;
            CommandPopup.IsOpen = false;
            return;
        }

        // 命令列表打开时：上下键导航，Tab/Enter 选中
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

        // 输入历史：↑↓ 翻历史消息
        if (e.Key == Key.Up || e.Key == Key.Down)
        {
            if (_inputHistory.Count == 0) return;

            // 首次进入历史模式，保存当前输入
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
                    // 回到最初：恢复进入历史模式前的输入
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

        // 任何其他按键：退出历史模式
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

        // 记录到输入历史（去重：与最近一条相同则不重复添加）
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

        // 1. 先尝试 Slash 命令
        if (text.StartsWith('/'))
        {
            var result = await _commandRegistry.ExecuteAsync(text, _commandContext);
            if (result?.Handled == true)
            {
                if (result.DisplayMessage != null)
                    AppendSystemMessage(result.DisplayMessage);
                if (result.RefreshUI)
                {
                    _ = _chatRenderer.ClearChat();
                    InitializeChat();
                }
                // /save 或 /clear 后标记已保存
                if (text.Trim() == "/clear" || text.Trim().StartsWith("/save"))
                    _hasUnsavedChanges = false;
                if (text.Trim() == "/clear")
                {
                    _commandContext.CurrentSessionId = null;
                    _permissionManager.ResetSession();
                }
                FinishStreaming();
                return;
            }
        }

        // 2. 发送给 AI
        _hasUnsavedChanges = true;
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
    //  流式对话核心
    // ═══════════════════════════════════════════

    private async Task StreamConversationAsync(CancellationToken ct)
    {
        var tools = _toolRegistry.GetDefinitions();

        while (true)
        {
            // 每次 AI 迭代独立一张思考卡片 + 内容块
            _thinkingBuffer = "";
            _thinkingActive = false;
            _lastThinkChunk = DateTime.MinValue;
            _iterationFirstContent = true;

            // 上下文裁剪
            var messages = await _conversation.GetProcessedMessagesAsync();

            var contentBuffer = new StringBuilder();
            var toolCallAccumulators =
                new Dictionary<int, (string id, string name, StringBuilder args)>();

            _eventBus.Publish(new StreamStartedEvent { UserMessage = messages.LastOrDefault()?.Content });

            await foreach (var chunk in _client.StreamChatAsync(
                tools, messages, _configService.Config, ct))
            {
                // 捕获 token 用量（最后一个 chunk 可能包含 usage）
                if (chunk.Usage != null)
                {
                    _eventBus.Publish(new UsageUpdatedEvent
                    {
                        PromptTokens = chunk.Usage.PromptTokens,
                        CompletionTokens = chunk.Usage.CompletionTokens,
                        TotalTokens = chunk.Usage.TotalTokens,
                        CacheHitTokens = chunk.Usage.PromptCacheHitTokens,
                        CacheMissTokens = chunk.Usage.PromptCacheMissTokens,
                        ReasoningTokens = chunk.Usage.CompletionTokensDetails?.ReasoningTokens ?? 0
                    });
                }

                var delta = chunk.Choices![0].Delta;
                if (delta == null) continue;

                // 思考内容 → 事件 + UI
                if (!string.IsNullOrEmpty(delta.ReasoningContent))
                {
                    _thinkingBuffer += delta.ReasoningContent;
                    _eventBus.Publish(new StreamChunkEvent { ReasoningContent = delta.ReasoningContent });
                    Dispatcher.Invoke(() => UpdateThinkingPanel());
                }

                // 普通文本 → 事件 + UI
                if (!string.IsNullOrEmpty(delta.Content))
                {
                    contentBuffer.Append(delta.Content);
                    _eventBus.Publish(new StreamChunkEvent { Content = delta.Content });
                    Dispatcher.Invoke(() => AppendStreamText(delta.Content));
                }

                // 工具调用 → 累积
                if (delta.ToolCalls != null)
                {
                    foreach (var tc in delta.ToolCalls)
                    {
                        if (!toolCallAccumulators.ContainsKey(tc.Index))
                        {
                            toolCallAccumulators[tc.Index] = (
                                id: tc.Id ?? "",
                                name: tc.Function?.Name ?? "",
                                args: new StringBuilder()
                            );
                        }
                        var acc = toolCallAccumulators[tc.Index];
                        if (!string.IsNullOrEmpty(tc.Id)) acc.id = tc.Id;
                        if (!string.IsNullOrEmpty(tc.Function?.Name)) acc.name = tc.Function.Name;
                        if (tc.Function?.Arguments != null) acc.args.Append(tc.Function.Arguments);
                    }
                }
            }

            FlushCurrentAiParagraph();

            // 本次迭代思考流式完成 → 折叠卡片
            _ = _chatRenderer.CollapseThinkingCard();

            _eventBus.Publish(new StreamCompletedEvent
            {
                FullResponse = contentBuffer.ToString(),
                FullReasoning = _thinkingBuffer
            });

            // 有工具调用 → 权限检查 + 管线执行 + 循环
            if (toolCallAccumulators.Count > 0)
            {
                var toolCalls = toolCallAccumulators.Select(kvp =>
                {
                    var (id, name, args) = kvp.Value;
                    return new ToolCall
                    {
                        Id = id,
                        Type = "function",
                        Function = new FunctionCall { Name = name, Arguments = args.ToString() }
                    };
                }).ToList();

                _conversation.AddAssistantMessageWithToolCalls(toolCalls, _thinkingBuffer);
                Dispatcher.Invoke(() =>
                {
                    _thinkingActive = false;
                    _ = _chatRenderer.CollapseThinkingCard();
                    AppendToolCards(toolCalls);
                    UpdateSpinnerText($"正在执行: {string.Join(", ", toolCalls.Select(t => t.Function.Name))}…");
                });

                // 让 UI 渲染工具卡片后再开始执行
                await Dispatcher.Yield();

                // 拆分为 task 工具（并行）和其他工具（顺序）
                var taskCalls = toolCalls.Where(t => t.Function.Name == "task").ToList();
                var otherCalls = toolCalls.Where(t => t.Function.Name != "task").ToList();

                // 顺序执行非 task 工具
                foreach (var tc in otherCalls)
                {
                    await ExecuteToolSequentialAsync(tc, ct);
                }

                // 并行执行 task 工具
                if (taskCalls.Count > 0)
                {
                    var taskResults = await Task.WhenAll(
                        taskCalls.Select(tc => ExecuteTaskToolAsync(tc, ct)));

                    for (var i = 0; i < taskCalls.Count; i++)
                    {
                        var tc = taskCalls[i];
                        var result = taskResults[i];
                        _conversation.AddToolResult(tc.Id, tc.Function.Name, result);
                    }
                }

                Dispatcher.Invoke(() =>
                    UpdateSpinnerText("AI 正在思考…"));
                continue;
            }

            // 无工具调用 → 完成
            if (contentBuffer.Length > 0)
                _conversation.AddAssistantMessage(contentBuffer.ToString(), _thinkingBuffer);
            break;
        }
    }

    /// <summary>
    /// 顺序执行单个非 task 工具（含 UI 更新）
    /// </summary>
    private async Task ExecuteToolSequentialAsync(ToolCall tc, CancellationToken ct)
    {
        Dispatcher.Invoke(() =>
            UpdateSpinnerText($"⏳ {tc.Function.Name}…"));

        // 确保卡片已渲染后再执行
        await Dispatcher.Yield();

        var args = TryParseArguments(tc.Function.Arguments);

        string? oldFileContent = null;
        if (tc.Function.Name == "write_file")
        {
            var fp2 = args.TryGetValue("filePath", out var fpp) ? fpp?.ToString() : null;
            if (!string.IsNullOrEmpty(fp2) && File.Exists(fp2))
                oldFileContent = File.ReadAllText(fp2);
        }

        var context = new ToolCallContext
        {
            ToolName = tc.Function.Name,
            ToolCallId = tc.Id,
            Arguments = args
        };

        _eventBus.Publish(new ToolCallRequestEvent
        {
            ToolName = tc.Function.Name,
            Arguments = tc.Function.Arguments
        });

        var pipeline = BuildPipeline(tc.Function.Name, args);

        // 工具执行跑在线程池，不阻塞 UI
        var result = await Task.Run(() => pipeline.ExecuteAsync(context));

        _eventBus.Publish(new ToolCallResultEvent
        {
            ToolName = tc.Function.Name,
            Result = result,
            Success = context.Error == null && !context.Cancelled
        });

        _conversation.AddToolResult(tc.Id, tc.Function.Name, result);
        var elapsed = _timingService.StopTool(tc.Id);
        var success = context.Error == null && !context.Cancelled;

        Dispatcher.Invoke(() =>
        {
            UpdateToolCardComplete(tc.Id, success, elapsed, result);
            UpdateSpinnerText($"✔ {tc.Function.Name} 完成");
        });

        // 让 UI 渲染完成后再执行下一个工具
        await Dispatcher.Yield();
    }

    /// <summary>
    /// 并行执行单个 task 工具（返回结果字符串）
    /// </summary>
    private async Task<string> ExecuteTaskToolAsync(ToolCall tc, CancellationToken ct)
    {
        var args = TryParseArguments(tc.Function.Arguments);
        args["___ct___"] = ct;

        var context = new ToolCallContext
        {
            ToolName = tc.Function.Name,
            ToolCallId = tc.Id,
            Arguments = args
        };

        _eventBus.Publish(new ToolCallRequestEvent
        {
            ToolName = tc.Function.Name,
            Arguments = tc.Function.Arguments
        });

        var pipeline = BuildPipeline(tc.Function.Name, args);

        // 子代理执行跑在线程池
        var result = await Task.Run(() => pipeline.ExecuteAsync(context));

        _eventBus.Publish(new ToolCallResultEvent
        {
            ToolName = tc.Function.Name,
            Result = result,
            Success = context.Error == null && !context.Cancelled
        });

        var elapsed = _timingService.StopTool(tc.Id);
        var success = context.Error == null && !context.Cancelled;

        Dispatcher.Invoke(() =>
        {
            UpdateToolCardComplete(tc.Id, success, elapsed, result);
            UpdateSpinnerText($"✔ {tc.Function.Name} 完成");
        });

        return result;
    }

    /// <summary>
    /// 按需构建工具管线（权限 + 日志 + 超时）
    /// </summary>
    private ToolPipeline BuildPipeline(string toolName, Dictionary<string, object?> args)
    {
        var tool = _toolRegistry.GetTool(toolName);
        if (tool == null)
            throw new InvalidOperationException($"工具未注册: {toolName}");

        var pipeline = new ToolPipeline(tool, _workspaceService);

        // 权限过滤器
        pipeline.AddFilter(new PermissionPipelineFilter(_permissionManager,
            async (name, command) =>
            {
                var task = await Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new PermissionDialog(name, command) { Owner = this };
                    dialog.ShowDialog();
                    return dialog.Decision;
                });
                return task;
            }));

        // 日志过滤器
        pipeline.AddFilter(new LoggingFilter());

        // 超时过滤器
        pipeline.AddFilter(new TimeoutFilter(60000));

        return pipeline;
    }

    private static Dictionary<string, object?> TryParseArguments(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                   ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    // ═══════════════════════════════════════════
    //  UI 渲染方法
    // ═══════════════════════════════════════════

    /// <summary>
    /// 用户消息 — 终端风格：▸ 标记 + 纯文本左对齐
    /// </summary>
    private async void AppendUserMessage(string content)
    {
        await _chatRenderer.AppendUserMessage(content);
        _aiStreamBuffer.Clear();
    }

    private void AppendStreamText(string text)
    {
        _aiStreamBuffer.Append(text);
        if (_iterationFirstContent)
        {
            _iterationFirstContent = false;
            _thinkingActive = false;
            _ = _chatRenderer.CollapseThinkingCard();
            _ = _chatRenderer.AppendAiContent(_aiStreamBuffer.ToString());
        }
        else
        {
            _ = _chatRenderer.UpdateAiContent(_aiStreamBuffer.ToString());
        }
    }

    private void FlushCurrentAiParagraph()
    {
        // Markdown 已在 AppendStreamText 中实时渲染，无需 flush
    }

    /// <summary>
    /// 为每个工具调用创建独立卡片（spinner 状态）
    /// </summary>
    private void AppendToolCards(List<ToolCall> toolCalls)
    {
        foreach (var tc in toolCalls)
        {
            _timingService.StartTool(tc.Id);
            _activeToolCards.Add(tc.Id);
            var summary = GetToolParamSummary(tc.Function.Name,
                TryParseArguments(tc.Function.Arguments));
            CreateToolCard(tc.Id, tc.Function.Name, summary);
        }
    }

    private void CreateToolCard(string toolCallId, string toolName, string paramSummary)
    {
        _ = _chatRenderer.AppendToolCard(toolCallId, toolName, paramSummary);
    }

    private async void UpdateToolCardComplete(string toolCallId, bool success, TimeSpan elapsed, string? resultText)
    {
        var elapsedStr = TimingService.FormatElapsed(elapsed);
        var display = resultText?.Length > 300 ? resultText[..300] + "\n...（共 N 行）" : resultText ?? "";
        await _chatRenderer.UpdateToolCardComplete(toolCallId, success, elapsedStr, display);
        _activeToolCards.Remove(toolCallId);
    }

    /// <summary>
    /// 系统消息 — 最淡的颜色，最小字号
    /// </summary>
    private async void AppendSystemMessage(string content)
    {
        await _chatRenderer.AppendSystemMessage(content);
    }

    // ═══════════════════════════════════════════
    //  Todo 面板
    // ═══════════════════════════════════════════

    private List<TodoItem> _currentTodos = new();

    private void UpdateTodoPanel(List<TodoItem> todos)
    {
        _currentTodos = todos;

        if (todos.Count == 0)
        {
            TodoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var total = todos.Count;
        var completed = todos.Count(t => t.Status == TodoStatus.Completed);
        var percent = total > 0 ? (int)((double)completed / total * 100) : 0;

        TodoPanel.Visibility = Visibility.Visible;
        TodoExpander.Foreground = (Brush)Application.Current.Resources["SuccessGreenBrush"];
        TodoExpander.Header = completed == total
            ? $"任务 ✓ 全部完成 ({completed}/{total})"
            : $"任务 ({completed}/{total})";
        TodoProgressBar.Value = percent;

        TodoItemsControl.Items.Clear();
        foreach (var todo in todos)
        {
            var icon = todo.Status switch
            {
                TodoStatus.Completed => "✔",
                TodoStatus.InProgress => "⏳",
                TodoStatus.Cancelled => "✘",
                _ => "○"
            };

            var fgColor = todo.Status switch
            {
                TodoStatus.Completed => (Brush)Application.Current.Resources["SuccessGreenBrush"],
                TodoStatus.InProgress => (Brush)Application.Current.Resources["RunningBlueBrush"],
                TodoStatus.Cancelled => (Brush)Application.Current.Resources["PrimaryLightBrush"],
                _ => (Brush)Application.Current.Resources["PrimaryMediumBrush"]
            };

            var priorityMark = todo.Priority switch
            {
                TodoPriority.High => " ⚡",
                _ => ""
            };

            var textDeco = todo.Status == TodoStatus.Cancelled
                ? TextDecorations.Strikethrough
                : null;

            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1, 0, 1)
            };

            var iconRun = new TextBlock
            {
                Text = icon,
                Foreground = fgColor,
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            item.Children.Add(iconRun);

            var textRun = new TextBlock
            {
                Text = $"{todo.Content}{priorityMark}",
                Foreground = fgColor,
                FontSize = 12,
                TextDecorations = textDeco,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            item.Children.Add(textRun);

            TodoItemsControl.Items.Add(item);
        }
    }

    private void TodoExpander_Expanded(object sender, RoutedEventArgs e)
    {
        // 无需额外处理，自动调整布局
    }

    private void TodoExpander_Collapsed(object sender, RoutedEventArgs e)
    {
        // 无需额外处理
    }

    // ═══════════════════════════════════════════
    //  子代理状态面板
    // ═══════════════════════════════════════════

    private record SubagentInfo(string TaskId, string Description, string Type,
        DateTime StartTime, bool Completed, string? Summary);

    private readonly List<SubagentInfo> _subagentList = new();

    private void OnSubagentStarted(SubagentStartedEvent e)
    {
        _subagentList.Add(new SubagentInfo(e.TaskId, e.Description, e.Type,
            DateTime.Now, false, null));
        _subagentNeedsRebuild = true;
        RenderSubagentPanel();
    }

    private void OnSubagentCompleted(SubagentCompletedEvent e)
    {
        var existing = _subagentList.Find(s => s.TaskId == e.TaskId);
        if (existing != null)
        {
            var idx = _subagentList.IndexOf(existing);
            _subagentList[idx] = existing with { Completed = true, Summary = e.Summary };
        }
        _subagentNeedsRebuild = true;
        RenderSubagentPanel();
    }

    private bool _subagentNeedsRebuild;

    private void RenderSubagentPanel()
    {
        // 清理超过 60 秒的已完成子代理
        var removed = _subagentList.RemoveAll(s => s.Completed && (DateTime.Now - s.StartTime).TotalSeconds > 60);
        if (removed > 0) _subagentNeedsRebuild = true;

        if (_subagentList.Count == 0)
        {
            SubagentPanel.Visibility = Visibility.Collapsed;
            _subagentNeedsRebuild = false;
            return;
        }

        SubagentPanel.Visibility = Visibility.Visible;

        // 仅 spinner tick 触发时只更新运行中的文本，不重建整个列表
        if (!_subagentNeedsRebuild)
        {
            for (var i = 0; i < _subagentList.Count; i++)
            {
                var info = _subagentList[i];
                if (info.Completed) continue;

                var elapsed = (DateTime.Now - info.StartTime).TotalSeconds;
                var spinFrame = SpinnerFrames[_spinnerIndex % SpinnerFrames.Length];

                if (SubagentItemsControl.Items[i] is StackPanel row && row.Children.Count >= 2)
                {
                    if (row.Children[0] is TextBlock spinnerTb)
                        spinnerTb.Text = $"{spinFrame} [{info.Type}] {info.Description}";
                    if (row.Children[1] is TextBlock elapsedTb)
                        elapsedTb.Text = $"{elapsed:F1}s";
                }
            }
            SubagentExpander.Header = $"子代理 (运行中: {_subagentList.Count(s => !s.Completed)})";
            return;
        }

        SubagentExpander.Header = _subagentList.Any(s => !s.Completed)
            ? $"子代理 (运行中: {_subagentList.Count(s => !s.Completed)})"
            : "子代理 ✓ 全部完成";

        SubagentItemsControl.Items.Clear();

        foreach (var info in _subagentList)
        {
            var elapsed = (DateTime.Now - info.StartTime).TotalSeconds;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            if (info.Completed)
            {
                var icon = info.Summary != null && info.Summary.Contains("失败")
                    ? "✘" : "✔";
                var fg = info.Summary != null && info.Summary.Contains("失败")
                    ? (Brush)Application.Current.Resources["ErrorRedBrush"] : (Brush)Application.Current.Resources["SuccessGreenBrush"];

                row.Children.Add(new TextBlock
                {
                    Text = $"{icon} [{info.Type}] {info.Description}",
                    Foreground = fg,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 8, 0)
                });

                if (info.Summary != null)
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = info.Summary,
                        Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
                        FontSize = 11,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 400
                    });
                }
            }
            else
            {
                // 运行中的用 spinner 前缀 + 耗时倒数
                var spinFrame = SpinnerFrames[(int)(elapsed / 0.12) % SpinnerFrames.Length];

                row.Children.Add(new TextBlock
                {
                    Text = $"{spinFrame} [{info.Type}] {info.Description}",
                    Foreground = (Brush)Application.Current.Resources["RunningBlueBrush"],
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 8, 0)
                });

                row.Children.Add(new TextBlock
                {
                    Text = $"{elapsed:F1}s",
                    Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
                    FontSize = 11
                });
            }

            SubagentItemsControl.Items.Add(row);
        }
        _subagentNeedsRebuild = false;
    }

    /// <summary>当前是否正在显示思考卡片</summary>
    private bool _thinkingActive;
    private DateTime _lastThinkChunk = DateTime.MinValue;

    private void UpdateThinkingPanel()
    {
        if (string.IsNullOrWhiteSpace(_thinkingBuffer))
            return;

        if (!_thinkingActive)
        {
            _ = _chatRenderer.AppendThinkingCard(_thinkingBuffer);
            _thinkingActive = true;
        }
        else
        {
            _ = _chatRenderer.UpdateThinkingCard(_thinkingBuffer);
        }
        _lastThinkChunk = DateTime.Now;
    }

    // ═══════════════════════════════════════════
    //  权限确认 — 内联卡片
    // ═══════════════════════════════════════════

    /// <summary>
    /// 权限确认对话框
    /// </summary>
    // ═══════════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════════

    private void ScrollChatToEnd()
    {
        // WebView2 在每个 append 调用中通过 JavaScript scrollToBottom() 自动滚动
    }

    private void SetInputEnabled(bool enabled)
    {
        InputBox.IsEnabled = enabled;
        SendButton.IsEnabled = enabled;
    }

    /// <summary>
    /// 获取工具对应的图标
    /// </summary>
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

    /// <summary>
    /// 获取工具参数摘要（截取关键参数用于卡片头部显示）
    /// </summary>
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

    private static string TruncateParam(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLen ? text : text[..maxLen] + "...";
    }

    // ═══════════════════════════════════════════
    //  右键菜单
    // ═══════════════════════════════════════════

    private void CopySelection_Click(object sender, RoutedEventArgs e)
    {
        // WebView2 自带文本选择的右键菜单
    }

    private void ClearScreen()
    {
        _conversation.ClearConversation();
        _ = _chatRenderer.ClearChat();
        InitializeChat();
    }

    // ═══════════════════════════════════════════
    //  进度指示器 (Spinner)
    // ═══════════════════════════════════════════

    private void EnsureSpinnerTimer()
    {
        if (_spinnerTimer != null) return;
        _spinnerTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Normal,
            (_, _) => SpinnerTimer_Tick(null, EventArgs.Empty),
            Dispatcher.CurrentDispatcher);
    }

    private void StartStatusSpinner(string text)
    {
        EnsureSpinnerTimer();
        _toolProgressText = text;
        _spinnerIndex = 0;
        ThinkingBar.Visibility = Visibility.Visible;
        ThinkingSpinner.Text = SpinnerFrames[0];
        ThinkingIntentLabel.Text = _toolProgressText;
        _spinnerTimer!.Start();
    }

    private void UpdateSpinnerText(string text)
    {
        _toolProgressText = text;
        ThinkingIntentLabel.Text = text;
    }

    private void StopStatusSpinner()
    {
        _spinnerTimer?.Stop();
        ThinkingBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>从思考内容尾部提取意图型句子，让用户知道模型下一步要干嘛</summary>
    private static string ExtractIntent(string thinkingBuffer)
    {
        if (string.IsNullOrWhiteSpace(thinkingBuffer))
            return "AI 正在思考…";

        // 取尾部最后 300 字（最新的思考）
        var tail = thinkingBuffer.Length > 300
            ? thinkingBuffer[^300..]
            : thinkingBuffer;

        // 按句号/换行拆分，从后往前找意图句
        var sentences = System.Text.RegularExpressions.Regex.Split(tail, @"(?<=[.\n])");
        for (var i = sentences.Length - 1; i >= 0; i--)
        {
            var s = sentences[i].Trim();
            if (s.Length < 5) continue;

            if (s.Length > 60) s = s[..60] + "…";

            // 匹配意图关键词
            if (System.Text.RegularExpressions.Regex.IsMatch(s,
                @"\b(Let me|I['']ll|I need to|I should|Now I|Next I|First I|I will|我要|我先|我现在|接下来)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return s;
            }
        }

        return "AI 正在思考…";
    }

    private void SpinnerTimer_Tick(object? sender, EventArgs e)
    {
        _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
        ThinkingSpinner.Text = SpinnerFrames[_spinnerIndex];

        // 思考中 → 提取意图更新文字
        if (_thinkingActive && !string.IsNullOrEmpty(_thinkingBuffer))
        {
            ThinkingIntentLabel.Text = ExtractIntent(_thinkingBuffer);
        }
        else
        {
            ThinkingIntentLabel.Text = _toolProgressText;
        }

        StatusTimingLabel.Text = _timingService.GetRoundSummary();

        // 思考中 — 实时更新进度（不自动折叠，等 content/工具输出时再叠）
        if (_thinkingActive && !string.IsNullOrEmpty(_thinkingBuffer))
        {
            _ = _chatRenderer.UpdateThinkingCard(_thinkingBuffer);
        }

        // 工具卡片 — 实时耗时
        foreach (var toolId in _activeToolCards)
        {
            var elapsed = _timingService.GetElapsed(toolId);
            _ = _chatRenderer.UpdateToolCardElapsed(toolId, TimingService.FormatElapsed(elapsed));
        }

        // 子代理面板 spinner 联动刷新
        if (_subagentList.Any(s => !s.Completed))
            RenderSubagentPanel();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _currentCancellation?.Cancel();
    }

    private void OpenSettingsWindow()
    {
        var settingsWindow = new SettingsWindow(_configService, _eventBus)
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsWindow();
    }

    private void WorkspaceBar_Click(object sender, MouseButtonEventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var path = NativeFolderPicker.ShowDialog(handle, "选择工作区目录");
        if (path != null)
            _workspaceService.SetWorkspace(path);
    }

    // ═══════════════════════════════════════════
    //  命令自动补全
    // ═══════════════════════════════════════════

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (!text.StartsWith('/') || text.Length < 1)
        {
            CommandPopup.IsOpen = false;
            return;
        }

        // 已输入完整命令 + 空格 → 不显示
        if (text.Contains(' '))
        {
            CommandPopup.IsOpen = false;
            return;
        }

        // 过滤命令
        var allCommands = _commandRegistry.GetAll();
        var matching = allCommands
            .Where(c => c.Name.StartsWith(text[1..], StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matching.Count == 0)
        {
            CommandPopup.IsOpen = false;
            return;
        }

        CommandListBox.ItemsSource = matching;
        CommandListBox.DisplayMemberPath = null;

        CommandListBox.ItemTemplate = CreateCommandItemTemplate();
        CommandListBox.SelectedIndex = 0;
        CommandPopup.IsOpen = true;
    }

    private DataTemplate CreateCommandItemTemplate()
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        var nameBlock = new FrameworkElementFactory(typeof(TextBlock));
        nameBlock.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding("Name") { StringFormat = "/{0}" });
        nameBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        nameBlock.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(86, 156, 214)));
        factory.AppendChild(nameBlock);

        var descBlock = new FrameworkElementFactory(typeof(TextBlock));
        descBlock.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding("Description"));
        descBlock.SetValue(TextBlock.FontSizeProperty, 12.0);
        descBlock.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(150, 150, 150)));
        descBlock.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
        factory.AppendChild(descBlock);

        template.VisualTree = factory;
        return template;
    }

    private void CommandListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            e.Handled = true;
            SelectCommandFromList();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CommandPopup.IsOpen = false;
            InputBox.Focus();
        }
    }

    private void CommandListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectCommandFromList();
    }

    private void SelectCommandFromList()
    {
        if (CommandListBox.SelectedItem is ISlashCommand cmd)
        {
            InputBox.Text = $"/{cmd.Name} ";
            InputBox.CaretIndex = InputBox.Text.Length;
        }
        CommandPopup.IsOpen = false;
        InputBox.Focus();
    }

    // ═══════════════════════════════════════════
    //  历史会话
    // ═══════════════════════════════════════════

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPopup.IsOpen)
        {
            HistoryPopup.IsOpen = false;
            return;
        }
        LoadHistorySessions();
        HistoryPopup.IsOpen = true;
    }

    private async void LoadHistorySessions()
    {
        var sessions = await _sessionStore.ListSessionsAsync();
        HistoryListBox.ItemsSource = null;

        if (sessions.Count == 0)
        {
            HistoryListBox.Visibility = Visibility.Collapsed;
            HistoryEmptyLabel.Visibility = Visibility.Visible;
            return;
        }

        HistoryListBox.Visibility = Visibility.Visible;
        HistoryEmptyLabel.Visibility = Visibility.Collapsed;

        HistoryListBox.ItemsSource = sessions;
        HistoryListBox.DisplayMemberPath = null;
        HistoryListBox.ItemTemplate = CreateHistoryItemTemplate();
        HistoryListBox.SelectedIndex = 0;
        HistoryListBox.Focus();
    }

    private DataTemplate CreateHistoryItemTemplate()
    {
        var template = new DataTemplate();

        // 外层 StackPanel（垂直）
        var outerStack = new FrameworkElementFactory(typeof(StackPanel));
        outerStack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        // 第一行：标题 + 时间（水平）
        var row1 = new FrameworkElementFactory(typeof(DockPanel));

        var titleBlock = new FrameworkElementFactory(typeof(TextBlock));
        titleBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
        titleBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        titleBlock.SetValue(TextBlock.FontSizeProperty, 13.0);
        titleBlock.SetValue(DockPanel.DockProperty, Dock.Left);
        row1.AppendChild(titleBlock);

        var timeBlock = new FrameworkElementFactory(typeof(TextBlock));
        timeBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("UpdatedAt")
        {
            StringFormat = "MM-dd HH:mm"
        });
        timeBlock.SetValue(TextBlock.FontSizeProperty, 10.0);
        timeBlock.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(136, 168, 192)));
        timeBlock.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
        timeBlock.SetValue(DockPanel.DockProperty, Dock.Right);
        row1.AppendChild(timeBlock);

        outerStack.AppendChild(row1);

        // 第二行：条数 · 模型
        var row2 = new FrameworkElementFactory(typeof(TextBlock));
        var multiBinding = new System.Windows.Data.MultiBinding();
        multiBinding.StringFormat = "{0} 条消息 · {1}";
        multiBinding.Bindings.Add(new System.Windows.Data.Binding("MessageCount"));
        multiBinding.Bindings.Add(new System.Windows.Data.Binding("Model"));
        row2.SetBinding(TextBlock.TextProperty, multiBinding);
        row2.SetValue(TextBlock.FontSizeProperty, 10.0);
        row2.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(136, 168, 192)));
        row2.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
        outerStack.AppendChild(row2);

        template.VisualTree = outerStack;
        return template;
    }

    private void HistoryListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            LoadSessionFromHistory();
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            DeleteSelectedSession();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HistoryPopup.IsOpen = false;
        }
    }

    private void HistoryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        LoadSessionFromHistory();
    }

    private async void LoadSessionFromHistory()
    {
        if (HistoryListBox.SelectedItem is not SessionMetadata meta) return;

        var data = await _sessionStore.LoadAsync(meta.Id);
        if (data == null) return;

        _conversation.DeserializeSession(data.MessagesJson);
        await _chatRenderer.ClearChat();
        _permissionManager.ResetSession();
        _hasUnsavedChanges = false;
        _commandContext.CurrentSessionId = meta.Id;

        // 历史消息完整渲染：跳过 system 消息（启动时已自动设置），渲染其他角色
        foreach (var msg in _conversation.Messages)
        {
            switch (msg.Role)
            {
                case "system":
                    break;

                case "user" when msg.Content != null:
                    await _chatRenderer.AppendUserMessage(msg.Content);
                    break;

                case "assistant":
                    if (!string.IsNullOrWhiteSpace(msg.ReasoningContent))
                    {
                        await _chatRenderer.AppendThinkingCard(msg.ReasoningContent);
                        await _chatRenderer.CollapseThinkingCard();
                    }

                    if (!string.IsNullOrWhiteSpace(msg.Content))
                        await _chatRenderer.AppendAiContent(msg.Content);

                    if (msg.ToolCalls != null)
                    {
                        foreach (var tc in msg.ToolCalls)
                        {
                            var argSummary = tc.Function.Arguments.Length > 80
                                ? tc.Function.Arguments[..80] + "..."
                                : tc.Function.Arguments;
                            await _chatRenderer.AppendToolCard(tc.Id, tc.Function.Name, argSummary);
                        }
                    }
                    break;

                case "tool" when msg.ToolCallId != null:
                    var resultText = msg.Content ?? "";
                    var resultPreview = resultText.Length > 200
                        ? resultText[..200] + "..."
                        : resultText;
                    await _chatRenderer.UpdateToolCardComplete(msg.ToolCallId, true, "", resultPreview);
                    break;
            }
        }

        HistoryPopup.IsOpen = false;
        AppendSystemMessage($"已加载历史会话: {meta.Title} ({meta.MessageCount} 条消息)");
    }

    private void NewSessionButton_Click(object sender, RoutedEventArgs e)
    {
        _conversation.ClearConversation();
        _permissionManager.ResetSession();
        _ = _chatRenderer.ClearChat();
        _hasUnsavedChanges = false;
        _commandContext.CurrentSessionId = null;
        HistoryPopup.IsOpen = false;
        AppendSystemMessage("已创建新对话");
    }

    private void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedSession();
    }

    private async void DeleteSelectedSession()
    {
        if (HistoryListBox.SelectedItem is not SessionMetadata meta) return;

        var result = MessageBox.Show(
            $"确认删除会话 \"{meta.Title}\"？\n此操作不可撤销。",
            "DeepSeek Code",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        await _sessionStore.DeleteAsync(meta.Id);

        // 如果删除的是当前正在编辑的会话，清空
        if (_commandContext.CurrentSessionId == meta.Id)
            _commandContext.CurrentSessionId = null;

        // 刷新列表
        LoadHistorySessions();
    }

    private void FinishStreaming()
    {
        _isStreaming = false;
        StopStatusSpinner();
        StatusTimingLabel.Text = _timingService.GetRoundSummary();
        SetInputEnabled(true);
        _currentCancellation = null;
        InputBox.Focus();
    }

    /// <summary>更新状态栏模型/Token 显示</summary>
    private void UpdateStatusBar()
    {
        _statusVm.Model = _configService.Config.Model;
        StatusModelLabel.Text = _statusVm.Model;
        StatusTokenLabel.Text = _statusVm.TokenDisplay;
    }

    /// <summary>根据 API 返回的 usage 更新状态栏</summary>
    private void UpdateTokenDisplay(UsageUpdatedEvent e)
    {
        _statusVm.TokenCount = e.TotalTokens;
        _statusVm.CacheHitTokens = e.CacheHitTokens;
        _statusVm.CacheMissTokens = e.CacheMissTokens;
        _statusVm.ReasoningTokens = e.ReasoningTokens;

        StatusTokenLabel.Text = _statusVm.TokenDisplay;

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
                // 快速保存：覆盖已加载的会话或新建
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

    /// <summary>从对话内容自动生成会话标题</summary>
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
