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
    public string Description => "Executes a PowerShell 7+ command and returns its output.\n- The command runs in pwsh.exe with -NoProfile for a clean environment.\n- Working directory can be specified via the workdir parameter; otherwise uses the current workspace.\n- timeout is 60 seconds; commands exceeding this are killed with entire process tree.\n- Output is truncated at 4000 characters if longer.\n- IMPORTANT: Prefer the dedicated file/search tools over shell commands when one fits. Avoid using this to run ls, dir, cat, grep, find, or echo — use glob, grep, and read_file instead.\n- Wrap file paths with spaces in double quotes.\n- Use && for sequential dependent commands. Use ; for sequential independent commands.\n- Dangerous commands (rm, del, format, shutdown, etc.) are blocked by the permission system regardless of user settings.\n- Interactive commands (those requiring user input) will hang until timeout — do not use them.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["command"] = new PropertySchema
            {
                Type = "string",
                Description = "The command to execute"
            },
            ["workdir"] = new PropertySchema
            {
                Type = "string",
                Description = "The working directory to run the command in (optional)"
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
