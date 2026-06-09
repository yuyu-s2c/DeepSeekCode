using System.Text;
using System.Text.Json;
using DeepSeekCode.Models;

namespace DeepSeekCode.Services;

/// <summary>
/// 子代理执行引擎 — 独立对话循环，带工具调用能力
/// </summary>
public class SubagentRunner
{
    private readonly DeepSeekClient _client;
    private readonly Tools.ToolRegistry _toolRegistry;
    private readonly PermissionManager _permissionManager;
    private readonly WorkspaceService _workspaceService;
    private readonly ConfigService _configService;
    private readonly EventBus _eventBus;
    private readonly Logger _logger;

    private const int MaxIterations = 15;
    private const int TimeoutMs = 120000;
    private const int MaxContextTokens = 16000;

    public SubagentRunner(
        DeepSeekClient client,
        Tools.ToolRegistry toolRegistry,
        PermissionManager permissionManager,
        WorkspaceService workspaceService,
        ConfigService configService,
        EventBus eventBus,
        Logger logger)
    {
        _client = client;
        _toolRegistry = toolRegistry;
        _permissionManager = permissionManager;
        _workspaceService = workspaceService;
        _configService = configService;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// 执行子代理任务，返回结果文本
    /// </summary>
    public async Task<string> RunAsync(
        string taskId,
        string description,
        string subagentType,
        string prompt,
        CancellationToken ct)
    {
        var startTime = DateTime.Now;

        _eventBus.Publish(new SubagentStartedEvent
        {
            TaskId = taskId,
            Description = description,
            Type = subagentType
        });

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystem(BuildSystemPrompt(subagentType)),
                ChatMessage.CreateUser(prompt)
            };

            var subagentConfig = BuildSubagentConfig(subagentType);
            var result = await RunLoopAsync(messages, subagentConfig, linkedCts.Token);

            _eventBus.Publish(new SubagentCompletedEvent
            {
                TaskId = taskId,
                Success = true,
                Summary = Truncate(result, 200),
                Duration = DateTime.Now - startTime
            });

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn($"子代理 {taskId} 取消或超时");
            _eventBus.Publish(new SubagentCompletedEvent
            {
                TaskId = taskId,
                Success = false,
                Summary = "执行超时或已取消",
                Duration = DateTime.Now - startTime
            });
            return "子代理执行超时或已取消";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"子代理 {taskId} 异常");
            _eventBus.Publish(new SubagentCompletedEvent
            {
                TaskId = taskId,
                Success = false,
                Summary = ex.Message,
                Duration = DateTime.Now - startTime
            });
            return $"子代理执行失败: {ex.Message}";
        }
    }

    private AppConfig BuildSubagentConfig(string subagentType)
    {
        var main = _configService.Config;
        // general 类型保留 Thinking 以保证复杂任务质量
        var enableThinking = subagentType == "general";

        return new AppConfig
        {
            ApiKey = main.ApiKey,
            ApiBaseUrl = main.ApiBaseUrl,
            Model = subagentType == "explore" ? "deepseek-v4-flash" : main.Model,
            MaxTokens = main.MaxTokens,
            ThinkingEnabled = enableThinking,
            ReasoningEffort = enableThinking ? "high" : "",
            Temperature = main.Temperature,
            TopP = main.TopP,
            FrequencyPenalty = main.FrequencyPenalty,
            PresencePenalty = main.PresencePenalty,
            EnableJsonOutput = false,
            EnablePrefixCompletion = false,
            PrefixContent = ""
        };
    }

    private string BuildSystemPrompt(string subagentType)
    {
        var ws = _workspaceService.WorkspacePath;

        var basePrompt = $@"You are a DeepSeek Code subagent running in a background thread pool, spawned by the main conversation to handle an independent task. The user will never see your output directly — only the main conversation relays what matters.

Working directory: {ws}

# Harness
 - Your job is to execute the specific task assigned to you, then return the result as plain text. Do not greet, explain what you are, or engage in off-task conversation.
 - You are done when you can produce a final answer without calling more tools. Max {MaxIterations} tool call iterations, {TimeoutMs / 1000}s timeout.
 - Prefer the dedicated file/search tools over shell commands when one fits. Independent tool calls can run in parallel in one response.
 - Reference code as `file_path:line_number`.
 - Report outcomes faithfully: if you couldn't find something, say so. Do not speculate.

Write code that reads like the surrounding code: match its comment density, naming, and idiom.";

        return subagentType switch
        {
            "explore" => basePrompt + $@"

## explore mode — read-only search

You are using the deepseek-v4-flash model with Thinking disabled for fast response.

**Allowed tools:** read_file, glob, grep only. No file modifications, no shell commands.

**Execution strategy:**
1. Analyze the task; identify keywords and file patterns to search for.
2. Use glob to locate candidate files, then grep to narrow down by content.
3. Use read_file to examine the relevant code sections.
4. Synthesize findings into an accurate, cited answer.

**Return format:** Lead with a brief conclusion, then detailed findings with file paths and line numbers. If nothing is found, state what you tried and why results may be absent. Do not give speculative answers.",

            _ => basePrompt + $@"

## general mode — full access

You are using the main conversation's model with Thinking enabled (reasoning_effort: high).

**Allowed tools:** all registered tools, including file read/write, shell commands, and Git operations.

**Execution strategy:**
1. Understand the task objective, then formulate an execution plan.
2. Execute step by step; use todo_write to track progress for multi-step tasks.
3. Verify correctness after each change (e.g. dotnet build).
4. If you encounter an error, analyze the cause and retry — max 2 attempts before reporting failure.

**Return format:** State completion status (success / partial / failed), list the main changes made and why, and clearly note anything incomplete or remaining."
        };
    }

    private async Task<string> RunLoopAsync(
        List<ChatMessage> messages,
        AppConfig config,
        CancellationToken ct)
    {
        var tools = _toolRegistry.GetDefinitions();
        var iterations = 0;

        while (iterations < MaxIterations)
        {
            ct.ThrowIfCancellationRequested();
            iterations++;

            var contentBuffer = new StringBuilder();
            var toolCallAccumulators = new Dictionary<int, (string id, string name, StringBuilder args)>();

            await foreach (var chunk in _client.StreamChatAsync(tools, messages, config, ct))
            {
                var delta = chunk.Choices?[0].Delta;
                if (delta == null) continue;

                if (!string.IsNullOrEmpty(delta.Content))
                    contentBuffer.Append(delta.Content);

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

            // 有工具调用
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

                messages.Add(ChatMessage.CreateAssistantWithToolCalls(toolCalls));

                foreach (var tc in toolCalls)
                {
                    var args = TryParseArguments(tc.Function.Arguments);
                    var context = new Tools.ToolCallContext
                    {
                        ToolName = tc.Function.Name,
                        ToolCallId = tc.Id,
                        Arguments = args
                    };

                    var result = await ExecuteSubagentToolAsync(tc.Function.Name, tc.Id, args);
                    messages.Add(ChatMessage.CreateToolResult(tc.Id, tc.Function.Name, result));
                }

                continue;
            }

            // 无工具调用 → 完成
            var content = contentBuffer.ToString();
            if (!string.IsNullOrEmpty(content))
                messages.Add(ChatMessage.CreateAssistant(content));

            return content;
        }

        return "子代理达到最大迭代次数，已停止";
    }

    /// <summary>执行子代理工具调用，工具未注册时返回错误而非抛异常</summary>
    private async Task<string> ExecuteSubagentToolAsync(
        string toolName,
        string toolCallId,
        Dictionary<string, object?> args)
    {
        var pipeline = BuildSubagentPipeline(toolName);
        if (pipeline == null)
            return $"错误: 工具 '{toolName}' 未注册";

        var context = new Tools.ToolCallContext
        {
            ToolName = toolName,
            ToolCallId = toolCallId,
            Arguments = args
        };

        return await pipeline.ExecuteAsync(context);
    }

    /// <summary>构建子代理工具管线，工具未注册时返回 null</summary>
    private Tools.ToolPipeline? BuildSubagentPipeline(string toolName)
    {
        var tool = _toolRegistry.GetTool(toolName);
        if (tool == null)
            return null;

        var pipeline = new Tools.ToolPipeline(tool, _workspaceService);

        // 权限过滤器：子代理模式 —— Allow/Ask 都通过，Deny 拦截
        pipeline.AddFilter(new Tools.PermissionPipelineFilter(_permissionManager, (name, command) =>
        {
            var level = _permissionManager.Check(name, command);
            // 子代理模式下 Ask 等同于 Allow（用户已授权子代理工作）
            return Task.FromResult<PermissionDecision?>(level != PermissionLevel.Deny 
                ? PermissionDecision.AllowOnce : PermissionDecision.Deny);
        }));

        // 日志过滤器
        pipeline.AddFilter(new Tools.LoggingFilter());

        // 超时过滤器
        pipeline.AddFilter(new Tools.TimeoutFilter(TimeoutMs));

        return pipeline;
    }

    private static Dictionary<string, object?> TryParseArguments(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                   ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            // JSON 解析失败，返回空字典让工具用默认参数
            return new Dictionary<string, object?>();
        }
    }

    private static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLen ? text : text[..maxLen] + "…";
    }
}
