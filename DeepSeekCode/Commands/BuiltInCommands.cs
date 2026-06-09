using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DeepSeekCode.Commands;
using DeepSeekCode.MCP;
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

    /// <summary>可修改的配置键及其说明</summary>
    private static readonly Dictionary<string, (string Label, string Desc)> ConfigKeys = new()
    {
        ["model"] = ("模型", "deepseek-v4-pro 或 deepseek-v4-flash"),
        ["maxTokens"] = ("最大 Token", "单次生成最大 token 数（256-384000，V4 最大输出 384K）"),
        ["thinkingEnabled"] = ("Thinking 模式", "true 或 false"),
        ["reasoningEffort"] = ("推理力度", "max / high / medium / low / min"),
        ["temperature"] = ("温度", "0.0-2.0，Thinking 模式无效"),
        ["topP"] = ("Top-P", "0.0-1.0，Thinking 模式无效"),
        ["frequencyPenalty"] = ("频率惩罚", "-2.0 到 2.0"),
        ["presencePenalty"] = ("存在惩罚", "-2.0 到 2.0"),
        ["apiBaseUrl"] = ("API 地址", "DeepSeek API 基地址"),
        ["enableJsonOutput"] = ("JSON 输出", "true 或 false"),
    };

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        if (context.Config == null)
            return Task.FromResult(CommandResult.Ok("配置服务不可用"));

        var config = context.Config.Config;
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        // /config → 列出所有配置
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(args))
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## 当前配置\n");
            foreach (var kv in ConfigKeys)
            {
                var value = GetConfigValue(config, kv.Key);
                sb.AppendLine($"- **{kv.Key}** ({kv.Value.Label}): `{value}`");
            }
            sb.AppendLine("\n使用 `/config <key> <value>` 修改配置");
            return Task.FromResult(CommandResult.Ok(sb.ToString()));
        }

        var key = parts[0].Trim().ToLower();

        // /config key → 查看单个配置
        if (parts.Length == 1)
        {
            if (!ConfigKeys.TryGetValue(key, out var info))
                return Task.FromResult(CommandResult.Ok($"未知配置项 '{key}'。输入 /config 查看可用配置项"));

            var value = GetConfigValue(config, key);
            return Task.FromResult(CommandResult.Ok($"**{key}** ({info.Label}): `{value}`\n{info.Desc}"));
        }

        // /config key value → 修改配置
        if (!ConfigKeys.ContainsKey(key))
            return Task.FromResult(CommandResult.Ok($"未知配置项 '{key}'。输入 /config 查看可用配置项"));

        var newValue = parts[1].Trim();
        var error = SetConfigValue(config, key, newValue);
        if (error != null)
            return Task.FromResult(CommandResult.Ok($"设置失败: {error}"));

        context.Config.Save(config);

        // 模型变更时发送事件
        if (key == "model")
            context.EventBus?.Publish(new Services.ConfigChangedEvent { Model = config.Model });

        return Task.FromResult(CommandResult.Ok($"**{key}** 已更新为 `{GetConfigValue(config, key)}`"));
    }

    private static string GetConfigValue(AppConfig config, string key) => key switch
    {
        "model" => config.Model,
        "maxTokens" => config.MaxTokens.ToString(),
        "thinkingEnabled" => config.ThinkingEnabled.ToString().ToLower(),
        "reasoningEffort" => config.ReasoningEffort,
        "temperature" => config.Temperature.ToString("F1"),
        "topP" => config.TopP.ToString("F1"),
        "frequencyPenalty" => config.FrequencyPenalty.ToString("F1"),
        "presencePenalty" => config.PresencePenalty.ToString("F1"),
        "apiBaseUrl" => config.ApiBaseUrl,
        "enableJsonOutput" => config.EnableJsonOutput.ToString().ToLower(),
        _ => "(未知)"
    };

    private static string? SetConfigValue(AppConfig config, string key, string value)
    {
        switch (key)
        {
            case "model":
                var validModels = new[] { "deepseek-v4-pro", "deepseek-v4-flash" };
                if (!validModels.Contains(value))
                    return $"无效模型 '{value}'，可用: {string.Join(", ", validModels)}";
                config.Model = value;
                break;
            case "maxTokens":
                if (!int.TryParse(value, out var tokens) || tokens < 256 || tokens > 384_000)
                    return "maxTokens 必须在 256-384000 之间（V4 最大输出 384K）";
                config.MaxTokens = tokens;
                break;
            case "thinkingEnabled":
                if (!bool.TryParse(value, out var thinking))
                    return "请输入 true 或 false";
                config.ThinkingEnabled = thinking;
                break;
            case "reasoningEffort":
                var validEfforts = new[] { "max", "high", "medium", "low", "min" };
                if (!validEfforts.Contains(value))
                    return $"无效推理力度 '{value}'，可用: {string.Join(", ", validEfforts)}";
                config.ReasoningEffort = value;
                break;
            case "temperature":
                if (!double.TryParse(value, out var temp) || temp < 0 || temp > 2)
                    return "temperature 必须在 0.0-2.0 之间";
                config.Temperature = temp;
                break;
            case "topP":
                if (!double.TryParse(value, out var topP) || topP < 0 || topP > 1)
                    return "topP 必须在 0.0-1.0 之间";
                config.TopP = topP;
                break;
            case "frequencyPenalty":
                if (!double.TryParse(value, out var fp) || fp < -2 || fp > 2)
                    return "frequencyPenalty 必须在 -2.0 到 2.0 之间";
                config.FrequencyPenalty = fp;
                break;
            case "presencePenalty":
                if (!double.TryParse(value, out var pp) || pp < -2 || pp > 2)
                    return "presencePenalty 必须在 -2.0 到 2.0 之间";
                config.PresencePenalty = pp;
                break;
            case "apiBaseUrl":
                if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                    return $"无效的 URL: {value}";
                config.ApiBaseUrl = value;
                break;
            case "enableJsonOutput":
                if (!bool.TryParse(value, out var json))
                    return "请输入 true 或 false";
                config.EnableJsonOutput = json;
                break;
            default:
                return $"不支持的配置项 '{key}'";
        }
        return null;
    }
}

/// <summary>/compact — 压缩上下文窗口</summary>
public class CompactCommand : ISlashCommand
{
    public string Name => "compact";
    public string Description => "压缩上下文窗口，释放 token";
    public string Usage => "/compact [保留轮数]";
    public bool CanRunDuringStreaming => false;

    public async Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        if (context.Conversation == null)
            return CommandResult.Ok("对话管理器不可用");

        var keepRounds = 3;
        if (!string.IsNullOrWhiteSpace(args) && int.TryParse(args.Trim(), out var n) && n > 0 && n <= 20)
            keepRounds = n;

        var beforeCount = context.Conversation.Messages.Count;
        await context.Conversation.CompactContextAsync(keepRounds);
        var afterCount = context.Conversation.Messages.Count;

        if (afterCount < beforeCount)
            return CommandResult.Ok(
                $"上下文已压缩: {beforeCount} → {afterCount} 条消息（保留最后 {keepRounds} 轮 + 历史摘要）",
                refresh: true);

        return CommandResult.Ok("上下文无需压缩（消息量不足以触发压缩）");
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

/// <summary>/mcp — 管理 MCP 服务器</summary>
public class McpCommand : ISlashCommand
{
    public string Name => "mcp";
    public string Description => "管理 MCP 服务器（列出/重载）";
    public string Usage => "/mcp [list|reload]";
    public bool CanRunDuringStreaming => true;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        var mcpService = context.McpService;
        if (mcpService == null)
            return Task.FromResult(CommandResult.Ok("MCP 服务不可用"));

        var subCmd = args?.Trim().ToLowerInvariant() ?? "list";

        if (subCmd == "reload")
        {
            return Task.FromResult(CommandResult.Ok(
                "MCP 热重载暂不支持，请重启应用以重新加载 MCP 服务器配置。" +
                "\n\n配置文件: `~\\.deepseek-code\\mcp-servers.json`"));
        }

        // 默认: 列出当前状态
        var config = McpService.LoadConfig();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## MCP 服务器\n");

        if (config.Servers.Count == 0)
        {
            sb.AppendLine("暂无配置的 MCP 服务器。");
            sb.AppendLine("\n在 `~\\.deepseek-code\\mcp-servers.json` 中添加服务器配置后重启应用。");
            sb.AppendLine("\n示例配置:");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"servers\": [{");
            sb.AppendLine("    \"name\": \"my-server\",");
            sb.AppendLine("    \"command\": \"npx\",");
            sb.AppendLine("    \"args\": [\"-y\", \"@scope/server-name\"],");
            sb.AppendLine("    \"enabled\": true");
            sb.AppendLine("  }]");
            sb.AppendLine("}");
            sb.AppendLine("```");
        }
        else
        {
            foreach (var server in config.Servers)
            {
                var status = server.Enabled ? "✅ 启用" : "⏸ 禁用";
                sb.AppendLine($"- **{server.Name}** [{status}]");
                sb.AppendLine($"  命令: `{server.Command} {string.Join(" ", server.Args)}`");
            }

            sb.AppendLine();
            var registeredTools = mcpService.RegisteredTools;
            if (registeredTools.Count > 0)
            {
                sb.AppendLine($"### 已注册 MCP 工具 ({registeredTools.Count})\n");
                foreach (var tool in registeredTools)
                    sb.AppendLine($"- `{tool.Name}` — {tool.Description}");
            }
            else
            {
                sb.AppendLine("(MCP 服务正在后台启动，工具即将加载...)");
            }
        }

        return Task.FromResult(CommandResult.Ok(sb.ToString()));
    }
}

/// <summary>/plan — 切换 Plan 模式</summary>
public class PlanCommand : ISlashCommand
{
    public string Name => "plan";
    public string Description => "切换 Plan 模式（先规划后执行）。输入 /plan off 退出。";
    public string Usage => "/plan [off]";
    public bool CanRunDuringStreaming => true;

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        var planMode = context.PlanMode;
        if (planMode == null)
            return Task.FromResult(CommandResult.Ok("Plan 模式服务不可用"));

        var subCmd = args?.Trim().ToLowerInvariant() ?? "";

        if (subCmd == "off" || subCmd == "exit" || subCmd == "stop")
        {
            if (!planMode.IsActive)
                return Task.FromResult(CommandResult.Ok("当前未在 Plan 模式。"));

            var planPath = planMode.Exit();
            var msg = "已退出 Plan 模式。";
            if (!string.IsNullOrEmpty(planPath))
                msg += $"\n\n计划文件路径: `{planPath}`";
            return Task.FromResult(CommandResult.Ok(msg));
        }

        if (planMode.IsActive)
            return Task.FromResult(CommandResult.Ok(
                "已在 Plan 模式中。\n\n" +
                $"计划文件: `{planMode.PlanFilePath}`\n\n" +
                "在此模式下 AI 只能读取文件，不能修改代码。\n" +
                "输入 `/plan off` 退出 Plan 模式。"));

        var path = planMode.Enter();
        return Task.FromResult(CommandResult.Ok(
            "已进入 Plan 模式 📋\n\n" +
            $"计划文件: `{path}`\n\n" +
            "AI 现在只能读取文件，不能修改代码（计划文件除外）。\n" +
            "AI 会先探索代码、设计方案，完成后请求你的审批。\n" +
            "输入 `/plan off` 退出 Plan 模式。"));
    }
}
