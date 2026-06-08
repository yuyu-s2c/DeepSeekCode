namespace DeepSeekCode.Services;

/// <summary>
/// 权限决策结果
/// </summary>
public enum PermissionDecision
{
    Deny,
    AllowOnce,
    AllowAll
}

/// <summary>
/// 权限等级
/// </summary>
public enum PermissionLevel
{
    /// <summary>始终禁止</summary>
    Deny,

    /// <summary>始终允许</summary>
    Allow,

    /// <summary>每次询问用户</summary>
    Ask
}

/// <summary>
/// 权限规则：定义某个工具在特定条件下的权限等级
/// </summary>
public class PermissionRule
{
    public string ToolName { get; set; } = "";
    public string? Pattern { get; set; }
    public PermissionLevel Level { get; set; } = PermissionLevel.Ask;
    public string? Description { get; set; }

    public bool Matches(string toolName, string? command = null)
    {
        if (!string.Equals(ToolName, toolName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Pattern == null)
            return true;

        if (command == null)
            return false;

        // 只匹配命令的第一个词（命令名），避免子串误伤
        // 如 "del " 不应命中 "dotnet publish -o ./delivery"
        var firstWord = command.TrimStart().Split(' ')[0];
        return string.Equals(firstWord, Pattern.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 权限管理器：根据规则决定工具是否可执行。
/// 支持会话级"全部允许"临时覆盖。
/// </summary>
public class PermissionManager
{
    private readonly List<PermissionRule> _rules = new();
    private bool _sessionAllowAll;

    public PermissionManager()
    {
        AddDefaultRules();
    }

    /// <summary>当前会话内全部允许（用户选择"允许本轮所有操作"后生效）</summary>
    public void AllowAllForSession() => _sessionAllowAll = true;

    /// <summary>重置会话状态（新对话、加载历史、切换工作区时调用）</summary>
    public void ResetSession() => _sessionAllowAll = false;

    private void AddDefaultRules()
    {
        // 只读工具始终允许
        _rules.Add(new PermissionRule
        {
            ToolName = "read_file",
            Level = PermissionLevel.Allow,
            Description = "读取文件始终安全"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "webfetch",
            Level = PermissionLevel.Allow,
            Description = "网页抓取始终安全（只读操作）"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "read_skill",
            Level = PermissionLevel.Allow,
            Description = "读取技能文件是只读操作"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "git_diff",
            Level = PermissionLevel.Allow,
            Description = "查看 Git 变更是只读操作"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "git_log",
            Level = PermissionLevel.Allow,
            Description = "查看 Git 历史是只读操作"
        });

        // Git commit 需要确认
        _rules.Add(new PermissionRule
        {
            ToolName = "git_commit",
            Level = PermissionLevel.Ask,
            Description = "Git 提交需要确认"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "glob",
            Level = PermissionLevel.Allow,
            Description = "文件搜索始终安全"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "grep",
            Level = PermissionLevel.Allow,
            Description = "代码搜索始终安全"
        });

        // 危险 shell 命令默认禁止（匹配命令名）
        _rules.Add(new PermissionRule
        {
            ToolName = "shell",
            Pattern = "rm",
            Level = PermissionLevel.Deny,
            Description = "rm 删除命令默认禁止"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "shell",
            Pattern = "del",
            Level = PermissionLevel.Deny,
            Description = "del 删除命令默认禁止"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "shell",
            Pattern = "format",
            Level = PermissionLevel.Deny,
            Description = "format 格式化命令默认禁止"
        });

        // 写文件工具需要询问
        _rules.Add(new PermissionRule
        {
            ToolName = "edit_file",
            Level = PermissionLevel.Ask,
            Description = "编辑文件需要确认"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "write_file",
            Level = PermissionLevel.Ask,
            Description = "写入文件需要确认"
        });
        _rules.Add(new PermissionRule
        {
            ToolName = "shell",
            Level = PermissionLevel.Ask,
            Description = "Shell 命令需要确认"
        });
    }

    public void AddRule(PermissionRule rule)
    {
        _rules.Insert(0, rule);
    }

    public void RemoveRule(string toolName, string? pattern = null)
    {
        _rules.RemoveAll(r =>
            string.Equals(r.ToolName, toolName, StringComparison.OrdinalIgnoreCase) &&
            r.Pattern == pattern);
    }

    public PermissionLevel Check(string toolName, string? command = null)
    {
        // 会话级全部允许（Deny 规则仍然生效）
        if (_sessionAllowAll)
        {
            foreach (var rule in _rules)
            {
                if (rule.Matches(toolName, command) && rule.Level == PermissionLevel.Deny)
                    return PermissionLevel.Deny;
            }
            return PermissionLevel.Allow;
        }

        foreach (var rule in _rules)
        {
            if (rule.Matches(toolName, command))
                return rule.Level;
        }
        return PermissionLevel.Ask;
    }

    public IReadOnlyList<PermissionRule> GetRules() => _rules.AsReadOnly();
}