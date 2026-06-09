using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using DeepSeekCode.Models;
using DeepSeekCode.Tools;

namespace DeepSeekCode.MCP;

/// <summary>MCP 服务管理器：加载配置、启动连接、发现工具、注册到 ToolRegistry</summary>
public class McpService : IDisposable
{
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger _logger;
    private readonly List<McpClient> _clients = new();
    private readonly List<McpToolAdapter> _adapters = new();
    private bool _disposed;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepseek-code", "mcp-servers.json");

    public IReadOnlyList<McpToolAdapter> RegisteredTools => _adapters.AsReadOnly();

    public McpService(ToolRegistry toolRegistry, ILogger logger)
    {
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    /// <summary>加载配置并启动所有已启用的 MCP 服务器</summary>
    public async Task StartAllAsync(CancellationToken ct = default)
    {
        var config = LoadConfig();

        foreach (var serverConfig in config.Servers)
        {
            if (!serverConfig.Enabled) continue;
            if (string.IsNullOrWhiteSpace(serverConfig.Command)) continue;

            try
            {
                var client = new McpClient(serverConfig, _logger);
                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(serverConfig.StartupTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                await client.InitializeAsync(linkedCts.Token);
                _clients.Add(client);

                var tools = await client.ListToolsAsync(ct);

                foreach (var toolInfo in tools)
                {
                    // 工具名加服务器前缀防止冲突
                    var fullName = $"mcp_{serverConfig.Name}_{toolInfo.Name}";
                    var adapter = new McpToolAdapter(client, toolInfo, fullName, serverConfig.Name);
                    _toolRegistry.Register(adapter);
                    _adapters.Add(adapter);
                }

                _logger.Info($"MCP 服务器 [{serverConfig.Name}] 启动成功，注册 {tools.Count} 个工具");
            }
            catch (Exception ex)
            {
                _logger.Warn($"MCP 服务器 [{serverConfig.Name}] 启动失败: {ex.Message}");
            }
        }
    }

    public static McpConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<McpConfig>(json) ?? new McpConfig();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[McpService] 配置加载失败: {ex.Message}");
        }
        return new McpConfig();
    }

    public static void SaveConfig(McpConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>获取当前配置（用于设置窗口）</summary>
    public static McpConfig GetConfig() => LoadConfig();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var adapter in _adapters)
            _toolRegistry.Unregister(adapter.Name);

        foreach (var client in _clients)
            client.Dispose();

        _clients.Clear();
        _adapters.Clear();
    }
}
