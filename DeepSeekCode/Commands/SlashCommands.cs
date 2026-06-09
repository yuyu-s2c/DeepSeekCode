namespace DeepSeekCode.Commands;

/// <summary>
/// Slash 命令接口（以 "/" 开头的命令）
/// </summary>
public interface ISlashCommand
{
    /// <summary>命令名称（不含 "/"）</summary>
    string Name { get; }

    /// <summary>简短描述</summary>
    string Description { get; }

    /// <summary>用法示例</summary>
    string Usage { get; }

    /// <summary>是否可以在流式输出中执行</summary>
    bool CanRunDuringStreaming { get; }

    /// <summary>执行命令，返回是否应该继续处理输入</summary>
    Task<CommandResult> ExecuteAsync(string args, CommandContext context);
}

/// <summary>
/// 命令执行上下文
/// </summary>
public class CommandContext
{
    public Services.ConversationManager? Conversation { get; init; }
    public Services.ConfigService? Config { get; init; }
    public Services.EventBus? EventBus { get; init; }
    public Session.ISessionStore? SessionStore { get; init; }
    public SlashCommandRegistry? CommandRegistry { get; init; }
    public Services.WorkspaceService? WorkspaceService { get; init; }
    public Skills.SkillEngine? SkillEngine { get; init; }
    public Services.DeepSeekClient? DeepSeekClient { get; init; }
    /// <summary>MCP 服务管理器</summary>
    public MCP.McpService? McpService { get; init; }
    /// <summary>Plan 模式服务</summary>
    public Services.PlanModeService? PlanMode { get; init; }
    /// <summary>当前正在编辑的会话 ID，保存时覆盖而非新建</summary>
    public string? CurrentSessionId { get; set; }
}

/// <summary>
/// 命令执行结果
/// </summary>
public class CommandResult
{
    /// <summary>是否已处理（true = 拦截，不再发给 AI）</summary>
    public bool Handled { get; init; } = true;

    /// <summary>要显示的消息（为空则不显示）</summary>
    public string? DisplayMessage { get; init; }

    /// <summary>是否需要刷新 UI</summary>
    public bool RefreshUI { get; init; }

    /// <summary>如果设置，替代原始用户输入发送给 AI（用于自定义命令模板替换）</summary>
    public string? UserPrompt { get; init; }

    public static CommandResult Ok(string? message = null, bool refresh = false)
        => new() { Handled = true, DisplayMessage = message, RefreshUI = refresh };

    public static CommandResult NotHandled()
        => new() { Handled = false };
}

/// <summary>
/// Slash 命令注册中心
/// </summary>
public class SlashCommandRegistry
{
    private readonly Dictionary<string, ISlashCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ISlashCommand command)
    {
        _commands[command.Name] = command;
    }

    public void Unregister(string name)
    {
        _commands.Remove(name);
    }

    public ISlashCommand? Find(string name)
    {
        return _commands.TryGetValue(name, out var cmd) ? cmd : null;
    }

    public bool IsCommand(string input)
    {
        return input.StartsWith('/') && _commands.ContainsKey(GetCommandName(input));
    }

    public async Task<CommandResult?> ExecuteAsync(string input, CommandContext context)
    {
        if (!input.StartsWith('/'))
            return null;

        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0][1..];
        var args = parts.Length > 1 ? parts[1] : "";

        var command = Find(name);
        if (command == null)
            return null;

        return await command.ExecuteAsync(args, context);
    }

    public IReadOnlyList<ISlashCommand> GetAll() => _commands.Values.ToList();

    private static string GetCommandName(string input)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0][1..] : "";
    }
}