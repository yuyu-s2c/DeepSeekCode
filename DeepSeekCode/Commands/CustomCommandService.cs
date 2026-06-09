using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using DeepSeekCode.Models;

namespace DeepSeekCode.Commands;

/// <summary>自定义命令服务：加载、注册、管理用户自定义 Slash 命令</summary>
public class CustomCommandService
{
    private readonly SlashCommandRegistry _registry;
    private readonly List<ISlashCommand> _loadedCommands = new();

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepseek-code", "custom-commands.json");

    public CustomCommandService(SlashCommandRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>加载自定义命令配置并注册到命令系统</summary>
    public void LoadAndRegister()
    {
        var config = LoadConfig();

        foreach (var cmd in config.Commands)
        {
            if (string.IsNullOrWhiteSpace(cmd.Name)) continue;
            if (string.IsNullOrWhiteSpace(cmd.Prompt)) continue;

            // 检查是否与内置命令冲突
            if (_registry.Find(cmd.Name) != null)
            {
                System.Diagnostics.Debug.WriteLine($"[CustomCommand] 跳过冲突命令: /{cmd.Name}");
                continue;
            }

            var executor = new CustomCommandExecutor(cmd);
            _registry.Register(executor);
            _loadedCommands.Add(executor);
        }
    }

    /// <summary>重新加载（先卸载旧命令再加载新配置）</summary>
    public void Reload()
    {
        foreach (var cmd in _loadedCommands)
            _registry.Unregister(cmd.Name);
        _loadedCommands.Clear();
        LoadAndRegister();
    }

    public static CustomCommandConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<CustomCommandConfig>(json) ?? new CustomCommandConfig();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomCommand] 配置加载失败: {ex.Message}");
        }
        return new CustomCommandConfig();
    }

    public static void SaveConfig(CustomCommandConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public IReadOnlyList<ISlashCommand> GetLoadedCommands() => _loadedCommands.AsReadOnly();
}

/// <summary>单个自定义命令的 ISlashCommand 执行器</summary>
internal class CustomCommandExecutor : ISlashCommand
{
    private readonly CustomCommand _command;

    public string Name => _command.Name;
    public string Description => _command.Description;
    public string Usage => $"/{_command.Name} <参数>";
    public bool CanRunDuringStreaming => false;

    public CustomCommandExecutor(CustomCommand command)
    {
        _command = command;
    }

    public Task<CommandResult> ExecuteAsync(string args, CommandContext context)
    {
        var prompt = _command.Prompt.Replace("{args}", args ?? "");
        prompt = prompt.Replace("$ARGUMENTS", args ?? "")
                       .Replace("$arguments", args ?? "");

        return Task.FromResult(new CommandResult
        {
            Handled = true,
            UserPrompt = prompt
        });
    }
}
