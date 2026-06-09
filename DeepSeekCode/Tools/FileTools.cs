using System.IO;
using System.Text.Json;
using DeepSeekCode.Diff;
using DeepSeekCode.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DeepSeekCode.Tools;

public class FileReadTool : ITool
{
    public string Name => "read_file";
    public string Description => "Reads a file from the local filesystem.\n- filePath must be an absolute path.\n- You can specify an optional offset and limit (especially handy for long files), but it's recommended to read the whole file when possible.\n- Results are returned in cat -n format, with line numbers starting at 1.\n- You MUST read a file before editing it — Edit will fail otherwise.\n- Do NOT re-read a file you just edited to verify — Edit/Write would have errored if the change failed.\n- Reading a directory or a missing file returns an error rather than content.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["filePath"] = new PropertySchema
            {
                Type = "string",
                Description = "The absolute path to the file to read"
            },
            ["offset"] = new PropertySchema
            {
                Type = "integer",
                Description = "Line number to start reading from (1-indexed, optional)"
            },
            ["limit"] = new PropertySchema
            {
                Type = "integer",
                Description = "Maximum number of lines to read (optional)"
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
    public string Description => "Performs exact string replacement in a file.\n- You MUST Read the file in this conversation before editing, or the call will fail.\n- oldString must match the file content exactly, including all whitespace and indentation, and be unique in the file — the edit fails if the match is ambiguous. If you get a multiple-match error, provide more surrounding lines in oldString to make it unique.\n- newString must be different from oldString.\n- Set replaceAll to true to replace every occurrence instead of just the first one.\n- Prefer this over write_file for targeted changes. Only use write_file when creating a new file or doing a full rewrite.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["filePath"] = new PropertySchema
            {
                Type = "string",
                Description = "The absolute path to the file to modify"
            },
            ["oldString"] = new PropertySchema
            {
                Type = "string",
                Description = "The text to replace"
            },
            ["newString"] = new PropertySchema
            {
                Type = "string",
                Description = "The text to replace it with (must be different from oldString)"
            },
            ["replaceAll"] = new PropertySchema
            {
                Type = "boolean",
                Description = "Replace all occurrences of oldString (default false, replaces only the first)"
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

        if (string.IsNullOrWhiteSpace(oldString))
            return Task.FromResult("错误: oldString 不能为空");

        if (!File.Exists(filePath))
            return Task.FromResult($"错误: 文件不存在 '{filePath}'");

        try
        {
            var oldContent = File.ReadAllText(filePath);

            var index = oldContent.IndexOf(oldString, StringComparison.Ordinal);
            if (index == -1)
                return Task.FromResult("错误: 在文件中未找到要替换的原文本");

            if (index != oldContent.LastIndexOf(oldString, StringComparison.Ordinal) && !replaceAll)
                return Task.FromResult("错误: oldString 在文件中出现多次，匹配不唯一。请提供更多上下文使匹配唯一，或设置 replaceAll=true");

            string newContent;
            if (replaceAll)
            {
                // 防死循环：oldString 是 newString 子串时直接使用 string.Replace（一次完成）
                if (newString.Contains(oldString, StringComparison.Ordinal))
                {
                    newContent = oldContent.Replace(oldString, newString, StringComparison.Ordinal);
                }
                else
                {
                    var count = 0;
                    var temp = oldContent;
                    while (temp.Contains(oldString))
                    {
                        var i = temp.IndexOf(oldString, StringComparison.Ordinal);
                        temp = temp[..i] + newString + temp[(i + oldString.Length)..];
                        count++;
                        // 安全上限：防止逻辑意外导致死循环
                        if (count > 10000)
                            return Task.FromResult("错误: 替换次数超过安全上限（10000 次），操作取消");
                    }
                    newContent = temp;
                }
                var replacementCount = ToolArgHelper.CountReplacements(oldContent, newContent, oldString);
                File.WriteAllText(filePath, newContent);

                var diff = DiffRenderer.FormatTextDiff(oldContent, newContent, Path.GetFileName(filePath));
                return Task.FromResult(string.IsNullOrEmpty(diff)
                    ? $"替换成功，共替换了 {replacementCount} 处"
                    : $"替换成功，共替换了 {replacementCount} 处\n\n{diff}");
            }
            else
            {
                newContent = oldContent[..index] + newString + oldContent[(index + oldString.Length)..];
                File.WriteAllText(filePath, newContent);

                var diff = DiffRenderer.FormatTextDiff(oldContent, newContent, Path.GetFileName(filePath));
                return Task.FromResult(string.IsNullOrEmpty(diff)
                    ? "替换成功"
                    : $"替换成功\n\n{diff}");
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
    public string Description => "Writes a file to the local filesystem, overwriting if one exists.\n- filePath must be an absolute path.\n- Parent directories are created automatically if they don't exist.\n- Prefer edit_file for targeted changes. Use this tool only when creating a new file or doing a full-content rewrite of a file you've already read.\n- This tool will overwrite the existing file if there is one at the provided path without prompting — double-check filePath before calling.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["filePath"] = new PropertySchema
            {
                Type = "string",
                Description = "The absolute path to the file to write"
            },
            ["content"] = new PropertySchema
            {
                Type = "string",
                Description = "The content to write to the file"
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

            var existed = File.Exists(filePath);
            var oldContent = existed ? File.ReadAllText(filePath) : null;
            File.WriteAllText(filePath, content);

            var fileName = Path.GetFileName(filePath);
            if (existed && oldContent != null)
            {
                var diff = DiffRenderer.FormatTextDiff(oldContent, content, fileName);
                return Task.FromResult(string.IsNullOrEmpty(diff)
                    ? $"文件已覆盖: {filePath}"
                    : $"文件已覆盖: {filePath}\n\n{diff}");
            }

            return Task.FromResult($"文件已创建: {filePath}");
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
    public string Description => "Fast file pattern matching tool that works with any codebase size.\n- Supports glob patterns like \"**/*.cs\" or \"src/**/*.tsx\".\n- Returns matching file paths sorted by modification time.\n- Use this to find files by name patterns — do NOT use shell ls/dir for file search.\n- The path parameter is optional; omit it to search from the current working directory.\n- When doing an open-ended search that may require multiple rounds of globbing and grepping, combine with grep rather than running shell find.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["pattern"] = new PropertySchema
            {
                Type = "string",
                Description = "The glob pattern to match files against (e.g. \"**/*.cs\", \"src/**/*.tsx\")"
            },
            ["path"] = new PropertySchema
            {
                Type = "string",
                Description = "The directory to search in. Defaults to the current working directory if omitted."
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
    public string Description => "Fast content search tool that works with any codebase size.\n- Searches file contents using full regex syntax (e.g. \"log.*Error\", \"function\\s+\\w+\").\n- Returns file paths and line numbers with at least one match, sorted by modification time.\n- Filter files by pattern with the include parameter (e.g. \"*.cs\", \"*.{ts,tsx}\").\n- Prefer this over shell grep/Select-String for code searches.\n- Limit: scans up to 100 files and returns up to 50 matching results. Skipped files (permission errors, binary) are silently ignored.\n- When doing a broad search across many files, use grep first to locate candidate files, then read_file to examine the specific sections.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["pattern"] = new PropertySchema
            {
                Type = "string",
                Description = "The regular expression pattern to search for"
            },
            ["path"] = new PropertySchema
            {
                Type = "string",
                Description = "The directory to search in. Defaults to the current working directory if omitted."
            },
            ["include"] = new PropertySchema
            {
                Type = "string",
                Description = "File name pattern to filter by (e.g. \"*.cs\", \"*.{ts,tsx}\"), optional"
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
            var regex = new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(5));
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
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { /* 正则超时，跳过 */ }
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
    /// <summary>
    /// 验证路径是否在当前工作区内（防止路径遍历攻击）
    /// </summary>
    public static bool ValidatePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(rawPath);
            var workspace = Path.GetFullPath(Environment.CurrentDirectory);

            return fullPath.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, workspace, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // 路径格式无效
        }
    }

    /// <summary>
    /// 计算替换次数（用于 edit_file 显示）
    /// </summary>
    public static int CountReplacements(string oldContent, string newContent, string oldString)
    {
        if (string.IsNullOrEmpty(oldString))
            return 0;

        // 如果源和目标长度相同，比较字符级差异
        // 否则通过 oldString 在原内容中的出现次数估算
        var count = 0;
        var idx = 0;
        while ((idx = oldContent.IndexOf(oldString, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += oldString.Length;
        }
        return count;
    }
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
