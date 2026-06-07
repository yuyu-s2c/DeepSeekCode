using System.IO;
using System.Text.Json;
using DeepSeekCode.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DeepSeekCode.Tools;

public class FileReadTool : ITool
{
    public string Name => "read_file";
    public string Description => "读取指定文件的内容";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["filePath"] = new PropertySchema
            {
                Type = "string",
                Description = "要读取的文件路径（绝对路径或相对路径）"
            },
            ["offset"] = new PropertySchema
            {
                Type = "integer",
                Description = "从第几行开始读取（可选，从 1 开始）"
            },
            ["limit"] = new PropertySchema
            {
                Type = "integer",
                Description = "最多读取多少行（可选）"
            }
        },
        Required = ["filePath"]
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

    public Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var filePath = ToolArgHelper.ArgString(arguments, "filePath") ?? "";
        var offset = ToolArgHelper.ArgInt(arguments, "offset");
        var limit = ToolArgHelper.ArgInt(arguments, "limit");

        if (!File.Exists(filePath))
            return Task.FromResult($"错误: 文件不存在 '{filePath}'");

        try
        {
            var lines = File.ReadAllLines(filePath);
            var start = offset > 0 ? offset - 1 : 0;
            var end = limit > 0 ? start + limit : lines.Length;
            start = Math.Max(0, start);
            end = Math.Min(lines.Length, end);

            var result = new System.Text.StringBuilder();
            for (var i = start; i < end; i++)
                result.AppendLine($"{i + 1}: {lines[i]}");

            return Task.FromResult(result.ToString());
        }
        catch (Exception ex)
        {
            return Task.FromResult($"读取文件失败: {ex.Message}");
        }
    }
}

public class FileEditTool : ITool
{
    public string Name => "edit_file";
    public string Description => "精确替换文件中的文本。找到 oldString 并用 newString 替换";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["filePath"] = new PropertySchema
            {
                Type = "string",
                Description = "要编辑的文件路径"
            },
            ["oldString"] = new PropertySchema
            {
                Type = "string",
                Description = "要被替换的原文本"
            },
            ["newString"] = new PropertySchema
            {
                Type = "string",
                Description = "替换后的新文本"
            },
            ["replaceAll"] = new PropertySchema
            {
                Type = "boolean",
                Description = "是否替换所有匹配项（默认 false，只替换第一个）"
            }
        },
        Required = ["filePath", "oldString", "newString"]
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

    public Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var filePath = ToolArgHelper.ArgString(arguments, "filePath") ?? "";
        var oldString = ToolArgHelper.ArgString(arguments, "oldString") ?? "";
        var newString = ToolArgHelper.ArgString(arguments, "newString") ?? "";
        var replaceAll = ToolArgHelper.ArgBool(arguments, "replaceAll");

        if (!File.Exists(filePath))
            return Task.FromResult($"错误: 文件不存在 '{filePath}'");

        try
        {
            var content = File.ReadAllText(filePath);

            var index = content.IndexOf(oldString, StringComparison.Ordinal);
            if (index == -1)
                return Task.FromResult("错误: 在文件中未找到要替换的原文本");

            if (replaceAll)
            {
                var count = 0;
                var temp = content;
                while (temp.Contains(oldString))
                {
                    var i = temp.IndexOf(oldString, StringComparison.Ordinal);
                    temp = temp[..i] + newString + temp[(i + oldString.Length)..];
                    count++;
                }
                File.WriteAllText(filePath, temp);
                return Task.FromResult($"替换成功，共替换了 {count} 处");
            }
            else
            {
                content = content[..index] + newString + content[(index + oldString.Length)..];
                File.WriteAllText(filePath, content);
                return Task.FromResult("替换成功");
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult($"编辑文件失败: {ex.Message}");
        }
    }
}

public class FileWriteTool : ITool
{
    public string Name => "write_file";
    public string Description => "将内容写入文件（覆盖已有文件或创建新文件）";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["filePath"] = new PropertySchema
            {
                Type = "string",
                Description = "要写入的文件路径"
            },
            ["content"] = new PropertySchema
            {
                Type = "string",
                Description = "要写入的文本内容"
            }
        },
        Required = ["filePath", "content"]
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

    public Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var filePath = ToolArgHelper.ArgString(arguments, "filePath") ?? "";
        var content = ToolArgHelper.ArgString(arguments, "content") ?? "";

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(filePath, content);
            return Task.FromResult($"文件已写入: {filePath}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"写入文件失败: {ex.Message}");
        }
    }
}

public class GlobTool : ITool
{
    public string Name => "glob";
    public string Description => "按 glob 模式搜索文件（如 **/*.cs、src/**/*.xaml）";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["pattern"] = new PropertySchema
            {
                Type = "string",
                Description = "glob 匹配模式"
            },
            ["path"] = new PropertySchema
            {
                Type = "string",
                Description = "搜索根目录（可选，默认当前目录）"
            }
        },
        Required = ["pattern"]
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

    public Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var pattern = ToolArgHelper.ArgString(arguments, "pattern") ?? "";
        var basePath = ToolArgHelper.ArgString(arguments, "path") ?? Environment.CurrentDirectory;

        try
        {
            if (!Directory.Exists(basePath))
                return Task.FromResult($"错误: 目录不存在 '{basePath}'");

            var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher();
            matcher.AddInclude(pattern);
            var files = matcher.GetResultsInFullPath(basePath);

            var result = string.Join("\n", files);
            if (string.IsNullOrEmpty(result))
                result = "未找到匹配的文件";

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult($"搜索失败: {ex.Message}");
        }
    }
}

public class GrepTool : ITool
{
    public string Name => "grep";
    public string Description => "在文件内容中搜索正则表达式匹配";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["pattern"] = new PropertySchema
            {
                Type = "string",
                Description = "要搜索的正则表达式"
            },
            ["path"] = new PropertySchema
            {
                Type = "string",
                Description = "搜索目录路径（可选，默认当前目录）"
            },
            ["include"] = new PropertySchema
            {
                Type = "string",
                Description = "文件名过滤模式，如 *.cs（可选）"
            }
        },
        Required = ["pattern"]
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

    public Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var pattern = ToolArgHelper.ArgString(arguments, "pattern") ?? "";
        var basePath = ToolArgHelper.ArgString(arguments, "path") ?? Environment.CurrentDirectory;
        var includeFilter = ToolArgHelper.ArgString(arguments, "include") ?? "*.*";

        try
        {
            if (!Directory.Exists(basePath))
                return Task.FromResult($"错误: 目录不存在 '{basePath}'");

            var results = new System.Text.StringBuilder();
            var regex = new System.Text.RegularExpressions.Regex(pattern);
            var files = System.IO.Directory.GetFiles(basePath, includeFilter,
                SearchOption.AllDirectories)
                .Take(100);

            var foundCount = 0;
            foreach (var file in files)
            {
                try
                {
                    var lines = File.ReadAllLines(file);
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            results.AppendLine($"{file}:{i + 1}: {lines[i].Trim()}");
                            foundCount++;
                            if (foundCount >= 50)
                            {
                                results.AppendLine("...(结果已截断，仅显示前 50 条匹配)");
                                return Task.FromResult(results.ToString());
                            }
                        }
                    }
                }
                catch (IOException) { /* 跳过无法读取的文件 */ }
                catch (UnauthorizedAccessException) { /* 跳过无权限的文件 */ }
            }

            if (foundCount == 0)
                results.AppendLine("未找到匹配内容");

            return Task.FromResult(results.ToString());
        }
        catch (Exception ex)
        {
            return Task.FromResult($"搜索失败: {ex.Message}");
        }
    }
}

/// <summary>
/// 工具参数提取帮助类 — 统一处理 JsonElement 和原始类型
/// </summary>
internal static class ToolArgHelper
{
    public static string? ArgString(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var val) || val == null) return null;
        return val switch
        {
            JsonElement je => je.ValueKind == JsonValueKind.Null ? null : je.GetString(),
            string s => s,
            _ => val.ToString()?.Trim('"')
        };
    }

    public static int ArgInt(Dictionary<string, object?> args, string key, int fallback = 0)
    {
        if (!args.TryGetValue(key, out var val) || val == null) return fallback;
        return val switch
        {
            JsonElement je => je.TryGetInt32(out var i) ? i : fallback,
            int i => i,
            _ => int.TryParse(val.ToString(), out var i) ? i : fallback
        };
    }

    public static bool ArgBool(Dictionary<string, object?> args, string key, bool fallback = false)
    {
        if (!args.TryGetValue(key, out var val) || val == null) return fallback;
        return val switch
        {
            JsonElement je => je.ValueKind == JsonValueKind.True || (je.ValueKind == JsonValueKind.False ? false : fallback),
            bool b => b,
            _ => bool.TryParse(val.ToString(), out var b) ? b : fallback
        };
    }
}
