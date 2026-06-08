using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepSeekCode.Models;

public class TodoItem
{
    public string Content { get; set; } = "";
    public TodoStatus Status { get; set; } = TodoStatus.Pending;
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;
}

[JsonConverter(typeof(TodoStatusConverter))]
public enum TodoStatus
{
    Pending,
    InProgress,
    Completed,
    Cancelled
}

[JsonConverter(typeof(TodoPriorityConverter))]
public enum TodoPriority
{
    High,
    Medium,
    Low
}

public class TodoStatusConverter : JsonConverter<TodoStatus>
{
    public override TodoStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString()?.ToLowerInvariant();
        return value switch
        {
            "pending" => TodoStatus.Pending,
            "in_progress" => TodoStatus.InProgress,
            "completed" => TodoStatus.Completed,
            "cancelled" => TodoStatus.Cancelled,
            _ => TodoStatus.Pending
        };
    }

    public override void Write(Utf8JsonWriter writer, TodoStatus value, JsonSerializerOptions options)
    {
        var str = value switch
        {
            TodoStatus.Pending => "pending",
            TodoStatus.InProgress => "in_progress",
            TodoStatus.Completed => "completed",
            TodoStatus.Cancelled => "cancelled",
            _ => "pending"
        };
        writer.WriteStringValue(str);
    }
}

public class TodoPriorityConverter : JsonConverter<TodoPriority>
{
    public override TodoPriority Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString()?.ToLowerInvariant();
        return value switch
        {
            "high" => TodoPriority.High,
            "medium" => TodoPriority.Medium,
            "low" => TodoPriority.Low,
            _ => TodoPriority.Medium
        };
    }

    public override void Write(Utf8JsonWriter writer, TodoPriority value, JsonSerializerOptions options)
    {
        var str = value switch
        {
            TodoPriority.High => "high",
            TodoPriority.Medium => "medium",
            TodoPriority.Low => "low",
            _ => "medium"
        };
        writer.WriteStringValue(str);
    }
}
