using DeepSeekCode.Models;

namespace DeepSeekCode.Tools;

/// <summary>
/// read_skill — 按需读取技能全文。
/// 参考 Claude Code 的按需加载模式，AI 根据任务需要调用此工具获取技能正文。
/// </summary>
public class ReadSkillTool : ITool
{
    private readonly Skills.SkillEngine _skillEngine;

    public ReadSkillTool(Skills.SkillEngine skillEngine)
    {
        _skillEngine = skillEngine;
    }

    public string Name => "read_skill";
    public string Description => "按需读取指定技能的完整内容。当需要某个技能领域的专业知识时调用此工具";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["name"] = new PropertySchema
            {
                Type = "string",
                Description = "技能名称（来自可用技能列表）"
            }
        },
        Required = ["name"]
    };

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
        var name = arguments.TryGetValue("name", out var n) ? n?.ToString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult("错误: 请指定技能名称");

        var skill = _skillEngine.FindByName(name);
        if (skill == null)
            return Task.FromResult($"未找到技能 '{name}'。使用 /skills 查看可用技能列表。");

        if (!skill.Enabled)
            return Task.FromResult($"技能 '{name}' 已禁用。");

        var result = $"# {skill.Name}\n\n{skill.Description}\n\n{skill.Body}";
        if (result.Length > 8000)
            result = result[..8000] + "\n\n...(内容已截断)";
        return Task.FromResult(result);
    }
}
