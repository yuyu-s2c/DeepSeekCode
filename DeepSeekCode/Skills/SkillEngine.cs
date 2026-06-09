using System.IO;
using System.Text;

namespace DeepSeekCode.Skills;

/// <summary>
/// 技能元数据
/// </summary>
public class SkillMetadata
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string? Author { get; set; }
    public string FilePath { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime LoadedAt { get; set; } = DateTime.Now;
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 技能引擎 — 加载 Markdown+Frontmatter 技能文件，注入系统提示词。
///
/// 技能文件格式：
///   ---
///   name: 技能名
///   description: 简短描述
///   version: 1.0
///   author: 作者名
///   ---
///   (技能正文 Markdown)
///
/// 加载路径：
///   用户级: ~\.deepseek-code\skills\*.md
///   项目级: {workspace}\.deepseek-code\skills\*.md
/// </summary>
public class SkillEngine
{
    private readonly List<SkillMetadata> _userSkills = new();
    private readonly List<SkillMetadata> _projectSkills = new();

    public IReadOnlyList<SkillMetadata> UserSkills => _userSkills.AsReadOnly();
    public IReadOnlyList<SkillMetadata> ProjectSkills => _projectSkills.AsReadOnly();

    /// <summary>
    /// 从目录加载所有 .md 技能文件
    /// </summary>
    public List<SkillMetadata> LoadFromDirectory(string directory, bool projectLevel = false)
    {
        var loaded = new List<SkillMetadata>();

        if (!Directory.Exists(directory))
            return loaded;

        foreach (var file in Directory.GetFiles(directory, "*.md"))
        {
            var skill = LoadSkillFile(file);
            if (skill != null)
                loaded.Add(skill);
        }

        if (projectLevel)
        {
            _projectSkills.Clear();
            _projectSkills.AddRange(loaded);
        }
        else
        {
            _userSkills.Clear();
            _userSkills.AddRange(loaded);
        }

        return loaded;
    }

    /// <summary>
    /// 加载单个技能文件
    /// </summary>
    public SkillMetadata? LoadSkillFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            var (metadata, body) = ParseFrontmatter(content, filePath);

            if (string.IsNullOrWhiteSpace(metadata.Name))
                metadata.Name = Path.GetFileNameWithoutExtension(filePath);

            metadata.FilePath = filePath;
            metadata.Body = body;
            metadata.LoadedAt = DateTime.Now;

            return metadata;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 生成注入到系统提示词的技能声明（全量正文，已废弃，改用 GenerateSkillsIndex）
    /// </summary>
    public string GenerateSkillsPrompt()
    {
        var allSkills = GetAllEnabledSkills();
        if (allSkills.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## 已加载技能");
        sb.AppendLine();

        foreach (var skill in allSkills)
        {
            sb.AppendLine($"### {skill.Name}");
            if (!string.IsNullOrWhiteSpace(skill.Description))
                sb.AppendLine($"_{skill.Description}_");
            sb.AppendLine();
            sb.AppendLine(skill.Body.Trim());
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 生成技能索引，注入系统提示词。全量输出所有已启用技能的 name + description，
    /// 正文通过 read_skill 工具按需获取。参考 Claude Code 的全量注入模式。
    /// </summary>
    public string GenerateSkillsIndex()
    {
        var allSkills = GetAllEnabledSkills();
        if (allSkills.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Available Skills");
        sb.AppendLine("The following skills are available for use with the read_skill tool. When a task falls into a skill's domain, use read_skill to load the full instructions before proceeding.");
        sb.AppendLine();

        foreach (var skill in allSkills)
        {
            sb.AppendLine($"- **{skill.Name}**");
            if (!string.IsNullOrWhiteSpace(skill.Description))
                sb.AppendLine($"  {skill.Description}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 列出所有已加载技能（项目级优先）
    /// </summary>
    public IReadOnlyList<SkillMetadata> GetAllSkills()
    {
        var all = new List<SkillMetadata>();
        all.AddRange(_projectSkills);
        all.AddRange(_userSkills.Where(u =>
            _projectSkills.All(p => !string.Equals(p.Name, u.Name, StringComparison.OrdinalIgnoreCase))));
        return all.AsReadOnly();
    }

    /// <summary>
    /// 获取所有启用的技能
    /// </summary>
    public List<SkillMetadata> GetAllEnabledSkills()
    {
        return GetAllSkills().Where(s => s.Enabled).ToList();
    }

    /// <summary>
    /// 按名称查找技能
    /// </summary>
    public SkillMetadata? FindByName(string name)
    {
        return GetAllSkills().FirstOrDefault(
            s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // ═══ Frontmatter 解析 ═══

    private static (SkillMetadata metadata, string body) ParseFrontmatter(string content, string filePath)
    {
        var metadata = new SkillMetadata { FilePath = filePath };

        if (!content.StartsWith("---"))
            return (metadata, content.Trim());

        var endIndex = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIndex < 0)
            return (metadata, content.Trim());

        var frontmatter = content[4..endIndex]; // skip "---\n"
        var body = content[(endIndex + 4)..].Trim(); // skip "\n---"

        foreach (var line in frontmatter.Split('\n'))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0) continue;

            var key = line[..colonIndex].Trim().ToLowerInvariant();
            var value = line[(colonIndex + 1)..].Trim().Trim('"', '\'');

            switch (key)
            {
                case "name":
                    metadata.Name = value;
                    break;
                case "description":
                    metadata.Description = value;
                    break;
                case "version":
                    metadata.Version = value;
                    break;
                case "author":
                    metadata.Author = value;
                    break;
            }
        }

        return (metadata, body);
    }
}
