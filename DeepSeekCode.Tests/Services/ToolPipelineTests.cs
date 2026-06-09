using DeepSeekCode.Models;
using DeepSeekCode.Services;
using DeepSeekCode.Tools;

namespace DeepSeekCode.Tests.Services;

public class ToolPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_RunsTool_AndReturnsResult()
    {
        var tool = new EchoTool();
        var pipeline = new ToolPipeline(tool);
        var context = new ToolCallContext
        {
            ToolName = "echo",
            Arguments = new Dictionary<string, object?> { ["text"] = "hello" }
        };

        var result = await pipeline.ExecuteAsync(context);

        Assert.Equal("ECHO: hello", result);
        Assert.False(context.Cancelled);
        Assert.Null(context.Error);
    }

    [Fact]
    public async Task ExecuteAsync_PermissionFilter_CanBlock()
    {
        var tool = new EchoTool();
        var permManager = new PermissionManager();
        // 注册 echo 工具为默认 Deny
        permManager.AddRule(new PermissionRule
        {
            ToolName = "echo",
            Level = PermissionLevel.Deny
        });

        var pipeline = new ToolPipeline(tool);
        pipeline.AddFilter(new PermissionPipelineFilter(permManager));

        var context = new ToolCallContext
        {
            ToolName = "echo",
            Arguments = new Dictionary<string, object?> { ["text"] = "blocked" }
        };

        var result = await pipeline.ExecuteAsync(context);

        Assert.True(context.Cancelled);
        Assert.Contains("拦截", result);
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutFilter_EnforcesTimeout()
    {
        var tool = new SlowTool(delayMs: 5000);
        var pipeline = new ToolPipeline(tool);
        pipeline.AddFilter(new TimeoutFilter(100)); // 100ms timeout

        var context = new ToolCallContext
        {
            ToolName = "slow",
            Arguments = new Dictionary<string, object?>()
        };

        var result = await pipeline.ExecuteAsync(context);

        Assert.True(context.Cancelled);
        Assert.Contains("超时", result);
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutFilter_FastTool_Completes()
    {
        var tool = new SlowTool(delayMs: 10);
        var pipeline = new ToolPipeline(tool);
        pipeline.AddFilter(new TimeoutFilter(5000));

        var context = new ToolCallContext
        {
            ToolName = "fast",
            Arguments = new Dictionary<string, object?>()
        };

        var result = await pipeline.ExecuteAsync(context);

        Assert.False(context.Cancelled);
        Assert.Equal("DONE", result);
    }

    [Fact]
    public async Task ExecuteAsync_LoggingFilter_DoesNotBlock()
    {
        var logged = new List<string>();
        var tool = new EchoTool();
        var pipeline = new ToolPipeline(tool);
        pipeline.AddFilter(new LoggingFilter(msg => logged.Add(msg)));

        var context = new ToolCallContext
        {
            ToolName = "echo",
            Arguments = new Dictionary<string, object?> { ["text"] = "x" }
        };

        var result = await pipeline.ExecuteAsync(context);

        Assert.Equal("ECHO: x", result);
        Assert.Equal(2, logged.Count); // before + after
    }

    // ── Test tools ──

    private class EchoTool : ITool
    {
        public string Name => "echo";
        public string Description => "Echo back";
        public ParameterSchema Parameters => new();
        public ToolDefinition ToDefinition() => new()
        {
            Function = new FunctionDefinition { Name = Name, Description = Description, Parameters = Parameters }
        };

        public Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
        {
            var text = arguments.TryGetValue("text", out var t) ? t?.ToString() : "";
            return Task.FromResult($"ECHO: {text}");
        }
    }

    private class SlowTool : ITool
    {
        private readonly int _delayMs;
        public SlowTool(int delayMs) => _delayMs = delayMs;
        public string Name => "slow";
        public string Description => "";
        public ParameterSchema Parameters => new();
        public ToolDefinition ToDefinition() => new()
        {
            Function = new FunctionDefinition { Name = Name, Description = Description, Parameters = Parameters }
        };

        public async Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
        {
            await Task.Delay(_delayMs);
            return "DONE";
        }
    }
}
