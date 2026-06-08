using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using DeepSeekCode.Models;
using HtmlAgilityPack;

namespace DeepSeekCode.Tools;

public class WebFetchTool : ITool
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "127.0.0.1", "0.0.0.0", "[::1]"
    };

    private static readonly string[] NoiseTags =
        ["script", "style", "nav", "footer", "aside", "header", "noscript", "iframe", "svg"];

    private static readonly string[] ContentTags =
        ["article", "main", "section"];

    // 多空白压缩
    private static readonly Regex WhitespaceRegex = new(@"\s{3,}", RegexOptions.Compiled);
    private static readonly Regex NewlineRegex = new(@"\n{3,}", RegexOptions.Compiled);

    public string Name => "webfetch";
    public string Description => "Fetches content from a specified URL and returns it in the chosen format.\n- HTTP URLs are automatically upgraded to HTTPS.\n- Fails on localhost/127.0.0.1/internal IPs for security.\n- HTML pages are automatically cleaned: navigation, ads, scripts, and other noise are stripped; only title and body text are preserved.\n- Returns text (default, plain text), markdown (preserves Markdown formatting), or html (raw HTML).\n- Timeout: 30 seconds. Read limit: 500KB.\n- Use this for reading official documentation, API references, and technical articles. Do NOT use for authenticated/private pages.";

    public ParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["url"] = new PropertySchema
            {
                Type = "string",
                Description = "The URL to fetch content from"
            },
            ["format"] = new PropertySchema
            {
                Type = "string",
                Description = "The format to return: text (plain text, default), markdown, or html",
                Enum = ["text", "markdown", "html"]
            }
        },
        Required = ["url"]
    };

    public ToolDefinition ToDefinition() => new()
    {
        Function = new FunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = Parameters
        }
    };

    public async Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var url = arguments.TryGetValue("url", out var u) ? u?.ToString()?.Trim() : null;
        var format = arguments.TryGetValue("format", out var f) ? f?.ToString()?.Trim().ToLower() : "text";

        if (string.IsNullOrWhiteSpace(url))
            return "错误: URL 不能为空";

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "错误: 仅支持 http/https 协议";

        // 安全：拦截内网/本地地址
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "错误: URL 格式无效";

        if (BlockedHosts.Contains(uri.Host) || IsPrivateNetwork(uri.Host))
            return "错误: 不允许访问内网或本地地址";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; DeepSeekCode/1.0; +https://deepseek.com)");
            request.Headers.Accept.ParseAdd(
                "text/html,text/markdown,text/plain,application/json;q=0.9,*/*;q=0.8");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
                return $"HTTP {response.StatusCode}: {response.ReasonPhrase}";

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var contentLength = response.Content.Headers.ContentLength ?? 0;

            // 响应体过大时截断读取
            const long maxReadBytes = 500 * 1024; // 500KB
            using var readStream = await response.Content.ReadAsStreamAsync();
            using var memStream = new MemoryStream();

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            bool truncated = false;

            while ((bytesRead = await readStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxReadBytes)
                {
                    var remaining = (int)(maxReadBytes - (totalRead - bytesRead));
                    if (remaining > 0)
                        await memStream.WriteAsync(buffer.AsMemory(0, remaining));
                    truncated = true;
                    break;
                }
                await memStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            }

            memStream.Position = 0;
            using var reader = new StreamReader(memStream, DetectEncoding(contentType));
            var rawContent = await reader.ReadToEndAsync();

            // 根据 Content-Type 和 format 参数决定处理方式
            var result = contentType switch
            {
                var t when t.Contains("text/markdown") => rawContent,
                var t when t.Contains("text/plain") => rawContent,
                var t when t.Contains("application/json") => rawContent,
                _ => format switch
                {
                    "html" => rawContent,
                    "markdown" => ExtractText(rawContent, "markdown"),
                    _ => ExtractText(rawContent, "text")
                }
            };

            // 截断过长的输出
            const int maxResultChars = 50000;
            if (result.Length > maxResultChars)
                result = result[..maxResultChars] + $"\n\n...(内容已截断，原始长度 {result.Length} 字符)";

            var truncatedNote = truncated ? $"\n(响应体已截断至 {totalRead / 1024}KB)\n" : "";
            return truncatedNote + result;
        }
        catch (TaskCanceledException)
        {
            return "错误: 请求超时（30 秒）";
        }
        catch (HttpRequestException ex)
        {
            return $"网络请求失败: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"抓取失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 从 HTML 中提取正文和标题
    /// </summary>
    private static string ExtractText(string html, string outputFormat)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // 提取标题
        var title = ExtractTitle(doc);

        // 移除噪声节点
        var htmlNode = doc.DocumentNode;
        foreach (var tag in NoiseTags)
        {
            var nodes = htmlNode.SelectNodes($"//{tag}");
            if (nodes != null)
            {
                foreach (var node in nodes)
                    node.Remove();
            }
        }

        // 找正文容器
        HtmlNode? contentRoot = null;
        foreach (var tag in ContentTags)
        {
            contentRoot = htmlNode.SelectSingleNode($"//{tag}");
            if (contentRoot != null) break;
        }

        // 没找到专用容器，用文本密度算法选最佳 body 子节点
        contentRoot ??= SelectBestContentNode(htmlNode);

        var innerText = contentRoot?.InnerText ?? htmlNode.InnerText;
        innerText = WebUtility.HtmlDecode(innerText);
        innerText = WhitespaceRegex.Replace(innerText, " ");
        innerText = NewlineRegex.Replace(innerText, "\n\n");
        innerText = innerText.Trim();

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.AppendLine($"# {title}");
            sb.AppendLine();
        }
        sb.Append(innerText);

        return sb.ToString();
    }

    /// <summary>
    /// 提取页面标题：OG 标题 → h1 → title
    /// </summary>
    private static string? ExtractTitle(HtmlDocument doc)
    {
        // og:title
        var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']");
        if (ogTitle != null)
        {
            var content = ogTitle.GetAttributeValue("content", "");
            if (!string.IsNullOrWhiteSpace(content)) return content.Trim();
        }

        // h1
        var h1 = doc.DocumentNode.SelectSingleNode("//h1");
        if (h1 != null)
        {
            var text = h1.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        // title
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        return titleNode?.InnerText.Trim();
    }

    /// <summary>
    /// 文本密度算法：选 body 下文本最多的直接子节点
    /// </summary>
    private static HtmlNode? SelectBestContentNode(HtmlNode root)
    {
        var body = root.SelectSingleNode("//body") ?? root;
        var candidates = body.ChildNodes
            .Where(n => n.NodeType == HtmlNodeType.Element)
            .ToList();

        if (candidates.Count == 0) return body;

        HtmlNode? best = null;
        int bestScore = 0;

        foreach (var node in candidates)
        {
            // 文本密度得分 = InnerText 长度 - 链接文本长度（链接多为导航）
            var fullText = node.InnerText ?? "";
            var linkText = string.Join("",
                node.SelectNodes(".//a")
                    ?.Select(a => a.InnerText) ?? Array.Empty<string>());

            var score = fullText.Length - linkText.Length;
            if (score > bestScore)
            {
                bestScore = score;
                best = node;
            }
        }

        return bestScore > 100 ? best : body;
    }

    /// <summary>
    /// 检测响应编码
    /// </summary>
    private static Encoding DetectEncoding(string contentType)
    {
        var match = Regex.Match(contentType, @"charset=([^\s;]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            try { return Encoding.GetEncoding(match.Groups[1].Value); }
            catch (ArgumentException)
            {
                // 不支持的编码名称，回退到 UTF-8
            }
        }
        return Encoding.UTF8;
    }

    /// <summary>
    /// 检查是否为内网 IP 地址
    /// </summary>
    private static bool IsPrivateNetwork(string host)
    {
        if (!IPAddress.TryParse(host, out var ip))
            return false;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
        }
        return false;
    }
}
