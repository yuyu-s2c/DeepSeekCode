using DeepSeekCode.Models;

namespace DeepSeekCode.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    ParameterSchema Parameters { get; }
    ToolDefinition ToDefinition();
    Task<string> ExecuteAsync(Dictionary<string, object?> arguments);
}
