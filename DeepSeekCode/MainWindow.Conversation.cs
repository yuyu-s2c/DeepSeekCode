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
using System.Windows.Media;
using System.Windows.Threading;

using DeepSeekCode.Models;
using DeepSeekCode.Services;
using DeepSeekCode.Tools;

namespace DeepSeekCode;

/// <summary>
/// 流式对话核心 + 工具管线执行 + UI 渲染方法
/// </summary>
public partial class MainWindow
{
    // ═══════════════════════════════════════════
    //  流式对话核心
    // ═══════════════════════════════════════════

    private async Task StreamConversationAsync(CancellationToken ct)
    {
        var tools = _toolRegistry.GetDefinitions();

        while (true)
        {
            _thinkingBuffer = "";
            _thinkingActive = false;
            _lastThinkChunk = DateTime.MinValue;
            _iterationFirstContent = true;
            _aiStreamBuffer.Clear();

            var messages = await _conversation.GetProcessedMessagesAsync();

            var contentBuffer = new StringBuilder();
            var toolCallAccumulators =
                new Dictionary<int, (string id, string name, StringBuilder args)>();

            _eventBus.Publish(new StreamStartedEvent { UserMessage = messages.LastOrDefault()?.Content });

            await foreach (var chunk in _client.StreamChatAsync(
                tools, messages, _configService.Config, ct))
            {
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

                if (!string.IsNullOrEmpty(delta.ReasoningContent))
                {
                    _thinkingBuffer += delta.ReasoningContent;
                    _eventBus.Publish(new StreamChunkEvent { ReasoningContent = delta.ReasoningContent });
                    Dispatcher.Invoke(() => UpdateThinkingPanel());
                }

                if (!string.IsNullOrEmpty(delta.Content))
                {
                    contentBuffer.Append(delta.Content);
                    _eventBus.Publish(new StreamChunkEvent { Content = delta.Content });
                    Dispatcher.Invoke(() => AppendStreamText(delta.Content));
                }

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
            _ = _chatRenderer.CollapseThinkingCard();

            _eventBus.Publish(new StreamCompletedEvent
            {
                FullResponse = contentBuffer.ToString(),
                FullReasoning = _thinkingBuffer
            });

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

                await Dispatcher.Yield();

                var taskCalls = toolCalls.Where(t => t.Function.Name == "task").ToList();
                var otherCalls = toolCalls.Where(t => t.Function.Name != "task").ToList();

                foreach (var tc in otherCalls)
                    await ExecuteToolSequentialAsync(tc, ct);

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

            if (contentBuffer.Length > 0)
                _conversation.AddAssistantMessage(contentBuffer.ToString(), _thinkingBuffer);
            break;
        }
    }

    // ═══════════════════════════════════════════
    //  工具管线执行
    // ═══════════════════════════════════════════

    private async Task ExecuteToolSequentialAsync(ToolCall tc, CancellationToken ct)
    {
        Dispatcher.Invoke(() =>
            UpdateSpinnerText($"⏳ {tc.Function.Name}…"));

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

            // Plan 模式工具执行后同步状态（注入提示词 + 更新 UI）
            if (tc.Function.Name is "enter_plan_mode" or "exit_plan_mode")
                SyncPlanMode();
        });

        await Dispatcher.Yield();
    }

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

    private ToolPipeline BuildPipeline(string toolName, Dictionary<string, object?> args)
    {
        var tool = _toolRegistry.GetTool(toolName);
        if (tool == null)
            throw new InvalidOperationException($"工具未注册: {toolName}");

        var pipeline = new ToolPipeline(tool, _workspaceService);

        // Plan 模式：write_file / edit_file 仅限计划文件，自动放行无需询问
        var isPlanFileWrite = _planMode.IsActive
            && (toolName == "write_file" || toolName == "edit_file")
            && args.TryGetValue("filePath", out var fp)
            && string.Equals(fp?.ToString(), _planMode.PlanFilePath, StringComparison.OrdinalIgnoreCase);

        // 工作区外路径检测：用于文件/搜索工具跨工作区操作时强制询问用户
        var isOutsideWorkspace = (toolName is "read_file" or "write_file" or "edit_file" or "glob" or "grep")
            && TryGetPathArg(toolName, args) is { } toolPath
            && !string.IsNullOrWhiteSpace(toolPath)
            && !ToolArgHelper.ValidatePath(toolPath);

        pipeline.AddFilter(new PermissionPipelineFilter(_permissionManager,
            async (name, command) =>
            {
                // Plan 模式写入计划文件：自动允许
                if (isPlanFileWrite)
                    return PermissionDecision.AllowOnce;

                var task = await Dispatcher.InvokeAsync(() =>
                {
                    var label = isOutsideWorkspace
                        ? $"⚠️ 工作区外路径: {command}"
                        : command;
                    var dialog = new PermissionDialog(name, label) { Owner = this };
                    dialog.ShowDialog();
                    return dialog.Decision;
                });
                return task;
            },
            forceAsk: isOutsideWorkspace));

        pipeline.AddFilter(new LoggingFilter(msg => _logger.Info(msg)));
        pipeline.AddFilter(new TimeoutFilter(60000));

        return pipeline;
    }

    /// <summary>从工具参数中提取路径参数（filePath 或 path）</summary>
    private static string? TryGetPathArg(string toolName, Dictionary<string, object?> args)
    {
        var key = toolName is "glob" or "grep" ? "path" : "filePath";
        return args.TryGetValue(key, out var val) ? val?.ToString() : null;
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

    private async void AppendUserMessage(string content)
    {
        await _chatRenderer.AppendUserMessage(content);
        _aiStreamBuffer.Clear();
    }

    private void AppendStreamText(string text)
    {
        _aiStreamBuffer.Append(text);

        // 渲染节流：每 50ms 最多触发一次 WebView2 更新，避免高频重渲染
        if (_renderThrottle.ElapsedMilliseconds < RenderThrottleMs && _iterationFirstContent == false)
            return;
        _renderThrottle.Restart();

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
        // 流式输出结束，确保最后一批文本已渲染到 WebView2
        if (_aiStreamBuffer.Length > 0 && !_iterationFirstContent)
        {
            _ = _chatRenderer.UpdateAiContent(_aiStreamBuffer.ToString());
        }
    }

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

        var summary = resultText ?? "";
        string? diffBlock = null;

        if (resultText != null)
        {
            var diffIdx = resultText.IndexOf("```diff", StringComparison.Ordinal);
            if (diffIdx >= 0)
            {
                summary = resultText[..diffIdx].TrimEnd('\n', '\r');
                diffBlock = resultText[diffIdx..];
            }
        }

        var display = TruncateLines(summary, 5);
        await _chatRenderer.UpdateToolCardComplete(toolCallId, success, elapsedStr, display);
        _activeToolCards.Remove(toolCallId);

        if (!string.IsNullOrEmpty(diffBlock))
            await _chatRenderer.AppendAiContent(diffBlock);
    }

    private async void AppendSystemMessage(string content)
    {
        await _chatRenderer.AppendSystemMessage(content);
    }

    // ═══════════════════════════════════════════
    //  Thinking 面板
    // ═══════════════════════════════════════════

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
}
