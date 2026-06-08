using System.Text.Json;
using DeepSeekCode.Models;
using DeepSeekCode.Services;

namespace DeepSeekCode.Tools;

public class TodoWriteTool : ITool
{
    private readonly EventBus _eventBus;

    public string Name => "todo_write";

    public string Description => "创建或更新任务列表。每次调用会替换整个任务列表";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["todos"] = new PropertySchema
            {
                Type = "array",
                Description = "任务列表，每次调用会完整替换",
                Items = new PropertySchema
                {
                    Type = "object",
                    Properties = new()
                    {
                        ["content"] = new PropertySchema
                        {
                            Type = "string",
                            Description = "任务描述"
                        },
                        ["status"] = new PropertySchema
                        {
                            Type = "string",
                            Description = "任务状态",
                            Enum = new() { "pending", "in_progress", "completed", "cancelled" }
                        },
                        ["priority"] = new PropertySchema
                        {
                            Type = "string",
                            Description = "优先级",
                            Enum = new() { "high", "medium", "low" }
                        }
                    },
                    Required = new() { "content", "status" }
                }
            }
        },
        Required = new() { "todos" }
    };

    public TodoWriteTool(EventBus eventBus)
    {
        _eventBus = eventBus;
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

    public Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var todos = new List<TodoItem>();

        if (arguments.TryGetValue("todos", out var todosObj) && todosObj != null)
        {
            try
            {
                var json = todosObj switch
                {
                    JsonElement je => je.GetRawText(),
                    string s => s,
                    _ => JsonSerializer.Serialize(todosObj)
                };

                var parsed = JsonSerializer.Deserialize<List<TodoItem>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed != null)
                    todos = parsed;
            }
            catch (Exception ex)
            {
                return Task.FromResult($"解析任务列表失败: {ex.Message}");
            }
        }

        _eventBus.Publish(new TodosUpdatedEvent { Todos = todos });

        var counts = $"总计 {todos.Count} 项";
        var completed = todos.Count(t => t.Status == TodoStatus.Completed);
        var inProgress = todos.Count(t => t.Status == TodoStatus.InProgress);
        var pending = todos.Count(t => t.Status == TodoStatus.Pending);

        var parts = new List<string> { counts };
        if (completed > 0) parts.Add($"已完成 {completed}");
        if (inProgress > 0) parts.Add($"进行中 {inProgress}");
        if (pending > 0) parts.Add($"待处理 {pending}");

        return Task.FromResult("任务列表已更新: " + string.Join("，", parts.Skip(1)));
    }
}
