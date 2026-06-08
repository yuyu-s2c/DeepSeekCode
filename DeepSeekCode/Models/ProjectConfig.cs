using System.Text.Json.Serialization;

namespace DeepSeekCode.Models;

/// <summary>
/// 项目级配置（项目根目录下的 .deepseek-code/project.json）
/// </summary>
public class ProjectConfig
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }

    [JsonPropertyName("includeFiles")]
    public List<string>? IncludeFiles { get; set; }

    [JsonPropertyName("excludeFiles")]
    public List<string>? ExcludeFiles { get; set; }

    [JsonPropertyName("maxContextTokens")]
    public int? MaxContextTokens { get; set; }

    [JsonPropertyName("autoCommit")]
    public bool? AutoCommit { get; set; }

    [JsonPropertyName("customTools")]
    public List<string>? CustomTools { get; set; }
}
