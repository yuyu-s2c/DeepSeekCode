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
        _http = new HttpClient
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

        // Thinking 模式
        var thinkingType = config.ThinkingEnabled ? "enabled" : "disabled";
        requestBody["thinking"] = new { type = thinkingType };

        if (config.ThinkingEnabled)
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
        var count = (int)Math.Ceiling(text.Length / 2.5);
        return Task.FromResult(count);
    }

    public Task<int> EstimateTokenCount(List<ChatMessage> messages)
    {
        var total = 0;
        foreach (var msg in messages)
        {
            if (msg.Content != null)
                total += (int)Math.Ceiling(msg.Content.Length / 2.5);
            if (msg.ToolCalls != null)
            {
                foreach (var tc in msg.ToolCalls)
                    total += (int)Math.Ceiling(tc.Function.Arguments.Length / 2.5) + 10;
            }
        }
        return Task.FromResult(total);
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
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() > 0)
            return choices[0].GetProperty("text").GetString();

        return null;
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
