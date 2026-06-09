using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DeepSeekCode.Models;

namespace DeepSeekCode.Services;

public class DeepSeekClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public DeepSeekClient(AppConfig config)
    {
        // 使用 SocketsHttpHandler 启用连接池、Keep-Alive、HTTP/2 多路复用
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            MaxConnectionsPerServer = 4
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(config.ApiBaseUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
    }

    public async IAsyncEnumerable<StreamChunk> StreamChatAsync(
        List<ToolDefinition> tools,
        List<ChatMessage> messages,
        AppConfig config,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["model"] = config.Model,
            ["messages"] = messages,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true },
            ["max_tokens"] = config.MaxTokens
        };

        if (tools.Count > 0)
            requestBody["tools"] = tools;

        // Thinking 模式: adaptive 让模型自行判断是否需要思考
        var thinkingType = config.ThinkingEnabled ? "adaptive" : "disabled";
        requestBody["thinking"] = new { type = thinkingType };

        // adaptive 模式下仍可传 reasoning_effort 作为偏好提示
        if (config.ThinkingEnabled && !string.IsNullOrEmpty(config.ReasoningEffort))
            requestBody["reasoning_effort"] = config.ReasoningEffort;

        // 生成参数（Thinking 模式下无效但发送不报错）
        if (!config.ThinkingEnabled)
        {
            requestBody["temperature"] = config.Temperature;
            requestBody["top_p"] = config.TopP;
            requestBody["frequency_penalty"] = config.FrequencyPenalty;
            requestBody["presence_penalty"] = config.PresencePenalty;
        }

        // JSON Output 模式
        if (config.EnableJsonOutput)
            requestBody["response_format"] = new { type = "json_object" };

        // Chat Prefix Completion
        if (config.EnablePrefixCompletion && !string.IsNullOrWhiteSpace(config.PrefixContent))
            requestBody["prefix"] = config.PrefixContent;

        var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = content
        };

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"API 返回 {response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);

            if (line == null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data: "))
                continue;

            var dataStr = line[6..];

            if (dataStr == "[DONE]")
                yield break;

            var chunk = JsonSerializer.Deserialize<StreamChunk>(dataStr, _jsonOptions);
            if (chunk is { Choices.Count: > 0 } || chunk?.Usage != null)
                yield return chunk;
        }
    }

    public Task<int> EstimateTokenCount(string text)
    {
        return Task.FromResult(EstimateTokenCountSync(text));
    }

    public Task<int> EstimateTokenCount(List<ChatMessage> messages)
    {
        return Task.FromResult(EstimateTokenCountSync(messages));
    }

    /// <summary>
    /// 按字符类型精确估算 token 数。
    /// 官方换算（DeepSeek API Docs）：
    ///   1 英文字符 ≈ 0.3 token
    ///   1 中文字符 ≈ 0.6 token
    /// 代码/数字/标点按英文比率 0.3 计算。
    /// </summary>
    public static int EstimateTokenCountSync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // 统计各类字符数
        var asciiChars = 0;   // 英文、数字、标点、空格等
        var cjkChars = 0;     // 中文、日文、韩文等全角字符

        foreach (var c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF ||   // CJK 统一汉字
                c >= 0x3400 && c <= 0x4DBF ||   // CJK 扩展 A
                c >= 0x20000 && c <= 0x2A6DF || // CJK 扩展 B
                c >= 0xF900 && c <= 0xFAFF ||   // CJK 兼容汉字
                c >= 0x3040 && c <= 0x309F ||   // 平假名
                c >= 0x30A0 && c <= 0x30FF ||   // 片假名
                c >= 0xAC00 && c <= 0xD7AF)     // 韩文
                cjkChars++;
            else
                asciiChars++;
        }

        // 英文/代码/标点: 0.3 token/char，中文: 0.6 token/char
        // 加上少量 overhead
        return (int)Math.Ceiling(asciiChars * 0.3 + cjkChars * 0.6);
    }

    /// <summary>同步估算消息列表 token（无需 API 调用，用于锁内快速计算）</summary>
    public static int EstimateTokenCountSync(List<ChatMessage> messages)
    {
        var total = 0;
        foreach (var msg in messages)
        {
            if (msg.Content != null)
                total += EstimateTokenCountSync(msg.Content);
            if (msg.ToolCalls != null)
            {
                foreach (var tc in msg.ToolCalls)
                    total += EstimateTokenCountSync(tc.Function.Arguments) + 10;
            }
        }
        return total;
    }

    // ═══ FIM Completion (Beta) ═══

    /// <summary>
    /// FIM (Fill-in-the-Middle) 补全 — /v1/completions 端点
    /// 仅 Non-Thinking 模式支持
    /// </summary>
    public async Task<string?> CompleteAsync(string prompt, string suffix, AppConfig config, CancellationToken ct = default)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["model"] = config.Model,
            ["prompt"] = prompt,
            ["suffix"] = suffix,
            ["max_tokens"] = config.MaxTokens
        };

        if (!config.ThinkingEnabled)
        {
            requestBody["temperature"] = config.Temperature;
            requestBody["top_p"] = config.TopP;
        }

        var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/completions")
        {
            Content = content
        };

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"FIM API 返回 {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() > 0)
            return choices[0].GetProperty("text").GetString();

        return null;
    }

    /// <summary>运行时更新 API 凭据（无需重启应用）</summary>
    public void UpdateCredentials(string apiKey, string apiBaseUrl)
    {
        _http.BaseAddress = new Uri(apiBaseUrl);
        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }
}
