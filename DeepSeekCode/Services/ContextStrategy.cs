using System.IO;
using DeepSeekCode.Models;

namespace DeepSeekCode.Services;

/// <summary>
/// 上下文策略接口。
/// 决定如何管理对话上下文窗口，防止超出 token 限制。
/// </summary>
public interface IContextStrategy
{
    /// <summary>策略名称</summary>
    string Name { get; }

    /// <summary>
    /// 在发送前处理消息列表（裁剪、压缩、注入等）
    /// 返回处理后的消息列表
    /// </summary>
    Task<List<ChatMessage>> ProcessAsync(
        List<ChatMessage> messages,
        ContextStrategyOptions options);
}

/// <summary>
/// 上下文策略配置
/// </summary>
public class ContextStrategyOptions
{
    /// <summary>最大 token 数</summary>
    public int MaxTokens { get; set; } = 32000;

    /// <summary>保留的最少轮数（SmartCompress 保留完整轮数，SlidingWindow 的绝对下限）</summary>
    public int MinRounds { get; set; } = 6;

    /// <summary>是否注入项目指令</summary>
    public bool InjectProjectInstructions { get; set; } = true;

    /// <summary>是否注入技能描述</summary>
    public bool InjectSkills { get; set; } = true;

    /// <summary>压缩模板：旧消息的摘要提示词</summary>
    public string? CompressTemplate { get; set; }

    /// <summary>上下文预估器</summary>
    public Func<string, Task<int>>? TokenEstimator { get; set; }

    /// <summary>摘要回调：用于 SmartCompress 等策略调用 AI 生成摘要</summary>
    public Func<string, Task<string>>? Summarizer { get; set; }

    /// <summary>项目根目录，用于 FileInjection 扫描</summary>
    public string? ProjectRoot { get; set; }
}

/// <summary>
/// 滑动窗口策略：保留最近 N 轮 + System prompt，超出直接裁剪
/// </summary>
public class SlidingWindowStrategy : IContextStrategy
{
    public string Name => "SlidingWindow";

    public async Task<List<ChatMessage>> ProcessAsync(
        List<ChatMessage> messages,
        ContextStrategyOptions options)
    {
        if (options.TokenEstimator == null || messages.Count <= 2)
            return messages;

        var result = new List<ChatMessage>(messages);

        while (await EstimateTotal(result, options.TokenEstimator) > options.MaxTokens
               && result.Count > options.MinRounds * 2 + 1)
        {
            var roundStart = result.FindIndex(m => m.Role == "user");
            if (roundStart < 0) break;

            var roundEnd = roundStart + 1;
            while (roundEnd < result.Count && result[roundEnd].Role != "user")
                roundEnd++;

            result.RemoveRange(roundStart, roundEnd - roundStart);
        }

        return result;
    }

    private static async Task<int> EstimateTotal(
        List<ChatMessage> messages,
        Func<string, Task<int>> estimator)
    {
        var total = 0;
        foreach (var msg in messages)
        {
            if (msg.Content != null)
                total += await estimator(msg.Content);
        }
        return total;
    }
}

/// <summary>
/// 智能压缩策略：超限时对旧消息做摘要压缩，保留关键信息
/// </summary>
public class SmartCompressStrategy : IContextStrategy
{
    public string Name => "SmartCompress";

    public async Task<List<ChatMessage>> ProcessAsync(
        List<ChatMessage> messages,
        ContextStrategyOptions options)
    {
        if (options.TokenEstimator == null || options.Summarizer == null || messages.Count <= 4)
            return messages;

        var totalTokens = await EstimateTotal(messages, options.TokenEstimator);
        if (totalTokens <= options.MaxTokens)
            return messages;

        // 保护 System prompt 和最后 N 轮，压缩中间部分
        var systemMsgs = messages.Where(m => m.Role == "system").ToList();
        var nonSystem = messages.Where(m => m.Role != "system").ToList();

        var keepRounds = Math.Max(options.MinRounds, 1);
        var rounds = SplitIntoRounds(nonSystem);
        if (rounds.Count <= keepRounds)
            return messages;

        var keepFromEnd = Math.Min(keepRounds, rounds.Count);
        var roundsToKeep = rounds.Skip(rounds.Count - keepFromEnd).ToList();
        var roundsToCompress = rounds.Take(rounds.Count - keepFromEnd).ToList();

        // 拼接待压缩内容
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Summarize the following conversation rounds concisely. Capture all critical information: key decisions, files modified, completed steps, and pending tasks. The summary will replace the original messages to save context space.\n");
        foreach (var round in roundsToCompress)
        {
            foreach (var msg in round)
            {
                var label = msg.Role switch
                {
                    "user" => "User",
                    "assistant" => "Assistant",
                    "tool" => $"Tool[{msg.Name ?? "unknown"}]",
                    _ => msg.Role
                };
                var content = msg.Content ?? "";
                if (content.Length > 500)
                    content = content[..500] + "...";
                sb.AppendLine($"[{label}]: {content}");
            }
            sb.AppendLine();
        }

        try
        {
            var summary = await options.Summarizer(sb.ToString());
            if (!string.IsNullOrWhiteSpace(summary))
            {
                // 构建结果：system 消息 + 摘要 + 保留的最近轮次
                var result = new List<ChatMessage>();
                result.AddRange(systemMsgs);
                result.Add(ChatMessage.CreateSystem($"## Conversation Summary (compressed)\n\nThe following summarizes earlier conversation rounds that have been trimmed to save context:\n\n{summary}"));
                foreach (var round in roundsToKeep)
                    result.AddRange(round);
                return result;
            }
        }
        catch
        {
            // 摘要失败，回退到滑动窗口裁剪
        }

        // 摘要失败或为空，回退到简单裁剪
        var fallback = new List<ChatMessage>();
        fallback.AddRange(systemMsgs);
        foreach (var round in roundsToKeep)
            fallback.AddRange(round);
        return fallback;
    }

    private static List<List<ChatMessage>> SplitIntoRounds(List<ChatMessage> messages)
    {
        var rounds = new List<List<ChatMessage>>();
        List<ChatMessage>? current = null;

        foreach (var msg in messages)
        {
            if (msg.Role == "user")
            {
                current = new List<ChatMessage>();
                rounds.Add(current);
            }
            current?.Add(msg);
        }

        return rounds;
    }

    private static async Task<int> EstimateTotal(
        List<ChatMessage> messages,
        Func<string, Task<int>> estimator)
    {
        var total = 0;
        foreach (var msg in messages)
        {
            if (msg.Content != null)
                total += await estimator(msg.Content);
        }
        return total;
    }
}

/// <summary>
/// 文件注入策略：扫描项目关键文件并注入到系统提示词
/// </summary>
public class FileInjectionStrategy : IContextStrategy
{
    public string Name => "FileInjection";

    /// <summary>要扫描的文件名模式（相对于项目根目录）</summary>
    private static readonly string[] KeyFilePatterns =
    [
        "DEEPSEEK.md", "CLAUDE.md", "README.md", "ARCHITECTURE.md",
        ".deepseek-code/project.json"
    ];

    /// <summary>要扫描的文件扩展名（一级根目录文件）</summary>
    private static readonly string[] KeyExtensions = [".csproj", ".sln", "package.json"];

    public async Task<List<ChatMessage>> ProcessAsync(
        List<ChatMessage> messages,
        ContextStrategyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProjectRoot) || !options.InjectProjectInstructions)
            return messages;

        var injection = await GenerateInjectionPromptAsync(options.ProjectRoot);
        if (string.IsNullOrWhiteSpace(injection))
            return messages;

        // 避免重复注入：检查是否已有相同的注入内容
        var alreadyInjected = messages.Any(m =>
            m.Role == "system" && m.Content != null && m.Content.Contains("## 项目文件注入"));
        if (alreadyInjected)
            return messages;

        var result = new List<ChatMessage>(messages);
        result.Add(ChatMessage.CreateSystem(injection));
        return result;
    }

    /// <summary>
    /// 扫描项目根目录的关键文件并生成注入提示
    /// </summary>
    public static async Task<string> GenerateInjectionPromptAsync(string projectRoot)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## 项目文件注入");
            sb.AppendLine();

            // 扫描指定文件名
            foreach (var pattern in KeyFilePatterns)
            {
                var path = Path.Combine(projectRoot, pattern.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    var content = await File.ReadAllTextAsync(path);
                    if (content.Length > 3000)
                        content = content[..3000] + "\n...(内容已截断)";
                    sb.AppendLine($"### {Path.GetFileName(path)}");
                    sb.AppendLine("```");
                    sb.AppendLine(content);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }

            // 扫描根目录关键扩展名文件
            try
            {
                var rootFiles = Directory.GetFiles(projectRoot, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in rootFiles.Take(10))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (KeyExtensions.Contains(ext) && !KeyFilePatterns.Select(Path.GetFileName).Contains(Path.GetFileName(file)))
                    {
                        var content = await File.ReadAllTextAsync(file);
                        if (content.Length > 2000)
                            content = content[..2000] + "\n...(内容已截断)";
                        var fileName = Path.GetFileName(file);
                        sb.AppendLine($"### {fileName}");
                        sb.AppendLine("```");
                        sb.AppendLine(content);
                        sb.AppendLine("```");
                        sb.AppendLine();
                    }
                }
            }
            catch
            {
                // 文件扫描失败，静默跳过
            }

            if (sb.Length > "## 项目文件注入\n\n".Length)
                return sb.ToString();
        }
        catch
        {
            // 注入失败，静默跳过
        }

        return string.Empty;
    }
}

/// <summary>
/// 上下文策略编排器：组合多个策略顺序执行
/// </summary>
public class ContextStrategyOrchestrator
{
    private readonly List<IContextStrategy> _strategies = new();
    private readonly ContextStrategyOptions _defaultOptions;

    public ContextStrategyOrchestrator(ContextStrategyOptions? defaultOptions = null)
    {
        _defaultOptions = defaultOptions ?? new ContextStrategyOptions();
    }

    /// <summary>添加策略</summary>
    public ContextStrategyOrchestrator AddStrategy(IContextStrategy strategy)
    {
        _strategies.Add(strategy);
        return this;
    }

    /// <summary>执行所有策略</summary>
    public async Task<List<Models.ChatMessage>> ProcessAsync(
        List<ChatMessage> messages,
        ContextStrategyOptions? overrideOptions = null)
    {
        var options = overrideOptions ?? _defaultOptions;
        var result = messages;

        foreach (var strategy in _strategies)
            result = await strategy.ProcessAsync(result, options);

        return result;
    }
}
