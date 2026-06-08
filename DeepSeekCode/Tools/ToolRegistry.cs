using System.Text.Json;
using DeepSeekCode.Models;

namespace DeepSeekCode.Tools;

public class ToolRegistry
{
    private readonly List<ITool> _tools = new();

    public void Register(ITool tool) => _tools.Add(tool);

    public ITool? GetTool(string name) => _tools.Find(t => t.Name == name);

    public List<ToolDefinition> GetDefinitions() => _tools.ConvertAll(t => t.ToDefinition());

    public async Task<string> ExecuteToolCallAsync(ToolCall toolCall)
    {
        var tool = _tools.Find(t => t.Name == toolCall.Function.Name);
        if (tool == null)
            return $"错误: 未找到工具 '{toolCall.Function.Name}'";

        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                toolCall.Function.Arguments);
            return await tool.ExecuteAsync(args ?? new());
        }
        catch (Exception ex)
        {
            return $"工具执行异常: {ex.Message}";
        }
    }
}
