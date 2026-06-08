using System.Text.Json.Serialization;

namespace DeepSeekCode.Models;

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("reasoning_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    public static ChatMessage CreateUser(string content) => new()
    {
        Role = "user",
        Content = content
    };

    public static ChatMessage CreateAssistant(string content) => new()
    {
        Role = "assistant",
        Content = content
    };

    public static ChatMessage CreateAssistantWithToolCalls(List<ToolCall> toolCalls) => new()
    {
        Role = "assistant",
        ToolCalls = toolCalls
    };

    public static ChatMessage CreateToolResult(string toolCallId, string toolName, string content) => new()
    {
        Role = "tool",
        ToolCallId = toolCallId,
        Name = toolName,
        Content = content
    };

    public static ChatMessage CreateSystem(string content) => new()
    {
        Role = "system",
        Content = content
    };

    public string DisplayContent
    {
        get
        {
            if (ToolCalls != null && ToolCalls.Count > 0)
            {
                var names = string.Join(", ", ToolCalls.Select(t => t.Function.Name));
                return $"[调用工具: {names}]";
            }
            return Content ?? "";
        }
    }
}

public class ToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; set; }

    [JsonPropertyName("function")]
    public FunctionCall Function { get; set; } = new();
}

public class FunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";
}
