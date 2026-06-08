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
    Task<List<Models.ChatMessage>> ProcessAsync(
        List<Models.ChatMessage> messages,
        ContextStrategyOptions options);
}

/// <summary>
/// 上下文策略配置
/// </summary>
public class ContextStrategyOptions
{
    /// <summary>最大 token 数</summary>
    public int MaxTokens { get; set; } = 32000;

    /// <summary>保留的最少轮数</summary>
    public int MinRounds { get; set; } = 2;

    /// <summary>是否注入项目指令</summary>
    public bool InjectProjectInstructions { get; set; } = true;

    /// <summary>是否注入技能描述</summary>
    public bool InjectSkills { get; set; } = true;

    /// <summary>压缩模板：旧消息的摘要提示词</summary>
    public string? CompressTemplate { get; set; }

    /// <summary>上下文预估器</summary>
    public Func<string, Task<int>>? TokenEstimator { get; set; }
}

/// <summary>
/// 滑动窗口策略：保留最近 N 轮 + System prompt，超出直接裁剪
/// </summary>
public class SlidingWindowStrategy : IContextStrategy
{
    public string Name => "SlidingWindow";

    public async Task<List<Models.ChatMessage>> ProcessAsync(
        List<Models.ChatMessage> messages,
        ContextStrategyOptions options)
    {
        if (options.TokenEstimator == null || messages.Count <= 2)
            return messages;

        var result = new List<Models.ChatMessage>(messages);

        while (await EstimateTotal(result, options.TokenEstimator) > options.MaxTokens
               && result.Count > options.MinRounds * 2 + 1)
        {
            // 找到第一个 user 消息（跳过 system）
            var roundStart = result.FindIndex(m => m.Role == "user");
            if (roundStart < 0) break;

            // 找到本轮结束位置（下一个 user 消息，或列表末尾）
            var roundEnd = roundStart + 1;
            while (roundEnd < result.Count && result[roundEnd].Role != "user")
                roundEnd++;

            // 删除整轮对话（user + assistant + 可能的 tool 结果）
            result.RemoveRange(roundStart, roundEnd - roundStart);
        }

        return result;
    }

    private static async Task<int> EstimateTotal(
        List<Models.ChatMessage> messages,
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
/// 智能压缩策略：对旧消息做摘要压缩，保留关键信息
/// </summary>
public class SmartCompressStrategy : IContextStrategy
{
    public string Name => "SmartCompress";

    public Task<List<Models.ChatMessage>> ProcessAsync(
        List<Models.ChatMessage> messages,
        ContextStrategyOptions options)
    {
        // TODO: 调用 DeepSeek 对旧消息做摘要
        // 策略：
        // 1. 保留 System prompt + 最后 N 轮
        // 2. 中间轮次发给 DeepSeek 做摘要压缩
        // 3. 将摘要作为 system 消息插入

        return Task.FromResult(messages);
    }
}

/// <summary>
/// 文件注入策略：在 system prompt 中注入项目文件内容
/// </summary>
public class FileInjectionStrategy
{
    /// <summary>
    /// 扫描项目根目录的关键文件并生成注入提示
    /// </summary>
    public async Task<string> GenerateInjectionPromptAsync(string projectRoot)
    {
        // TODO: 扫描以下文件并注入：
        // - .deepseek-code/project.json
        // - INSTRUCTIONS.md / CLAUDE.md
        // - package.json / *.csproj（项目结构）

        return await Task.FromResult(string.Empty);
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
        List<Models.ChatMessage> messages,
        ContextStrategyOptions? overrideOptions = null)
    {
        var options = overrideOptions ?? _defaultOptions;
        var result = messages;

        foreach (var strategy in _strategies)
            result = await strategy.ProcessAsync(result, options);

        return result;
    }
}
