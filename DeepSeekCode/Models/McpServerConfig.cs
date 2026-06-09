using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DeepSeekCode.Models;

/// <summary>单个 MCP 服务器配置</summary>
public class McpServerConfig
{
    /// <summary>服务器名称（唯一标识，用于日志和工具命名空间）</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>启动命令（如 "npx"、"python"、"node"）</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    /// <summary>命令行参数</summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    /// <summary>环境变量</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = new();

    /// <summary>是否启用</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>启动超时（秒）</summary>
    [JsonPropertyName("startupTimeoutSeconds")]
    public int StartupTimeoutSeconds { get; set; } = 30;

    /// <summary>工作目录（默认用户目录）</summary>
    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";
}

/// <summary>MCP 全局配置（存储所有服务器列表）</summary>
public class McpConfig
{
    [JsonPropertyName("servers")]
    public List<McpServerConfig> Servers { get; set; } = new();
}
