namespace DeepSeekCode.Services;

/// <summary>
/// 全局事件总线，解耦 UI 层与核心逻辑。
/// 各模块通过 Subscribe/Publish 通信，不直接引用。
/// </summary>
public class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();

    /// <summary>
    /// 订阅事件
    /// </summary>
    public void Subscribe<T>(Action<T> handler) where T : class
    {
        lock (_lock)
        {
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _handlers[type] = list;
            }
            list.Add(handler);
        }
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public void Unsubscribe<T>(Action<T> handler) where T : class
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(typeof(T), out var handlers))
                handlers.Remove(handler);
        }
    }

    /// <summary>
    /// 发布事件（同步，按注册顺序执行）
    /// </summary>
    public void Publish<T>(T @event) where T : class
    {
        List<Delegate>? handlers;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out handlers))
                return;
            // 拷贝快照，避免回调中修改集合导致死锁
            handlers = new List<Delegate>(handlers);
        }
        for (var i = 0; i < handlers.Count; i++)
            ((Action<T>)handlers[i])(@event);
    }

    /// <summary>
    /// 发布事件（异步并行，不等待完成）
    /// </summary>
    public void PublishAsync<T>(T @event) where T : class
    {
        List<Delegate>? handlers;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out handlers))
                return;
            // 拷贝快照，避免回调中修改集合导致死锁
            handlers = new List<Delegate>(handlers);
        }
        foreach (var handler in handlers)
            Task.Run(() => ((Action<T>)handler)(@event));
    }
}

// ── 事件定义 ──

/// <summary>流式输出开始</summary>
public class StreamStartedEvent
{
    public string? UserMessage { get; init; }
}

/// <summary>流式输出文本块到达</summary>
public class StreamChunkEvent
{
    public string? Content { get; init; }
    public string? ReasoningContent { get; init; }
}

/// <summary>流式输出结束</summary>
public class StreamCompletedEvent
{
    public string? FullResponse { get; init; }
    public string? FullReasoning { get; init; }
}

/// <summary>流式输出取消</summary>
public class StreamCancelledEvent { }

/// <summary>流式输出出错</summary>
public class StreamErrorEvent
{
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
}

/// <summary>准备调用工具</summary>
public class ToolCallRequestEvent
{
    public string? ToolName { get; init; }
    public string? Arguments { get; init; }
}

/// <summary>工具调用完成</summary>
public class ToolCallResultEvent
{
    public string? ToolName { get; init; }
    public string? Result { get; init; }
    public bool Success { get; init; }
}

/// <summary>权限询问（等待用户确认）</summary>
public class PermissionRequestEvent
{
    public string? ToolName { get; init; }
    public string? Command { get; init; }
    public PermissionLevel Level { get; init; }
    public bool? UserDecision { get; set; }
}

/// <summary>会话切换</summary>
public class SessionChangedEvent
{
    public string? SessionId { get; init; }
    public string? SessionTitle { get; init; }
}

/// <summary>配置变更</summary>
public class ConfigChangedEvent
{
    public string? Model { get; init; }
    /// <summary>API Key 或 Base URL 是否变更（需重建客户端凭据）</summary>
    public bool CredentialsChanged { get; init; }
}

/// <summary>打开设置窗口请求</summary>
public class OpenSettingsRequestedEvent { }

/// <summary>Token 用量更新（流式结束时携带缓存命中信息）</summary>
public class UsageUpdatedEvent
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
    public int CacheHitTokens { get; init; }
    public int CacheMissTokens { get; init; }
    public int ReasoningTokens { get; init; }
}

/// <summary>Todo 列表更新（由 todo_write 工具触发）</summary>
public class TodosUpdatedEvent
{
    public List<Models.TodoItem> Todos { get; init; } = new();
}

/// <summary>子代理启动</summary>
public class SubagentStartedEvent
{
    public string TaskId { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "";
}

/// <summary>子代理完成</summary>
public class SubagentCompletedEvent
{
    public string TaskId { get; init; } = "";
    public bool Success { get; init; }
    public string Summary { get; init; } = "";
    public TimeSpan Duration { get; init; }
}

/// <summary>全部子代理执行完毕</summary>
public class SubagentAllCompletedEvent { }