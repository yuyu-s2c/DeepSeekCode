using System.Threading;
using DeepSeekCode.Models;
using DeepSeekCode.Services;

namespace DeepSeekCode.Tools;

public class TaskTool : ITool
{
    private readonly SubagentRunner _runner;
    private int _taskCounter;

    public string Name => "task";
    public string Description => "Launch a new subagent to handle complex, multi-step tasks autonomously in a background thread pool.\n- Each subagent has its own isolated conversation context and tool access.\n- subagent_type: \"explore\" for read-only code search (uses flash model, no thinking — fast), \"general\" for full read/write access (uses main model with thinking — thorough).\n- description: a short (3-5 word) label for the task, shown in the UI.\n- prompt: the detailed task description — the subagent sees ONLY this and executes it.\n- Multiple task tools can run in parallel with Task.WhenAll for independent work.\n- The subagent returns its final text as the tool result; relay what matters to the user.\n- Subagent context windows are limited (16K tokens) — keep prompts focused and scoped.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["description"] = new PropertySchema
            {
                Type = "string",
                Description = "A short (3-5 word) description of the task, shown in the UI"
            },
            ["prompt"] = new PropertySchema
            {
                Type = "string",
                Description = "The detailed task instructions for the subagent. The subagent sees ONLY this prompt and executes it."
            },
            ["subagent_type"] = new PropertySchema
            {
                Type = "string",
                Description = "Subagent type: explore (read-only, flash model, fast) or general (full access, main model, thorough)",
                Enum = new() { "explore", "general" }
            }
        },
        Required = new() { "description", "prompt" }
    };

    public TaskTool(SubagentRunner runner)
    {
        _runner = runner;
    }

    public ToolDefinition ToDefinition() => new()
    {
        Function = new FunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = Parameters
        }
    };

    public async Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var description = arguments.TryGetValue("description", out var d) ? d?.ToString() ?? "未知任务" : "未知任务";
        var prompt = arguments.TryGetValue("prompt", out var p) ? p?.ToString() ?? "" : "";
        var subagentType = arguments.TryGetValue("subagent_type", out var t)
            ? t?.ToString()?.ToLowerInvariant() ?? "explore" : "explore";

        if (string.IsNullOrWhiteSpace(prompt))
            return "错误: prompt 参数不能为空";

        // 从参数中提取取消令牌（由主对话循环注入）
        var ct = CancellationToken.None;
        if (arguments.TryGetValue("___ct___", out var ctObj) && ctObj is CancellationToken token)
            ct = token;

        var taskId = $"task_{Interlocked.Increment(ref _taskCounter):D3}";

        // 抛到线程池执行，释放 UI 线程
        var capturedCt = ct;
        return await Task.Run(() =>
            _runner.RunAsync(taskId, description, subagentType, prompt, capturedCt), ct);
    }
}
