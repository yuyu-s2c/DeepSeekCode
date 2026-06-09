using System.Text.Json;
using DeepSeekCode.Models;

namespace DeepSeekCode.Services;

public class ConversationManager
{
    private readonly List<ChatMessage> _messages = new();
    private readonly object _msgLock = new();
    private readonly int _maxContextTokens;
    private readonly DeepSeekClient _client;
    private ContextStrategyOrchestrator? _contextOrchestrator;
    private string? _extraSystemContext;

    public IReadOnlyList<ChatMessage> Messages
    {
        get { lock (_msgLock) return _messages.ToList(); }
    }

    public ConversationManager(DeepSeekClient client, int maxContextTokens = 900_000)
    {
        _client = client;
        _maxContextTokens = maxContextTokens;
    }

    /// <summary>注入上下文策略编排器（可选，用于高级上下文管理）</summary>
    public void SetContextStrategy(ContextStrategyOrchestrator orchestrator)
    {
        _contextOrchestrator = orchestrator;
    }

    /// <summary>设置额外的 system context（Plan 模式提示词等），传 null 清除</summary>
    public void SetExtraSystemContext(string? context)
    {
        _extraSystemContext = context;
    }

    public void SetSystemPrompt(string prompt)
    {
        lock (_msgLock)
        {
            _messages.RemoveAll(m => m.Role == "system");
            if (!string.IsNullOrWhiteSpace(prompt))
                _messages.Insert(0, ChatMessage.CreateSystem(prompt));
        }
    }

    /// <summary>追加额外的系统级上下文（DEEPSEEK.md、技能等），不覆盖已有的 system 消息</summary>
    public void AppendSystemContext(string content)
    {
        lock (_msgLock)
        {
            if (!string.IsNullOrWhiteSpace(content))
                _messages.Add(ChatMessage.CreateSystem(content));
        }
    }

    public void AddUserMessage(string content)
    {
        lock (_msgLock)
        {
            _messages.Add(ChatMessage.CreateUser(content));
        }
    }

    public void AddAssistantMessage(string content, string? reasoningContent = null)
    {
        lock (_msgLock)
        {
            var msg = ChatMessage.CreateAssistant(content);
            msg.ReasoningContent = reasoningContent;
            _messages.Add(msg);
        }
    }

    public void AddAssistantMessageWithToolCalls(List<ToolCall> toolCalls, string? reasoningContent = null)
    {
        lock (_msgLock)
        {
            var msg = ChatMessage.CreateAssistantWithToolCalls(toolCalls);
            msg.ReasoningContent = reasoningContent;
            _messages.Add(msg);
        }
    }

    public void AddToolResult(string toolCallId, string toolName, string result)
    {
        lock (_msgLock)
        {
            _messages.Add(ChatMessage.CreateToolResult(toolCallId, toolName, result));
        }
    }

    public async Task<List<ChatMessage>> GetProcessedMessagesAsync()
    {
        List<ChatMessage> snapshot;
        lock (_msgLock) snapshot = new List<ChatMessage>(_messages);

        if (_contextOrchestrator != null)
            snapshot = await _contextOrchestrator.ProcessAsync(snapshot);
        else
        {
            var tokenCount = await _client.EstimateTokenCount(snapshot);
            if (tokenCount > _maxContextTokens)
            {
                await TrimContextIfNeeded();
                lock (_msgLock) snapshot = new List<ChatMessage>(_messages);
            }
        }

        // 注入额外 system context（Plan 模式提示词等）
        // 缓存友好：插入到最后一个初始 system 消息之后（固定位置），而非追加到末尾
        // 这样 Plan 模式提示词始终在对话历史之前，保持缓存前缀稳定
        if (!string.IsNullOrWhiteSpace(_extraSystemContext))
        {
            var lastSystemIdx = snapshot.FindLastIndex(m => m.Role == "system");
            if (lastSystemIdx >= 0)
                snapshot.Insert(lastSystemIdx + 1, ChatMessage.CreateSystem(_extraSystemContext));
            else
                snapshot.Insert(0, ChatMessage.CreateSystem(_extraSystemContext));
        }

        return snapshot;
    }

    private async Task TrimContextIfNeeded()
    {
        List<ChatMessage> snapshot;
        lock (_msgLock) snapshot = new List<ChatMessage>(_messages);

        var tokenCount = EstimateQuick(snapshot);
        if (tokenCount <= _maxContextTokens)
            return;

        lock (_msgLock)
        {
            // 保存所有 system 消息（缓存锚点，不可删除）
            var systemMsgs = _messages.Where(m => m.Role == "system").ToList();
            _messages.RemoveAll(m => m.Role == "system");

            // 按完整轮次分组并预计算每轮 token（避免 O(n²) 重复计算）
            var rounds = SplitIntoRounds(_messages);
            var roundTokens = new int[rounds.Count];
            for (var i = 0; i < rounds.Count; i++)
                roundTokens[i] = rounds[i].Sum(m => DeepSeekClient.EstimateTokenCountSync(m.Content ?? ""));
            var currentTotal = roundTokens.Sum();
            var minRounds = 2;

            while (rounds.Count > minRounds && currentTotal > _maxContextTokens)
            {
                // 删除最早一轮，O(1) 减法而非 O(n) 重算
                currentTotal -= roundTokens[0];
                var oldestRound = rounds[0];
                foreach (var msg in oldestRound)
                    _messages.Remove(msg);
                rounds.RemoveAt(0);
                // 移除对应的 token 计数
                var newRoundTokens = new int[roundTokens.Length - 1];
                Array.Copy(roundTokens, 1, newRoundTokens, 0, newRoundTokens.Length);
                roundTokens = newRoundTokens;
            }

            // 恢复所有 system 消息
            _messages.InsertRange(0, systemMsgs);
        }
    }

    /// <summary>快速估算 token（同步，无需 API 调用）</summary>
    private static int EstimateQuick(List<ChatMessage> messages)
    {
        return DeepSeekClient.EstimateTokenCountSync(messages);
    }

    public void ClearConversation()
    {
        lock (_msgLock)
        {
            var systemMsgs = _messages.Where(m => m.Role == "system").ToList();
            _messages.Clear();
            _messages.AddRange(systemMsgs);
        }
    }

    public string SerializeSession()
    {
        lock (_msgLock) return JsonSerializer.Serialize(_messages);
    }

    public void DeserializeSession(string json)
    {
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(json);
        lock (_msgLock)
        {
            _messages.Clear();
            if (messages != null)
                _messages.AddRange(messages);
        }
    }

    /// <summary>
    /// 压缩对话上下文：对旧消息做摘要并替换为用户消息（缓存友好：不插入 system 消息）
    /// 保留 system 消息 + 最后 keepRounds 轮，其余压缩为摘要
    /// </summary>
    public async Task CompactContextAsync(int keepRounds = 3)
    {
        List<ChatMessage> snapshot;
        lock (_msgLock) snapshot = new List<ChatMessage>(_messages);

        var systemMsgs = snapshot.Where(m => m.Role == "system").ToList();
        var nonSystem = snapshot.Where(m => m.Role != "system").ToList();

        if (nonSystem.Count <= keepRounds * 2)
            return;

        var rounds = SplitIntoRounds(nonSystem);
        if (rounds.Count <= keepRounds)
            return;

        var roundsToKeep = rounds.Skip(rounds.Count - keepRounds).ToList();
        var roundsToCompress = rounds.Take(rounds.Count - keepRounds).ToList();

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("请对以下对话内容生成一段简洁的摘要，保留所有重要信息（关键决策、修改的文件、完成的步骤、待处理事项）：\n");
            foreach (var round in roundsToCompress)
            {
                foreach (var msg in round)
                {
                    var label = msg.Role switch
                    {
                        "user" => "用户",
                        "assistant" => "助手",
                        "tool" => $"工具[{msg.Name}]",
                        _ => msg.Role
                    };
                    var content = msg.Content ?? "";
                    if (content.Length > 500)
                        content = content[..500] + "...";
                    sb.AppendLine($"[{label}]: {content}");
                }
                sb.AppendLine();
            }

            var summary = await SummarizeAsync(sb.ToString());
            if (string.IsNullOrWhiteSpace(summary))
                return;

            lock (_msgLock)
            {
                var currentSystem = _messages.Where(m => m.Role == "system").ToList();
                _messages.Clear();
                _messages.AddRange(currentSystem);
                // 缓存友好：摘要作为 user 消息注入，而非 system 消息
                _messages.Add(ChatMessage.CreateUser(
                    $"<conversation_history_summary>\n" +
                    $"以下是对之前 {roundsToCompress.Count} 轮对话的摘要：\n\n" +
                    $"{summary}\n" +
                    $"</conversation_history_summary>"));
                foreach (var round in roundsToKeep)
                    _messages.AddRange(round);
            }
        }
        catch
        {
            // 压缩失败，保持原样
        }
    }

    /// <summary>调用 DeepSeek API 总结文本</summary>
    private async Task<string> SummarizeAsync(string text)
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystem("你是一个对话摘要助手。请对输入的对话内容生成简洁摘要，提取所有关键信息。"),
            ChatMessage.CreateUser(text)
        };

        var config = new Models.AppConfig
        {
            Model = "deepseek-v4-flash",
            MaxTokens = 1024,
            ThinkingEnabled = false
        };

        var fullContent = "";
        await foreach (var chunk in _client.StreamChatAsync(new(), messages, config))
        {
            if (chunk.Choices?.Count > 0)
            {
                var delta = chunk.Choices[0].Delta;
                if (delta?.Content != null)
                    fullContent += delta.Content;
            }
        }

        return fullContent.Trim();
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
}
