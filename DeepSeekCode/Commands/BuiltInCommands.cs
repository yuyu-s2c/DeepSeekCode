using System.IO;
using System.Text.RegularExpressions;
using DeepSeekCode.Commands;
using DeepSeekCode.Models;
using DeepSeekCode.Session;
using DeepSeekCode.Skills;

namespace DeepSeekCode.Commands.BuiltIn;

/// <summary>/help — 显示可用命令列表</summary>
public class HelpCommand : ISlashCommand
{
    public string Name => "help";
    public string Description => "显示可用命令列表";
    public string Usage => "/help";
    public bool CanRunDuringStreaming => true;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        if (context.CommandRegistry == null)
            return Task.FromResult(CommandResult.Ok("命令注册中心不可用"));

        var commands = context.CommandRegistry.GetAll();
        var sb = new System.Text.StringBuilder();

        // 查看具体命令
        if (!string.IsNullOrWhiteSpace(args))
        {
            var cmd = context.CommandRegistry.Find(args.Trim());
            if (cmd != null)
            {
                sb.AppendLine($"## /{cmd.Name}");
                sb.AppendLine($"- {cmd.Description}");
                sb.AppendLine($"- 用法: `{cmd.Usage}`");
            }
            else
                sb.AppendLine($"未找到命令 '{args.Trim()}'");
            return Task.FromResult(CommandResult.Ok(sb.ToString()));
        }

        sb.AppendLine("## 可用命令\n");
        foreach (var cmd in commands)
        {
            sb.AppendLine($"- **/{cmd.Name}** — {cmd.Description}");
            sb.AppendLine($"  用法: `{cmd.Usage}`");
        }
        sb.AppendLine("\n---\n输入 `/` 可触发命令补全。");

        return Task.FromResult(CommandResult.Ok(sb.ToString()));
    }
}

/// <summary>/clear — 清空当前对话</summary>
public class ClearCommand : ISlashCommand
{
    public string Name => "clear";
    public string Description => "清空当前对话";
    public string Usage => "/clear";
    public bool CanRunDuringStreaming => false;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        context.Conversation?.ClearConversation();
        return Task.FromResult(CommandResult.Ok("对话已清空", refresh: true));
    }
}

/// <summary>/model — 切换模型</summary>
public class ModelCommand : ISlashCommand
{
    public string Name => "model";
    public string Description => "切换 AI 模型";
    public string Usage => "/model [deepseek-v4-pro|deepseek-v4-flash]";
    public bool CanRunDuringStreaming => false;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        var validModels = new[] { "deepseek-v4-pro", "deepseek-v4-flash" };

        if (string.IsNullOrWhiteSpace(args))
        {
            var current = context.Config?.Config.Model ?? "未知";
            return Task.FromResult(CommandResult.Ok(
                $"当前模型: **{current}**\n\n可用模型:\n- deepseek-v4-pro（高精度）\n- deepseek-v4-flash（快速）"));
        }

        var model = args.Trim().ToLower();
        if (!validModels.Contains(model))
            return Task.FromResult(CommandResult.Ok(
                $"无效模型 '{model}'，可用: {string.Join(", ", validModels)}"));

        var config = context.Config?.Config;
        if (config != null)
        {
            config.Model = model;
            context.Config!.Save(config);
        }

        context.EventBus?.Publish(new Services.ConfigChangedEvent { Model = model });
        return Task.FromResult(CommandResult.Ok($"已切换至 **{model}**", refresh: true));
    }
}

/// <summary>/save — 保存当前会话</summary>
public class SaveCommand : ISlashCommand
{
    public string Name => "save";
    public string Description => "保存当前会话";
    public string Usage => "/save [会话名称]";
    public bool CanRunDuringStreaming => true;

    public async Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        if (context.SessionStore == null)
            return CommandResult.Ok("会话存储不可用");
        if (context.Conversation == null)
            return CommandResult.Ok("对话管理器不可用");

        var messages = context.Conversation.Messages;
        if (messages.Count <= 1)
            return CommandResult.Ok("对话为空，无需保存");

        var title = string.IsNullOrWhiteSpace(args)
            ? GenerateSessionTitle(context)
            : args.Trim();

        // 如果有当前会话 ID，覆盖而非新建
        var sessionId = context.CurrentSessionId ?? Guid.NewGuid().ToString("N")[..8];
        context.CurrentSessionId = sessionId;

        var metadata = new Session.SessionMetadata
        {
            Id = sessionId,
            Title = title,
            MessageCount = messages.Count,
            Model = context.Config?.Config.Model ?? "deepseek-v4-pro"
        };

        var messagesJson = context.Conversation.SerializeSession();
        await context.SessionStore.SaveAsync(metadata, messagesJson);
        return CommandResult.Ok($"会话已保存: **{title}** (ID: {metadata.Id})");
    }

    /// <summary>从第一句用户消息自动生成会话标题</summary>
    private static string GenerateSessionTitle(CommandContext context)
    {
        var firstUserMsg = context.Conversation?.Messages
            .FirstOrDefault(m => m.Role == "user")?.Content;

        if (string.IsNullOrWhiteSpace(firstUserMsg))
        {
            var workspaceName = Path.GetFileName(
                (context.WorkspaceService?.WorkspacePath ?? "").TrimEnd(Path.DirectorySeparatorChar));
            return $"{workspaceName}_{DateTime.Now:MMdd_HHmm}";
        }

        var title = firstUserMsg.Length > 30
            ? firstUserMsg[..30] + "..."
            : firstUserMsg;

        title = Regex.Replace(title, @"\s+", " ").Trim();
        return title;
    }
}

/// <summary>/load — 加载历史会话</summary>
public class LoadCommand : ISlashCommand
{
    public string Name => "load";
    public string Description => "加载历史会话";
    public string Usage => "/load [会话ID 或名称前缀]";
    public bool CanRunDuringStreaming => false;

    public async Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        if (context.SessionStore == null)
            return CommandResult.Ok("会话存储不可用");

        var sessions = await context.SessionStore.ListSessionsAsync();

        // 无参数 → 列出所有会话
        if (string.IsNullOrWhiteSpace(args))
        {
            if (sessions.Count == 0)
                return CommandResult.Ok("没有已保存的会话。使用 /save 保存当前对话。");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## 已保存的会话\n");
            foreach (var s in sessions)
            {
                var time = s.UpdatedAt.ToString("MM-dd HH:mm");
                sb.AppendLine($"- `{s.Id}` — **{s.Title}** ({s.MessageCount} 条消息, {time})");
            }
            sb.AppendLine("\n使用 `/load <ID>` 加载指定会话");
            return CommandResult.Ok(sb.ToString());
        }

        // 按 ID 精确匹配
        var arg = args.Trim();
        var match = sessions.Find(s => s.Id == arg)
                    ?? sessions.Find(s => s.Title.Contains(arg, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return CommandResult.Ok($"未找到会话 '{arg}'。输入 /load 查看列表。");

        var loaded = await context.SessionStore.LoadAsync(match.Id);
        if (loaded == null)
            return CommandResult.Ok("会话加载失败");

        context.Conversation?.DeserializeSession(loaded.MessagesJson);
        return CommandResult.Ok($"已加载会话: **{match.Title}** ({match.MessageCount} 条消息)", refresh: true);
    }
}

/// <summary>/config — 查看或修改配置</summary>
public class ConfigCommand : ISlashCommand
{
    public string Name => "config";
    public string Description => "查看或修改配置";
    public string Usage => "/config [key] [value]";
    public bool CanRunDuringStreaming => true;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        // TODO: 读取/修改 ConfigService 中的配置项
        return Task.FromResult(CommandResult.Ok("/config 功能开发中"));
    }
}

/// <summary>/compact — 压缩上下文窗口</summary>
public class CompactCommand : ISlashCommand
{
    public string Name => "compact";
    public string Description => "压缩上下文窗口，释放 token";
    public string Usage => "/compact";
    public bool CanRunDuringStreaming => false;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        // TODO: 对历史对话做压缩摘要
        return Task.FromResult(CommandResult.Ok("/compact 功能开发中"));
    }
}

/// <summary>/settings — 打开设置窗口</summary>
public class SettingsCommand : ISlashCommand
{
    public string Name => "settings";
    public string Description => "打开设置窗口";
    public string Usage => "/settings";
    public bool CanRunDuringStreaming => true;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        // 通过事件总线触发 MainWindow 打开设置窗口
        context.EventBus?.Publish(new Services.OpenSettingsRequestedEvent());
        return Task.FromResult(CommandResult.Ok(null));
    }
}

/// <summary>/workspace — 查看或切换工作区</summary>
public class WorkspaceCommand : ISlashCommand
{
    public string Name => "workspace";
    public string Description => "查看或切换工作区目录";
    public string Usage => "/workspace [路径]";
    public bool CanRunDuringStreaming => true;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        if (context.WorkspaceService == null)
            return Task.FromResult(CommandResult.Ok("工作区服务不可用"));

        if (string.IsNullOrWhiteSpace(args))
        {
            var path = context.WorkspaceService.WorkspacePath;
            var name = context.WorkspaceService.WorkspaceName;
            return Task.FromResult(CommandResult.Ok($"当前工作区: **{name}**\n路径: `{path}`"));
        }

        try
        {
            context.WorkspaceService.SetWorkspace(args.Trim());
            return Task.FromResult(CommandResult.Ok(
                $"工作区已切换为: **{context.WorkspaceService.WorkspaceName}**\n路径: `{context.WorkspaceService.WorkspacePath}`",
                refresh: true));
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult(CommandResult.Ok($"目录不存在: {args.Trim()}"));
        }
    }
}

/// <summary>/skills — 列出已加载的技能</summary>
public class SkillsCommand : ISlashCommand
{
    public string Name => "skills";
    public string Description => "列出或查看已加载的技能";
    public string Usage => "/skills [技能名称]";
    public bool CanRunDuringStreaming => true;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        if (context.SkillEngine == null)
            return Task.FromResult(CommandResult.Ok("技能引擎不可用"));

        var arg = args.Trim();

        // 查看具体技能
        if (!string.IsNullOrWhiteSpace(arg))
        {
            var skill = context.SkillEngine.FindByName(arg);
            if (skill == null)
                return Task.FromResult(CommandResult.Ok($"未找到技能 '{arg}'"));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"## {skill.Name}");
            sb.AppendLine($"- {skill.Description}");
            if (!string.IsNullOrWhiteSpace(skill.Author))
                sb.AppendLine($"- 作者: {skill.Author}");
            sb.AppendLine($"- 版本: {skill.Version}");
            sb.AppendLine($"- 状态: {(skill.Enabled ? "启用" : "禁用")}");
            sb.AppendLine($"- 文件: `{skill.FilePath}`");
            return Task.FromResult(CommandResult.Ok(sb.ToString()));
        }

        // 列出所有技能
        var allSkills = context.SkillEngine.GetAllSkills();
        if (allSkills.Count == 0)
        {
            var dirs = new[]
            {
                Path.Combine(context.WorkspaceService?.WorkspacePath ?? "",
                    ".deepseek-code", "skills"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".deepseek-code", "skills")
            };

            var dirList = string.Join("\n", dirs.Select(d => $"- `{d}`"));
            return Task.FromResult(CommandResult.Ok(
                $"没有已加载的技能。\n\n将 `.md` 技能文件放入以下目录即可自动加载：\n\n{dirList}"));
        }

        var sb2 = new System.Text.StringBuilder();
        sb2.AppendLine("## 已加载的技能\n");
        foreach (var s in allSkills)
        {
            var tag = s.Enabled ? "启用" : "禁用";
            sb2.AppendLine($"- **{s.Name}** [{tag}] — {s.Description}");
        }
        sb2.AppendLine("\n使用 `/skills <名称>` 查看技能详情");

        return Task.FromResult(CommandResult.Ok(sb2.ToString()));
    }
}
