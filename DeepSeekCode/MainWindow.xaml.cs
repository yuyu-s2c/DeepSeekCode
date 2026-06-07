using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using DeepSeekCode.Commands;
using DeepSeekCode.Diff;
using DeepSeekCode.Models;
using DeepSeekCode.Services;
using DeepSeekCode.Tools;
using DeepSeekCode.UI;

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

    private CancellationTokenSource? _currentCancellation;
    private bool _isStreaming;
    private string _thinkingBuffer = "";
    private Paragraph? _currentAiParagraph;

    // 输入历史
    private readonly List<string> _inputHistory = new();
    private int _historyIndex = -1;
    private string _savedInput = "";

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
        ChatViewer.Document = new FlowDocument
        {
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["PrimaryDarkBrush"]
        };

        SubscribeToEvents();
        SetInitialSystemPrompt();
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

    private void OnPermissionRequested(PermissionRequestEvent e)
    {
        Dispatcher.Invoke(() =>
        {
            var message = $"允许执行工具 '{e.ToolName}'?\n{e.Command}";
            var result = MessageBox.Show(message, "权限确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            e.UserDecision = result == MessageBoxResult.Yes;
        });
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
        var doc = (FlowDocument)ChatViewer.Document;
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(new Run("DeepSeek Code 已就绪。输入 /help 获取帮助。"))
        {
            Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
            FontSize = 11
        });
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
        StartStatusSpinner("AI 正在思考…");
        StopButton.Visibility = Visibility.Visible;
        _thinkingBuffer = "";

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
                    ChatViewer.Document = new FlowDocument
                    {
                        FontFamily = new FontFamily("Microsoft YaHei"),
                        FontSize = 14
                    };
                    InitializeChat();
                }
                FinishStreaming();
                return;
            }
        }

        // 2. 发送给 AI
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
                    AppendToolCallMessage(toolCalls);
                    UpdateSpinnerText($"正在执行: {string.Join(", ", toolCalls.Select(t => t.Function.Name))}…");
                });

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

        var args = TryParseArguments(tc.Function.Arguments);

        // 写文件前捕获原内容（用于 diff 对比）
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
        var result = await pipeline.ExecuteAsync(context);

        _eventBus.Publish(new ToolCallResultEvent
        {
            ToolName = tc.Function.Name,
            Result = result,
            Success = context.Error == null && !context.Cancelled
        });

        _conversation.AddToolResult(tc.Id, tc.Function.Name, result);
        var capturedOldContent = oldFileContent;
        Dispatcher.Invoke(() =>
        {
            AppendToolResultMessage(tc.Function.Name, result);

            if (tc.Function.Name == "edit_file")
                RenderEditDiff(args);
            else if (tc.Function.Name == "write_file")
                RenderWriteDiff(args, capturedOldContent);

            UpdateSpinnerText($"✔ {tc.Function.Name} 完成");
        });
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
        var result = await pipeline.ExecuteAsync(context);

        _eventBus.Publish(new ToolCallResultEvent
        {
            ToolName = tc.Function.Name,
            Result = result,
            Success = context.Error == null && !context.Cancelled
        });

        Dispatcher.Invoke(() =>
        {
            AppendToolResultMessage(tc.Function.Name, result);
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
            async name =>
            {
                var permissionEvent = new PermissionRequestEvent
                {
                    ToolName = name,
                    Command = args.TryGetValue("command", out var cmd) ? cmd?.ToString() : null,
                    Level = _permissionManager.Check(name,
                        args.TryGetValue("command", out var c) ? c?.ToString() : null)
                };
                _eventBus.Publish(permissionEvent);
                await Task.CompletedTask;
                return permissionEvent.UserDecision;
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
    /// 用户消息 — 终端风格：▸ 标记 + 纯文本左对齐，保留右键菜单
    /// </summary>
    private void AppendUserMessage(string content)
    {
        var doc = (FlowDocument)ChatViewer.Document;

        // 用户标记行
        doc.Blocks.Add(new Paragraph(new Run("▸ 你")
        {
            Foreground = (Brush)Application.Current.Resources["PrimaryBlueBrush"],
            FontWeight = FontWeights.SemiBold
        })
        {
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 2)
        });

        // 用户内容 — 可右键复制/重发
        var contentPara = new Paragraph(new Run(content)
        {
            Foreground = (Brush)Application.Current.Resources["PrimaryDarkBrush"]
        })
        {
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4)
        };

        // 右键菜单（重新发送）
        var resendMenu = new ContextMenu();
        var resendItem = new MenuItem { Header = "重新发送" };
        resendItem.Click += (_, _) =>
        {
            InputBox.Text = content;
            InputBox.CaretIndex = content.Length;
            InputBox.Focus();
        };
        resendMenu.Items.Add(resendItem);
        contentPara.ContextMenu = resendMenu;

        doc.Blocks.Add(contentPara);

        _currentAiParagraph = null;
        ScrollChatToEnd();
    }

    private void AppendStreamText(string text)
    {
        var doc = (FlowDocument)ChatViewer.Document;
        if (_currentAiParagraph == null)
        {
            _currentAiParagraph = new Paragraph
            {
                Margin = new Thickness(0, 4, 0, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212))
            };
            doc.Blocks.Add(_currentAiParagraph);
        }

        if (_currentAiParagraph.Inlines.LastOrDefault() is Run lastRun)
            lastRun.Text += text;
        else
            _currentAiParagraph.Inlines.Add(new Run(text));

        ScrollChatToEnd();
    }

    private void FlushCurrentAiParagraph()
    {
        if (_currentAiParagraph == null || _currentAiParagraph.Inlines.Count == 0)
        {
            if (_currentAiParagraph != null)
            {
                ((FlowDocument)ChatViewer.Document).Blocks.Remove(_currentAiParagraph);
                _currentAiParagraph = null;
            }
            return;
        }

        var textBuilder = new StringBuilder();
        foreach (var inline in _currentAiParagraph.Inlines)
        {
            if (inline is Run run)
                textBuilder.Append(run.Text);
        }

        var markdown = textBuilder.ToString();
        var doc = (FlowDocument)ChatViewer.Document;

        // AI 标记行
        doc.Blocks.Add(new Paragraph(new Run("DeepSeek")
        {
            Foreground = (Brush)Application.Current.Resources["AiLabelBrush"],
            FontWeight = FontWeights.SemiBold
        })
        {
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 2)
        });

        doc.Blocks.Remove(_currentAiParagraph);
        _currentAiParagraph = null;

        var rendered = Markdown.MarkdownRenderer.Render(markdown);
        while (rendered.Blocks.Count > 0)
        {
            var block = rendered.Blocks.FirstBlock;
            rendered.Blocks.Remove(block);
            doc.Blocks.Add(block);
        }

        ScrollChatToEnd();
    }

    private void AppendToolCallMessage(List<ToolCall> toolCalls)
    {
        var names = string.Join(", ", toolCalls.Select(t => t.Function.Name));
        ((FlowDocument)ChatViewer.Document).Blocks.Add(
            new Paragraph(new Run($"[调用工具: {names}]"))
            {
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 2)
            });
        ScrollChatToEnd();
    }

    private void AppendToolResultMessage(string toolName, string result)
    {
        var display = result.Length > 200 ? result[..200] + "\n...(已截断)" : result;
        ((FlowDocument)ChatViewer.Document).Blocks.Add(
            new Paragraph(new Run($"[{toolName}] {display}"))
            {
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                FontSize = 11,
                Margin = new Thickness(16, 2, 0, 2)
            });
        ScrollChatToEnd();
    }

    /// <summary>
    /// 系统消息 — 最淡的颜色，最小字号
    /// </summary>
    private void AppendSystemMessage(string content)
    {
        ((FlowDocument)ChatViewer.Document).Blocks.Add(
            new Paragraph(new Run(content))
            {
                Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 3)
            });
        ScrollChatToEnd();
    }

    private void RenderEditDiff(Dictionary<string, object?> args)
    {
        var filePath = args.TryGetValue("filePath", out var fp) ? fp?.ToString() : null;
        var oldStr = args.TryGetValue("oldString", out var os) ? os?.ToString() : null;
        var newStr = args.TryGetValue("newString", out var ns) ? ns?.ToString() : null;

        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(oldStr) || string.IsNullOrEmpty(newStr))
            return;

        var diffSection = DiffRenderer.RenderDiff(oldStr, newStr, filePath);
        ((FlowDocument)ChatViewer.Document).Blocks.Add(diffSection);
        ScrollChatToEnd();
    }

    private void RenderWriteDiff(Dictionary<string, object?> args, string? oldContent)
    {
        var filePath = args.TryGetValue("filePath", out var fp) ? fp?.ToString() : null;
        var newContent = args.TryGetValue("content", out var ct) ? ct?.ToString() : null;

        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(newContent))
            return;

        var old = oldContent ?? "";
        var diffSection = DiffRenderer.RenderDiff(old, newContent, filePath);
        ((FlowDocument)ChatViewer.Document).Blocks.Add(diffSection);
        ScrollChatToEnd();
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
                TodoStatus.Completed => Color.FromRgb(100, 180, 120),
                TodoStatus.InProgress => Color.FromRgb(86, 156, 214),
                TodoStatus.Cancelled => Color.FromRgb(140, 140, 140),
                _ => Color.FromRgb(200, 200, 180)
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
                Foreground = new SolidColorBrush(fgColor),
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            item.Children.Add(iconRun);

            var textRun = new TextBlock
            {
                Text = $"{todo.Content}{priorityMark}",
                Foreground = new SolidColorBrush(fgColor),
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
                var spinFrame = SpinnerFrames[(int)(elapsed / 0.12) % SpinnerFrames.Length];

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
                    ? Color.FromRgb(220, 80, 80) : Color.FromRgb(80, 200, 120);

                row.Children.Add(new TextBlock
                {
                    Text = $"{icon} [{info.Type}] {info.Description}",
                    Foreground = new SolidColorBrush(fg),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 8, 0)
                });

                if (info.Summary != null)
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = info.Summary,
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
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
                    Foreground = new SolidColorBrush(Color.FromRgb(86, 156, 214)),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 8, 0)
                });

                row.Children.Add(new TextBlock
                {
                    Text = $"{elapsed:F1}s",
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 170, 190)),
                    FontSize = 11
                });
            }

            SubagentItemsControl.Items.Add(row);
        }
        _subagentNeedsRebuild = false;
    }

    private Paragraph? _thinkingParagraph;

    private void UpdateThinkingPanel()
    {
        var doc = (FlowDocument)ChatViewer.Document;

        if (string.IsNullOrWhiteSpace(_thinkingBuffer))
        {
            // 清空上次的思考段落
            if (_thinkingParagraph != null)
            {
                doc.Blocks.Remove(_thinkingParagraph);
                _thinkingParagraph = null;
            }
            return;
        }

        // 首次有思考内容时创建段落
        if (_thinkingParagraph == null)
        {
            _thinkingParagraph = new Paragraph
            {
                Margin = new Thickness(0, 6, 0, 4),
                Background = (Brush)Application.Current.Resources["SurfaceCardBrush"],
                Padding = new Thickness(10, 6, 10, 6)
            };
            doc.Blocks.Add(_thinkingParagraph);
        }

        // 每次更新重新渲染
        _thinkingParagraph.Inlines.Clear();
        _thinkingParagraph.Inlines.Add(new Run("思考过程")
        {
            Foreground = (Brush)Application.Current.Resources["AiLabelBrush"],
            FontSize = 10,
            FontWeight = FontWeights.SemiBold
        });
        _thinkingParagraph.Inlines.Add(new LineBreak());
        _thinkingParagraph.Inlines.Add(new Run(_thinkingBuffer)
        {
            Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
            FontSize = 10
        });

        ScrollChatToEnd();
    }

    // ═══════════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════════

    private void ScrollChatToEnd()
    {
        var doc = (FlowDocument)ChatViewer.Document;
        if (doc.Blocks.LastBlock != null)
            doc.Blocks.LastBlock.BringIntoView();
    }

    private void ScrollThinkingToEnd()
    {
        // Thinking 内容现在内联在 ChatViewer 中，跟随主滚动
    }

    private void SetInputEnabled(bool enabled)
    {
        InputBox.IsEnabled = enabled;
        SendButton.IsEnabled = enabled;
    }

    // ═══════════════════════════════════════════
    //  右键菜单
    // ═══════════════════════════════════════════

    private void CopySelection_Click(object sender, RoutedEventArgs e)
    {
        var selection = ChatViewer.Selection;
        if (selection != null && !selection.IsEmpty)
        {
            Clipboard.SetText(selection.Text);
        }
    }

    private void ClearScreen()
    {
        _conversation.ClearConversation();
        ChatViewer.Document = new FlowDocument
        {
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = 14
        };
        InitializeChat();
    }

    // ═══════════════════════════════════════════
    //  进度指示器 (Spinner)
    // ═══════════════════════════════════════════

    private void EnsureSpinnerTimer()
    {
        if (_spinnerTimer != null) return;
        _spinnerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _spinnerTimer.Tick += SpinnerTimer_Tick;
    }

    private void StartStatusSpinner(string text)
    {
        EnsureSpinnerTimer();
        _toolProgressText = text;
        _spinnerIndex = 0;
        StatusIndicatorLabel.Text = $"{SpinnerFrames[0]} {_toolProgressText}";
        _spinnerTimer!.Start();
    }

    private void UpdateSpinnerText(string text)
    {
        _toolProgressText = text;
    }

    private void StopStatusSpinner()
    {
        _spinnerTimer?.Stop();
        StatusIndicatorLabel.Text = "";
    }

    private void SpinnerTimer_Tick(object? sender, EventArgs e)
    {
        _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
        StatusIndicatorLabel.Text = $"{SpinnerFrames[_spinnerIndex]} {_toolProgressText}";

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

    private void FinishStreaming()
    {
        _isStreaming = false;
        StopStatusSpinner();
        SetInputEnabled(true);
        StopButton.Visibility = Visibility.Collapsed;
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
}
