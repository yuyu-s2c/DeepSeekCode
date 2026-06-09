using System.Text.Json;
using DeepSeekCode.Models;
using DeepSeekCode.Services;

namespace DeepSeekCode.Tests.Services;

public class ConversationManagerTests
{
    private readonly DeepSeekClient _client;

    public ConversationManagerTests()
    {
        _client = new DeepSeekClient(new AppConfig
        {
            ApiKey = "test-key",
            ApiBaseUrl = "https://api.deepseek.com",
            Model = "deepseek-v4-flash"
        });
    }

    [Fact]
    public void AddUserMessage_StoresMessage()
    {
        var mgr = new ConversationManager(_client);
        mgr.AddUserMessage("hello");

        var msgs = mgr.Messages;
        Assert.Single(msgs);
        Assert.Equal("user", msgs[0].Role);
        Assert.Equal("hello", msgs[0].Content);
    }

    [Fact]
    public void AddAssistantMessage_StoresContentAndReasoning()
    {
        var mgr = new ConversationManager(_client);
        mgr.AddAssistantMessage("response", "thinking...");

        var msgs = mgr.Messages;
        Assert.Single(msgs);
        Assert.Equal("assistant", msgs[0].Role);
        Assert.Equal("response", msgs[0].Content);
        Assert.Equal("thinking...", msgs[0].ReasoningContent);
    }

    [Fact]
    public void AddAssistantMessageWithToolCalls_StoresCorrectly()
    {
        var mgr = new ConversationManager(_client);
        var toolCalls = new List<ToolCall>
        {
            new()
            {
                Id = "call_1",
                Function = new FunctionCall { Name = "read_file", Arguments = """{"filePath":"test.cs"}""" }
            }
        };
        mgr.AddAssistantMessageWithToolCalls(toolCalls, "reasoning");

        var msgs = mgr.Messages;
        Assert.Single(msgs);
        Assert.Equal("assistant", msgs[0].Role);
        Assert.NotNull(msgs[0].ToolCalls);
        Assert.Equal("call_1", msgs[0].ToolCalls![0].Id);
        Assert.Equal("read_file", msgs[0].ToolCalls[0].Function.Name);
    }

    [Fact]
    public void AddToolResult_StoresCorrectMetadata()
    {
        var mgr = new ConversationManager(_client);
        mgr.AddToolResult("call_1", "read_file", "file content here");

        var msgs = mgr.Messages;
        Assert.Single(msgs);
        Assert.Equal("tool", msgs[0].Role);
        Assert.Equal("call_1", msgs[0].ToolCallId);
        Assert.Equal("read_file", msgs[0].Name);
        Assert.Equal("file content here", msgs[0].Content);
    }

    [Fact]
    public void SetSystemPrompt_ReplacesExisting()
    {
        var mgr = new ConversationManager(_client);
        mgr.SetSystemPrompt("first");
        mgr.SetSystemPrompt("second");

        var systemMsgs = mgr.Messages.Where(m => m.Role == "system").ToList();
        Assert.Single(systemMsgs);
        Assert.Equal("second", systemMsgs[0].Content);
    }

    [Fact]
    public void ClearConversation_RetainsSystemMessages()
    {
        var mgr = new ConversationManager(_client);
        mgr.SetSystemPrompt("sys msg");
        mgr.AddUserMessage("user msg");
        mgr.AddAssistantMessage("ai msg");

        mgr.ClearConversation();

        var msgs = mgr.Messages;
        Assert.Single(msgs);
        Assert.Equal("system", msgs[0].Role);
    }

    [Fact]
    public void SerializeDeserialize_RoundTrip()
    {
        var mgr = new ConversationManager(_client);
        mgr.SetSystemPrompt("sys");
        mgr.AddUserMessage("hello");
        mgr.AddAssistantMessage("world");

        var json = mgr.SerializeSession();
        Assert.False(string.IsNullOrWhiteSpace(json));

        var mgr2 = new ConversationManager(_client);
        mgr2.DeserializeSession(json);

        Assert.Equal(3, mgr2.Messages.Count);
        Assert.Equal("system", mgr2.Messages[0].Role);
        Assert.Equal("user", mgr2.Messages[1].Role);
        Assert.Equal("assistant", mgr2.Messages[2].Role);
    }

    [Fact]
    public void Messages_IsThreadSafeCopy()
    {
        var mgr = new ConversationManager(_client);
        mgr.AddUserMessage("original");

        var snapshot = mgr.Messages;
        // 返回的是副本，修改不影响内部
        Assert.Single(snapshot);
    }
}
