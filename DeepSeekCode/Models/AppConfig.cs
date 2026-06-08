using System.Text.Json.Serialization;

namespace DeepSeekCode.Models;

public class AppConfig
{
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; set; } = "https://api.deepseek.com";

    // ── 模型 ──

    [JsonPropertyName("model")]
    public string Model { get; set; } = "deepseek-v4-pro";

    [JsonPropertyName("maxTokens")]
    public int MaxTokens { get; set; } = 8192;

    // ── Thinking 模式 ──

    [JsonPropertyName("thinkingEnabled")]
    public bool ThinkingEnabled { get; set; } = true;

    [JsonPropertyName("reasoningEffort")]
    public string ReasoningEffort { get; set; } = "max";

    // ── 生成参数（仅 Non-Thinking 模式生效） ──

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("topP")]
    public double TopP { get; set; } = 1.0;

    [JsonPropertyName("frequencyPenalty")]
    public double FrequencyPenalty { get; set; } = 0.0;

    [JsonPropertyName("presencePenalty")]
    public double PresencePenalty { get; set; } = 0.0;

    // ── Beta 功能 ──

    [JsonPropertyName("enableJsonOutput")]
    public bool EnableJsonOutput { get; set; } = false;

    [JsonPropertyName("enablePrefixCompletion")]
    public bool EnablePrefixCompletion { get; set; } = false;

    [JsonPropertyName("prefixContent")]
    public string PrefixContent { get; set; } = "";

    // ── 工作区记忆 ──

    [JsonPropertyName("lastWorkspacePath")]
    public string LastWorkspacePath { get; set; } = "";
}
