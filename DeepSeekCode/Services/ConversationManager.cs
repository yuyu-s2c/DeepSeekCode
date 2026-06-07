using System.Text.Json;
using DeepSeekCode.Models;

namespace DeepSeekCode.Services;

public class ConversationManager
{
    private readonly List<ChatMessage> _messages = new();
    private readonly int _maxContextTokens;
    private readonly DeepSeekClient _client;
    private ContextStrategyOrchestrator? _contextOrchestrator;

    public IReadOnlyList<ChatMessage> Messages => _messages;

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
        _messages.RemoveAll(m => m.Role == "system");
        if (!string.IsNullOrWhiteSpace(prompt))
            _messages.Insert(0, ChatMessage.CreateSystem(prompt));
    }

    /// <summary>追加额外的系统级上下文（DEEPSEEK.md、技能等），不覆盖已有的 system 消息</summary>
    public void AppendSystemContext(string content)
    {
        if (!string.IsNullOrWhiteSpace(content))
            _messages.Add(ChatMessage.CreateSystem(content));
    }

    public void AddUserMessage(string content)
    {
        _messages.Add(ChatMessage.CreateUser(content));
    }

    public void AddAssistantMessage(string content, string? reasoningContent = null)
    {
        var msg = ChatMessage.CreateAssistant(content);
        msg.ReasoningContent = reasoningContent;
        _messages.Add(msg);
    }

    public void AddAssistantMessageWithToolCalls(List<ToolCall> toolCalls, string? reasoningContent = null)
    {
        var msg = ChatMessage.CreateAssistantWithToolCalls(toolCalls);
        msg.ReasoningContent = reasoningContent;
        _messages.Add(msg);
    }

    public void AddToolResult(string toolCallId, string toolName, string result)
    {
        _messages.Add(ChatMessage.CreateToolResult(toolCallId, toolName, result));
    }

    public async Task<List<ChatMessage>> GetProcessedMessagesAsync()
    {
        if (_contextOrchestrator != null)
            return await _contextOrchestrator.ProcessAsync(new List<ChatMessage>(_messages));

        var tokenCount = await _client.EstimateTokenCount(_messages);
        if (tokenCount > _maxContextTokens)
        {
            await TrimContextIfNeeded();
            return new List<ChatMessage>(_messages);
        }

        return _messages; // 无需裁剪，直接返回引用（调用方序列化，不修改）
    }

    private async Task TrimContextIfNeeded()
    {
        // 保存所有 system 消息
        var systemMsgs = _messages.Where(m => m.Role == "system").ToList();
        _messages.RemoveAll(m => m.Role == "system");

        var tokenCount = await _client.EstimateTokenCount(_messages);
        // 保留至少 2 条非 system 消息（首条用户消息 + 最后回复），不够就不裁
        while (_messages.Count > 2 && tokenCount > _maxContextTokens)
        {
            // 从第 2 条开始删（保留第 1 条，即用户原始消息）
            if (_messages.Count > 2)
                _messages.RemoveAt(1);
            else
                break;

            tokenCount = await _client.EstimateTokenCount(_messages);
        }

        // 恢复所有 system 消息在前面
        _messages.InsertRange(0, systemMsgs);
    }

    public void ClearConversation()
    {
        var systemMsgs = _messages.Where(m => m.Role == "system").ToList();
        _messages.Clear();
        _messages.AddRange(systemMsgs);
    }

    public string SerializeSession()
    {
        return JsonSerializer.Serialize(_messages);
    }

    public void DeserializeSession(string json)
    {
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(json);
        _messages.Clear();
        if (messages != null)
            _messages.AddRange(messages);
    }
}
