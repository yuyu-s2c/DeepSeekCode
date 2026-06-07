using System.Diagnostics;
using System.IO;
using System.Text;
using DeepSeekCode.Models;

namespace DeepSeekCode.Tools;

/// <summary>
/// Git 集成工具集：git_diff / git_log / git_commit
/// </summary>
public abstract class GitToolBase : ITool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ParameterSchema Parameters { get; }

    public ToolDefinition ToDefinition() => new()
    {
        Function = new FunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = Parameters
        }
    };

    public abstract Task<string> ExecuteAsync(Dictionary<string, object?> arguments);

    /// <summary>在工作区目录执行 git 命令</summary>
    protected static async Task<string> RunGitAsync(string args, string? workdir = null)
    {
        var wd = workdir ?? Environment.CurrentDirectory;

        var info = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = wd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(info);
            if (process == null)
                return "错误: 无法启动 git，请确认已安装 Git 并加入 PATH";

            var output = new StringBuilder();
            var error = new StringBuilder();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAny(Task.WhenAll(outputTask, errorTask), Task.Delay(30000));

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return "错误: Git 命令执行超时";
            }

            await Task.WhenAll(outputTask, errorTask);

            if (process.ExitCode != 0)
                return $"Git 错误 (exit {process.ExitCode}):\n{errorTask.Result}{outputTask.Result}";

            var result = outputTask.Result + errorTask.Result;
            if (string.IsNullOrWhiteSpace(result)) return "(无输出)";
            if (result.Length > 6000) result = result[..6000] + "\n...(已截断)";
            return result;
        }
        catch (Exception ex)
        {
            return $"Git 命令执行失败: {ex.Message}";
        }
    }
}

/// <summary>git_diff — 查看工作区变更</summary>
public class GitDiffTool : GitToolBase
{
    public override string Name => "git_diff";
    public override string Description => "查看 Git 工作区的文件变更（未暂存 + 已暂存），等同于 git diff HEAD";

    public override ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["path"] = new PropertySchema
            {
                Type = "string",
                Description = "限定到特定文件或目录的路径（可选）"
            },
            ["staged"] = new PropertySchema
            {
                Type = "boolean",
                Description = "仅显示已暂存的变更（--staged），默认显示全部变更"
            }
        },
        Required = []
    };

    public override async Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var path = arguments.TryGetValue("path", out var p) ? p?.ToString()?.Trim() : null;
        var staged = arguments.TryGetValue("staged", out var s) &&
            (s is true || string.Equals(s?.ToString(), "true", StringComparison.OrdinalIgnoreCase));

        var args = staged ? "diff --staged" : "diff HEAD";
        if (!string.IsNullOrWhiteSpace(path))
            args += $" -- {path}";

        return await RunGitAsync(args);
    }
}

/// <summary>git_log — 查看提交历史</summary>
public class GitLogTool : GitToolBase
{
    public override string Name => "git_log";
    public override string Description => "查看 Git 提交历史，支持限制条数和路径过滤";

    public override ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["count"] = new PropertySchema
            {
                Type = "integer",
                Description = "显示的提交条数，默认 10"
            },
            ["path"] = new PropertySchema
            {
                Type = "string",
                Description = "限定到特定文件或目录的路径（可选）"
            },
            ["oneline"] = new PropertySchema
            {
                Type = "boolean",
                Description = "每条提交一行显示，默认 true"
            }
        },
        Required = []
    };

    public override async Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var count = 10;
        if (arguments.TryGetValue("count", out var c) && c != null)
            int.TryParse(c.ToString(), out count);
        var path = arguments.TryGetValue("path", out var p) ? p?.ToString()?.Trim() : null;
        var oneline = !(arguments.TryGetValue("oneline", out var o) &&
            string.Equals(o?.ToString(), "false", StringComparison.OrdinalIgnoreCase));

        var args = oneline ? $"log --oneline -{count}" : $"log -{count}";
        if (!string.IsNullOrWhiteSpace(path))
            args += $" -- {path}";

        return await RunGitAsync(args);
    }
}

/// <summary>git_commit — 执行提交</summary>
public class GitCommitTool : GitToolBase
{
    public override string Name => "git_commit";
    public override string Description => "执行 Git 提交。先 git add 指定文件，再 git commit 提交。请确保 commit message 清晰描述变更内容";

    public override ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["message"] = new PropertySchema
            {
                Type = "string",
                Description = "提交信息（遵循 conventional commits 规范）"
            },
            ["files"] = new PropertySchema
            {
                Type = "string",
                Description = "要提交的文件路径，多个文件用英文逗号分隔。留空则提交所有已追踪的变更（git commit -a）"
            }
        },
        Required = ["message"]
    };

    public override async Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        if (!arguments.TryGetValue("message", out var msgObj) || msgObj == null)
            return "错误: 缺少必要参数 'message'";

        var message = msgObj.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return "错误: 提交信息不能为空";

        var filesStr = arguments.TryGetValue("files", out var f) ? f?.ToString() : null;
        var files = filesStr?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        var sb = new StringBuilder();

        // git add
        if (files != null && files.Count > 0)
        {
            foreach (var file in files)
            {
                // 转义文件名中的双引号，防止命令注入
                var safeFile = file.Replace("\"", "\\\"");
                var addResult = await RunGitAsync($"add \"{safeFile}\"");
                if (addResult.StartsWith("Git 错误") || addResult.StartsWith("错误"))
                    return addResult;
            }
            sb.AppendLine($"已暂存 {files.Count} 个文件");
        }
        else
        {
            // git add -A 暂存所有变更
            var addResult = await RunGitAsync("add -A");
            if (addResult.StartsWith("Git 错误") || addResult.StartsWith("错误"))
                return addResult;
        }

        // git commit
        var commitResult = await RunGitAsync($"commit -m \"{message.Replace("\"", "\\\"")}\"");

        sb.Append(commitResult);
        return sb.ToString();
    }
}
