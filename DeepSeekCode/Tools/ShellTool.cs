using System.Diagnostics;
using System.IO;
using System.Text;
using DeepSeekCode.Models;

namespace DeepSeekCode.Tools;

public class ShellTool : ITool
{
    private static readonly HashSet<string> DangerousCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm", "del", "rmdir", "rd", "format", "shutdown", "restart",
        "taskkill", "reg", "takeown", "icacls"
    };

    private readonly HashSet<string> ReadOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "ls", "dir", "cat", "type", "echo", "git", "dotnet", "npm",
        "node", "python", "pwsh", "where", "which", "find", "findstr",
        "pwd", "cd", "printenv", "set"
    };

    public string Name => "shell";
    public string Description => "在当前终端中执行 Shell 命令";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["command"] = new PropertySchema
            {
                Type = "string",
                Description = "要执行的 Shell 命令"
            },
            ["workdir"] = new PropertySchema
            {
                Type = "string",
                Description = "命令执行的工作目录（可选）"
            }
        },
        Required = ["command"]
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

    public async Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        if (!arguments.TryGetValue("command", out var cmdObj) || cmdObj == null)
            return "错误: 缺少必要参数 'command'";

        var command = cmdObj.ToString()!;
        var workdir = arguments.TryGetValue("workdir", out var wd) && wd != null
            ? wd.ToString() : Environment.CurrentDirectory;

        if (string.IsNullOrWhiteSpace(command))
            return "错误: 命令不能为空";

        var firstWord = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var isDangerous = DangerousCommands.Contains(firstWord);

        var processInfo = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            Arguments = $"-NoProfile -Command \"{command}\"",
            WorkingDirectory = workdir ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null)
                return "错误: 无法启动进程";

            var output = new StringBuilder();
            var error = new StringBuilder();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAny(
                Task.WhenAll(outputTask, errorTask),
                Task.Delay(60000));

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return "错误: 命令执行超时（60 秒）";
            }

            await Task.WhenAll(outputTask, errorTask);

            output.Append(outputTask.Result);
            if (errorTask.Result.Length > 0)
                output.AppendLine($"[stderr] {errorTask.Result}");

            var result = output.ToString();
            if (result.Length > 4000)
                result = result[..4000] + "\n...(输出已截断)";

            return result;
        }
        catch (Exception ex)
        {
            return $"命令执行失败: {ex.Message}";
        }
    }
}
