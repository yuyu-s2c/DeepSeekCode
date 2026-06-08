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
    public string Description => "Loads the full content of a skill on demand.\n- name must match a skill from the available skills list shown in the system context.\n- Use this when a task falls into a skill's domain and you need the detailed instructions.\n- Returns the skill's frontmatter metadata and full body (capped at 8000 characters).\n- Do NOT call this for skills already loaded in the current conversation.\n- If a skill name is not found, use /skills to see the available list.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["name"] = new PropertySchema
            {
                Type = "string",
                Description = "The name of the skill to load (from the available skills list)"
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
