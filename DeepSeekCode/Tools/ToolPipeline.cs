namespace DeepSeekCode.Tools;

/// <summary>
/// 工具管线过滤器接口。
/// 在工具执行前后插入自定义逻辑（权限校验、日志、超时等）。
/// </summary>
public interface IToolPipelineFilter
{
    /// <summary>过滤器名称</summary>
    string Name { get; }

    /// <summary>优先级（数字越小越先执行）</summary>
    int Priority { get; }

    /// <summary>工具执行前调用。返回 false 拦截执行</summary>
    Task<bool> OnBeforeExecuteAsync(ToolCallContext context);

    /// <summary>工具执行后调用（无论成功或失败）</summary>
    Task OnAfterExecuteAsync(ToolCallContext context, string result);
}

/// <summary>
/// 工具调用上下文（贯穿整个管线）
/// </summary>
public class ToolCallContext
{
    public string ToolName { get; init; } = "";
    public string? ToolCallId { get; init; }
    public Dictionary<string, object?> Arguments { get; init; } = new();
    public DateTime StartTime { get; init; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
    public TimeSpan Elapsed => (EndTime ?? DateTime.Now) - StartTime;
    public bool Cancelled { get; set; }
    public string? Error { get; set; }

    /// <summary>上下文数据袋（过滤器间传递数据）</summary>
    public Dictionary<string, object?> Bag { get; } = new();
}

/// <summary>
/// 工具管线：编排过滤器和实际工具执行
/// </summary>
public class ToolPipeline
{
    private readonly List<IToolPipelineFilter> _filters = new();
    private readonly ITool _tool;
    private readonly Services.WorkspaceService? _workspaceService;

    public ToolPipeline(ITool tool, Services.WorkspaceService? workspaceService = null)
    {
        _tool = tool;
        _workspaceService = workspaceService;
    }

    /// <summary>添加过滤器</summary>
    public ToolPipeline AddFilter(IToolPipelineFilter filter)
    {
        _filters.Add(filter);
        _filters.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        return this;
    }

    /// <summary>执行管线</summary>
    public async Task<string> ExecuteAsync(ToolCallContext context)
    {
        CancellationTokenRegistration? ctRegistration = null;
        CancellationTokenSource? timeoutCts = null;

        try
        {
            // Before 钩子 — 过滤器在这里设置超时等参数
            foreach (var filter in _filters)
            {
                var allowed = await filter.OnBeforeExecuteAsync(context);
                if (!allowed)
                {
                    context.Cancelled = true;
                    return $"工具 '{context.ToolName}' 执行被拦截（{filter.Name}）";
                }
            }

            // 在 Before 钩子之后读取超时配置（因为 TimeoutFilter 在 OnBeforeExecuteAsync 中设置 Bag）
            if (context.Bag.TryGetValue("TimeoutMs", out var timeoutObj) && timeoutObj is int timeoutMs && timeoutMs > 0)
            {
                timeoutCts = new CancellationTokenSource(timeoutMs);
                if (context.Bag.TryGetValue("___ct___", out var externalCt) && externalCt is CancellationToken ct)
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                    ctRegistration = linked.Token.Register(() => context.Cancelled = true);
                }
                else
                {
                    ctRegistration = timeoutCts.Token.Register(() => context.Cancelled = true);
                }
            }

            if (context.Cancelled)
                return $"工具 '{context.ToolName}' 执行已取消";

            // 实际执行（带统一超时）
            string result;
            try
            {
                using (_workspaceService?.EnterWorkspace())
                {
                    var executeTask = _tool.ExecuteAsync(context.Arguments);
                    if (timeoutCts != null)
                    {
                        var completed = await Task.WhenAny(executeTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));
                        if (completed != executeTask)
                            throw new OperationCanceledException("工具执行超时");
                        result = await executeTask;
                    }
                    else
                    {
                        result = await executeTask;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                context.Cancelled = true;
                context.Error = "工具执行超时";
                result = $"工具 '{context.ToolName}' 执行超时";
            }
            catch (Exception ex)
            {
                context.Error = ex.Message;
                result = $"工具执行异常: {ex.Message}";
            }

            context.EndTime = DateTime.Now;

            // After 钩子
            foreach (var filter in _filters)
                await filter.OnAfterExecuteAsync(context, result);

            return result;
        }
        finally
        {
            ctRegistration?.Dispose();
            timeoutCts?.Dispose();
        }
    }
}

// ── 内置过滤器 ──

/// <summary>超时过滤器</summary>
public class TimeoutFilter : IToolPipelineFilter
{
    private readonly int _timeoutMs;

    public string Name => "Timeout";
    public int Priority => 100;

    public TimeoutFilter(int timeoutMs = 30000)
    {
        _timeoutMs = timeoutMs;
    }

    public Task<bool> OnBeforeExecuteAsync(ToolCallContext context)
    {
        context.Bag["TimeoutMs"] = _timeoutMs;
        return Task.FromResult(true);
    }

    public Task OnAfterExecuteAsync(ToolCallContext context, string result)
    {
        // 在 ToolPipeline 层面，超时由 ShellTool 内部处理
        return Task.CompletedTask;
    }
}

/// <summary>日志过滤器</summary>
public class LoggingFilter : IToolPipelineFilter
{
    private readonly Action<string> _logger;

    public string Name => "Logging";
    public int Priority => 200;

    public LoggingFilter(Action<string>? logger = null)
    {
        _logger = logger ?? (msg => System.Diagnostics.Debug.WriteLine(msg));
    }

    public Task<bool> OnBeforeExecuteAsync(ToolCallContext context)
    {
        _logger($"[ToolPipeline] 开始执行: {context.ToolName} | 参数: {string.Join(", ", context.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))}");
        return Task.FromResult(true);
    }

    public Task OnAfterExecuteAsync(ToolCallContext context, string result)
    {
        var status = context.Error != null ? "失败" : (context.Cancelled ? "取消" : "完成");
        _logger($"[ToolPipeline] {status}: {context.ToolName} | 耗时: {context.Elapsed.TotalMilliseconds:F0}ms | 结果长度: {result.Length}");
        return Task.CompletedTask;
    }
}

/// <summary>权限过滤器</summary>
public class PermissionPipelineFilter : IToolPipelineFilter
{
    private readonly Services.PermissionManager _permissionManager;
    private readonly Func<string, string, Task<Services.PermissionDecision?>>? _userPromptCallback;

    public string Name => "Permission";
    public int Priority => 10;

    public PermissionPipelineFilter(
        Services.PermissionManager permissionManager,
        Func<string, string, Task<Services.PermissionDecision?>>? userPromptCallback = null)
    {
        _permissionManager = permissionManager;
        _userPromptCallback = userPromptCallback;
    }

    public async Task<bool> OnBeforeExecuteAsync(ToolCallContext context)
    {
        var command = context.Arguments.TryGetValue("command", out var cmd) ? cmd?.ToString() : null;
        var level = _permissionManager.Check(context.ToolName, command);

        switch (level)
        {
            case Services.PermissionLevel.Allow:
                return true;
            case Services.PermissionLevel.Deny:
                return false;
            case Services.PermissionLevel.Ask:
                if (_userPromptCallback != null)
                {
                    var decision = await _userPromptCallback(context.ToolName, command ?? context.ToolName);
                    if (decision == Services.PermissionDecision.AllowAll)
                        _permissionManager.AllowAllForSession();
                    return decision == Services.PermissionDecision.AllowOnce
                        || decision == Services.PermissionDecision.AllowAll;
                }
                return true;
            default:
                return true;
        }
    }

    public Task OnAfterExecuteAsync(ToolCallContext context, string result)
    {
        return Task.CompletedTask;
    }
}
