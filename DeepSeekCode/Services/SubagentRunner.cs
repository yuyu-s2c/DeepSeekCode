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

    private const int MaxIterations = 15;
    private const int TimeoutMs = 120000;
    private const int MaxContextTokens = 16000;

    public SubagentRunner(
        DeepSeekClient client,
        Tools.ToolRegistry toolRegistry,
        PermissionManager permissionManager,
        WorkspaceService workspaceService,
        ConfigService configService,
        EventBus eventBus)
    {
        _client = client;
        _toolRegistry = toolRegistry;
        _permissionManager = permissionManager;
        _workspaceService = workspaceService;
        _configService = configService;
        _eventBus = eventBus;
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
            Model = main.Model,
            MaxTokens = main.MaxTokens,
            ThinkingEnabled = enableThinking,
            ReasoningEffort = enableThinking ? "medium" : "",
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
        var basePrompt = $"你是 DeepSeek Code 子代理。工作区: {_workspaceService.WorkspacePath}\n";

        return subagentType switch
        {
            "explore" => basePrompt +
                "模式：只读探索。只能用 read_file / glob / grep 工具查询代码，禁止修改文件。给出准确、结构化的回答。完成任务后直接返回结果。",
            _ => basePrompt +
                "模式：通用。完成任务后直接返回结果，简洁明了。可以读取和修改文件。"
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

                    var pipeline = BuildSubagentPipeline(tc.Function.Name, args);
                    var result = await pipeline.ExecuteAsync(context);

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

    private Tools.ToolPipeline BuildSubagentPipeline(
        string toolName,
        Dictionary<string, object?> args)
    {
        var tool = _toolRegistry.GetTool(toolName);
        if (tool == null)
            throw new InvalidOperationException($"工具未注册: {toolName}");

        var pipeline = new Tools.ToolPipeline(tool, _workspaceService);

        // 权限过滤器：子代理模式 —— Allow/Ask 都通过，Deny 拦截
        pipeline.AddFilter(new Tools.PermissionPipelineFilter(_permissionManager, name =>
        {
            var level = _permissionManager.Check(name,
                args.TryGetValue("command", out var c) ? c?.ToString() : null);

            // 子代理模式下 Ask 等同于 Allow（用户已授权子代理工作）
            return Task.FromResult<bool?>(level != PermissionLevel.Deny);
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
