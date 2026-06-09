using System;
using System.IO;

namespace DeepSeekCode.Services;

/// <summary>
/// 跨会话 Memory 系统 — 读/写 ~/.deepseek-code/memory.md
/// </summary>
public class MemoryService
{
    private static readonly string MemoryDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepseek-code");

    private static readonly string MemoryFile = Path.Combine(MemoryDir, "memory.md");

    /// <summary>读取 Memory 内容，如果文件不存在返回 null</summary>
    public string? LoadMemory()
    {
        if (!File.Exists(MemoryFile)) return null;
        try { return File.ReadAllText(MemoryFile).Trim(); }
        catch { return null; }
    }

    /// <summary>生成注入到 system 消息的 Memory 提示词</summary>
    public string? BuildMemoryContext()
    {
        var content = LoadMemory();
        if (string.IsNullOrWhiteSpace(content)) return null;

        return $"""
# Memory
The following is your persistent, cross-session memory file. It records user preferences, conventions, decisions, and project context that carry across conversations. You should reference it when relevant, and update it (via edit_file or write_file) when the user shares new preferences, conventions, or important context.

Memory file location: `{MemoryFile}`

Contents:
{content}
""";
    }

    /// <summary>确保 Memory 目录和初始文件存在</summary>
    public static void EnsureExists()
    {
        Directory.CreateDirectory(MemoryDir);
        if (!File.Exists(MemoryFile))
        {
            File.WriteAllText(MemoryFile, "# Memory\n\n" +
                "本文件记录跨会话的用户偏好、约定和项目上下文。AI 可读取和更新。\n\n" +
                "## 偏好\n\n## 规范\n\n## 项目上下文\n");
        }
    }
}
