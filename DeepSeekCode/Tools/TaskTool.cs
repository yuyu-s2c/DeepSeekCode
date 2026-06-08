using System.Threading;
using DeepSeekCode.Models;
using DeepSeekCode.Services;

namespace DeepSeekCode.Tools;

public class TaskTool : ITool
{
    private readonly SubagentRunner _runner;
    private int _taskCounter;

    public string Name => "task";
    public string Description => "启动子代理处理独立的复杂任务。每个子代理拥有独立对话上下文和工具访问权限";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["description"] = new PropertySchema
            {
                Type = "string",
                Description = "任务简短描述（用于 UI 显示，3-5 个字）"
            },
            ["prompt"] = new PropertySchema
            {
                Type = "string",
                Description = "详细的子任务指令。子代理只执行此任务，完成后返回结果"
            },
            ["subagent_type"] = new PropertySchema
            {
                Type = "string",
                Description = "子代理类型。explore=只读探索, general=完整权限",
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
