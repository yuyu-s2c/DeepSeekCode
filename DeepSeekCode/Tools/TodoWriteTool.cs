using System.Text.Json;
using DeepSeekCode.Models;
using DeepSeekCode.Services;

namespace DeepSeekCode.Tools;

public class TodoWriteTool : ITool
{
    private readonly EventBus _eventBus;

    public string Name => "todo_write";

    public string Description => "Creates and maintains a structured task list for the current coding session.\n- Each call REPLACES the entire task list — send the full list of current tasks every time.\n- Each task has: content (description), status (pending / in_progress / completed / cancelled), and priority (high / medium / low).\n- Mark the current task in_progress BEFORE beginning work on it. Only ONE task in_progress at a time.\n- Mark a task completed ONLY after the work is actually done and verified (tests pass, build succeeds).\n- When a new task is discovered during work, add it to the next update.\n- Use proactively for any task requiring 3+ distinct steps. Skip for single straightforward actions.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["todos"] = new PropertySchema
            {
                Type = "array",
                Description = "The complete task list. Each call REPLACES the entire list.",
                Items = new PropertySchema
                {
                    Type = "object",
                    Properties = new()
                    {
                        ["content"] = new PropertySchema
                        {
                            Type = "string",
                            Description = "Brief description of the task"
                        },
                        ["status"] = new PropertySchema
                        {
                            Type = "string",
                            Description = "Current status: pending, in_progress, completed, or cancelled",
                            Enum = new() { "pending", "in_progress", "completed", "cancelled" }
                        },
                        ["priority"] = new PropertySchema
                        {
                            Type = "string",
                            Description = "Priority level: high, medium, or low",
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
