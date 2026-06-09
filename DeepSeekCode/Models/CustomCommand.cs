using System.Text.Json.Serialization;

namespace DeepSeekCode.Models;

/// <summary>用户自定义 Slash 命令</summary>
public class CustomCommand
{
    /// <summary>命令名称（不含 / 前缀）</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>简短描述</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>提示词模板（{args} 替换为用户输入）</summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";
}

/// <summary>自定义命令集合</summary>
public class CustomCommandConfig
{
    [JsonPropertyName("commands")]
    public List<CustomCommand> Commands { get; set; } = new();
}
