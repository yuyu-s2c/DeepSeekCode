using System.IO;

namespace DeepSeekCode.Services;

/// <summary>
/// 项目指令服务：自动发现并读取 DEEPSEEK.md 文件，注入系统提示词。
///
/// 查找优先级（仅取第一个存在的文件）：
///   项目级: {workspace}/DEEPSEEK.md
///   用户级: ~\.deepseek-code\DEEPSEEK.md
/// </summary>
public class ProjectInstructionService
{
    private readonly WorkspaceService _workspaceService;
    private static readonly string UserConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepseek-code");

    public ProjectInstructionService(WorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    /// <summary>
    /// 构建完整的项目指令上下文（项目级 + 用户级），返回可注入到系统提示词的 Markdown 片段。
    /// 无指令文件时返回 null。
    /// </summary>
    public string? BuildInstructionContext()
    {
        var parts = new List<string>();

        // 项目级 DEEPSEEK.md
        var projectInstruction = ReadProjectInstruction();
        if (!string.IsNullOrWhiteSpace(projectInstruction))
            parts.Add(projectInstruction);

        // 用户级 DEEPSEEK.md
        var userInstruction = ReadUserInstruction();
        if (!string.IsNullOrWhiteSpace(userInstruction))
            parts.Add(userInstruction);

        if (parts.Count == 0) return null;

        return "## 项目指令（来自 DEEPSEEK.md）\n\n" + string.Join("\n\n---\n\n", parts);
    }

    /// <summary>
    /// 读取项目根目录的 DEEPSEEK.md
    /// </summary>
    public string? ReadProjectInstruction()
    {
        var path = Path.Combine(_workspaceService.WorkspacePath, "DEEPSEEK.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// 读取用户目录的 DEEPSEEK.md
    /// </summary>
    public string? ReadUserInstruction()
    {
        var path = Path.Combine(UserConfigDir, "DEEPSEEK.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// 检查项目中是否存在 DEEPSEEK.md
    /// </summary>
    public bool HasProjectInstruction()
    {
        return File.Exists(Path.Combine(_workspaceService.WorkspacePath, "DEEPSEEK.md"));
    }
}
