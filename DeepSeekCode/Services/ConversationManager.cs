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

    public IReadOnlyList<ChatMessage> Messages
    {
        get { lock (_msgLock) return _messages.ToList(); }
    }

    public ConversationManager(DeepSeekClient client, int maxContextTokens = 32000)
    {
        _client = client;
        _maxContextTokens = maxContextTokens;
    }

    /// <summary>注入上下文策略编排器（可选，用于高级上下文管理）</summary>
    public void SetContextStrategy(ContextStrategyOrchestrator orchestrator)
    {
        _contextOrchestrator = orchestrator;
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
            return await _contextOrchestrator.ProcessAsync(snapshot);

        var tokenCount = await _client.EstimateTokenCount(snapshot);
        if (tokenCount > _maxContextTokens)
        {
            await TrimContextIfNeeded();
            lock (_msgLock) return new List<ChatMessage>(_messages);
        }

        return snapshot;
    }

    private async Task TrimContextIfNeeded()
    {
        lock (_msgLock)
        {
            // 保存所有 system 消息
            var systemMsgs = _messages.Where(m => m.Role == "system").ToList();
            _messages.RemoveAll(m => m.Role == "system");

            var tokenCount = EstimateQuick(_messages);
            // 保留至少 2 条非 system 消息（首条用户消息 + 最后回复），不够就不裁
            while (_messages.Count > 2 && tokenCount > _maxContextTokens)
            {
                // 从第 2 条开始删（保留第 1 条，即用户原始消息）
                if (_messages.Count > 2)
                    _messages.RemoveAt(1);
                else
                    break;

                tokenCount = EstimateQuick(_messages);
            }

            // 恢复所有 system 消息在前面
            _messages.InsertRange(0, systemMsgs);
        }
    }

    /// <summary>快速估算 token（同步，无需 API 调用）</summary>
    private static int EstimateQuick(List<ChatMessage> messages)
    {
        var total = 0;
        foreach (var msg in messages)
        {
            if (msg.Content != null)
                total += (int)Math.Ceiling(msg.Content.Length / 2.5);
            if (msg.ToolCalls != null)
            {
                foreach (var tc in msg.ToolCalls)
                    total += (int)Math.Ceiling(tc.Function.Arguments.Length / 2.5) + 10;
            }
        }
        return total;
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
}
