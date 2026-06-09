using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using DeepSeekCode.Models;

namespace DeepSeekCode.MCP;

/// <summary>MCP 协议客户端（JSON-RPC 2.0 over stdio）</summary>
public class McpClient : IDisposable
{
    private readonly McpServerConfig _config;
    private readonly ILogger _logger;
    private Process? _process;
    private int _requestId;
    private bool _initialized;
    private bool _disposed;

    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    public string ServerName => _config.Name;

    public McpClient(McpServerConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>启动 MCP 服务器进程并完成握手</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        var psi = new ProcessStartInfo
        {
            FileName = _config.Command,
            WorkingDirectory = string.IsNullOrWhiteSpace(_config.WorkingDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : _config.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };

        // 构建参数字符串
        foreach (var arg in _config.Args)
            psi.ArgumentList.Add(arg);

        // 环境变量
        foreach (var kv in _config.Env)
            psi.Environment[kv.Key] = kv.Value;

        _process = new Process { StartInfo = psi };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.Info($"[MCP:{_config.Name}] stderr: {e.Data}");
        };

        _process.Start();
        _process.BeginErrorReadLine();

        _readCts = new CancellationTokenSource();
        _readTask = ReadLoopAsync(_readCts.Token);

        // ── 握手：initialize → initialized ──
        var initResult = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { tools = new { } },
            clientInfo = new { name = "DeepSeekCode", version = "1.0.0" }
        }, ct);

        var serverInfo = initResult.TryGetProperty("serverInfo", out var si)
            ? si.GetProperty("name").GetString() : "unknown";
        _logger.Info($"[MCP:{_config.Name}] 已连接 → {serverInfo}");

        // 发送 initialized 通知
        await SendNotificationAsync("notifications/initialized", null);
        _initialized = true;
    }

    /// <summary>获取服务器提供的工具列表</summary>
    public async Task<List<McpToolInfo>> ListToolsAsync(CancellationToken ct = default)
    {
        if (!_initialized)
            throw new InvalidOperationException("MCP 客户端未初始化");

        var result = await SendRequestAsync("tools/list", null, ct);
        var toolsJson = result.GetProperty("tools");
        var tools = new List<McpToolInfo>();

        foreach (var tool in toolsJson.EnumerateArray())
        {
            tools.Add(new McpToolInfo
            {
                Name = tool.GetProperty("name").GetString() ?? "",
                Description = tool.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                InputSchema = tool.TryGetProperty("inputSchema", out var schema) ? schema : null
            });
        }

        _logger.Info($"[MCP:{_config.Name}] 发现 {tools.Count} 个工具");
        return tools;
    }

    /// <summary>调用 MCP 工具</summary>
    public async Task<string> CallToolAsync(string toolName, Dictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        if (!_initialized)
            throw new InvalidOperationException("MCP 客户端未初始化");

        var result = await SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments
        }, ct);

        // 解析结果: content 数组
        var sb = new StringBuilder();
        if (result.TryGetProperty("content", out var contentArr))
        {
            foreach (var item in contentArr.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var t) ? t.GetString() : "text";
                var text = item.TryGetProperty("text", out var txt) ? txt.GetString() : "";

                if (type == "text" && text != null)
                    sb.AppendLine(text);
                else if (type == "resource" && item.TryGetProperty("resource", out var res))
                    sb.AppendLine(JsonSerializer.Serialize(res));
            }
        }

        // isError 标记
        if (result.TryGetProperty("isError", out var isErr) && isErr.GetBoolean())
            sb.Insert(0, "[MCP 工具返回错误]\n");

        return sb.ToString().TrimEnd();
    }

    // ═══════════════════════════════════════════
    //  JSON-RPC 核心
    // ═══════════════════════════════════════════

    private async Task<JsonElement> SendRequestAsync(string method, object? @params,
        CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        };

        var tcs = new TaskCompletionSource<JsonElement>();
        lock (_lock) _pendingRequests[id] = tcs;

        await WriteLineAsync(JsonSerializer.Serialize(request), ct);

        using var reg = ct.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    private async Task SendNotificationAsync(string method, object? @params,
        CancellationToken ct = default)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params
        };
        await WriteLineAsync(JsonSerializer.Serialize(notification), ct);
    }

    private async Task WriteLineAsync(string json, CancellationToken ct)
    {
        if (_process?.StandardInput == null)
            throw new InvalidOperationException("MCP 进程未启动");

        await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            var reader = _process?.StandardOutput;
            if (reader == null) return;

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break; // 进程退出

                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    // 响应（有 id）
                    if (root.TryGetProperty("id", out var idElem) && idElem.ValueKind != JsonValueKind.Null)
                    {
                        var id = idElem.GetInt32();
                        TaskCompletionSource<JsonElement>? tcs;
                        lock (_lock) _pendingRequests.Remove(id, out tcs);

                        if (tcs == null) continue;

                        if (root.TryGetProperty("error", out var error))
                        {
                            var errMsg = error.TryGetProperty("message", out var em)
                                ? em.GetString() : "未知 RPC 错误";
                            tcs.TrySetException(new McpRpcException(errMsg ?? "MCP 错误"));
                        }
                        else if (root.TryGetProperty("result", out var result))
                        {
                            tcs.TrySetResult(result);
                        }
                    }
                    // 服务器通知（无 id）— 暂不处理
                }
                catch (JsonException ex)
                {
                    _logger.Warn($"[MCP:{_config.Name}] 无法解析响应: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex)
        {
            _logger.Warn($"[MCP:{_config.Name}] 读取循环 IO 异常: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _readCts?.Cancel();
        try { _readTask?.Wait(2000); } catch { }

        lock (_lock)
        {
            foreach (var kv in _pendingRequests)
                kv.Value.TrySetCanceled();
            _pendingRequests.Clear();
        }

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch { }
        _process?.Dispose();
        _readCts?.Dispose();
    }
}

/// <summary>从 MCP 服务器发现的工具信息</summary>
public class McpToolInfo
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public System.Text.Json.JsonElement? InputSchema { get; init; }
}

/// <summary>MCP JSON-RPC 错误</summary>
public class McpRpcException : Exception
{
    public McpRpcException(string message) : base(message) { }
}

/// <summary>简易日志接口（避免循环依赖）</summary>
public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}
