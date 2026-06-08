using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ColorCode;
using HtmlAgilityPack;

namespace DeepSeekCode.Markdown;

/// <summary>
/// WPF 语法高亮器，基于 ColorCode.Core 引擎。
/// 将代码文本 + 语言标识符转换为 WPF Inline 元素列表。
/// </summary>
public static class SyntaxHighlighter
{
    // 语言别名映射
    private static readonly Dictionary<string, string> LanguageAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "csharp",
        ["c#"] = "csharp",
        ["fs"] = "fsharp",
        ["f#"] = "fsharp",
        ["js"] = "javascript",
        ["ts"] = "typescript",
        ["py"] = "python",
        ["rb"] = "ruby",
        ["go"] = "go",
        ["rs"] = "rust",
        ["sh"] = "shell",
        ["bash"] = "shell",
        ["zsh"] = "shell",
        ["ps1"] = "powershell",
        ["yml"] = "yaml",
        ["md"] = "markdown",
        ["dockerfile"] = "docker",
        ["h"] = "cpp",
        ["hpp"] = "cpp",
    };

    private static readonly HtmlFormatter Formatter = new();

    private static readonly Regex HexColorRegex = new(
        @"(?:^|;|\s)color:\s*#([0-9A-Fa-f]{6})",
        RegexOptions.Compiled);

    // 默认前景色
    private static readonly Brush DefaultBrush = new SolidColorBrush(Color.FromRgb(212, 212, 212));

    /// <summary>
    /// 从代码文本和语言标识生成 WPF Inline 列表。
    /// 不支持的语言回退为纯文本。
    /// </summary>
    public static List<Inline> Highlight(string code, string? language)
    {
        var lang = ResolveLanguage(language);
        if (lang == null)
            return [new Run(code)];

        try
        {
            var html = Formatter.GetHtmlString(code, lang);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var inlines = new List<Inline>();
            WalkStyledNodes(doc.DocumentNode, inlines);
            return inlines;
        }
        catch
        {
            return [new Run(code)];
        }
    }

    /// <summary>
    /// 遍历 DOM 树，提取带 color 样式的节点生成 WPF Run。
    /// 文本中的 \n 替换为 LineBreak 元素。
    /// </summary>
    private static void WalkStyledNodes(HtmlNode node, List<Inline> inlines)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child.NodeType)
            {
                case HtmlNodeType.Text:
                    AppendTextWithBreaks(WebUtility.HtmlDecode(child.InnerText), inlines);
                    break;

                case HtmlNodeType.Element:
                    switch (child.Name.ToLowerInvariant())
                    {
                        case "span":
                            var spanBrush = ParseColorFromStyle(child.GetAttributeValue("style", ""));
                            var spanText = WebUtility.HtmlDecode(child.InnerText);
                            if (!string.IsNullOrEmpty(spanText))
                                AppendTextWithBreaks(spanText, inlines, spanBrush ?? DefaultBrush);
                            break;

                        case "br":
                            inlines.Add(new LineBreak());
                            break;

                        default:
                            if (child.HasChildNodes)
                                WalkStyledNodes(child, inlines);
                            else
                            {
                                var otherText = WebUtility.HtmlDecode(child.InnerText);
                                if (!string.IsNullOrEmpty(otherText))
                                    AppendTextWithBreaks(otherText, inlines);
                            }
                            break;
                    }
                    break;

                default:
                    if (child.HasChildNodes)
                        WalkStyledNodes(child, inlines);
                    break;
            }
        }
    }

    /// <summary>
    /// 将文本按 \n 分割，中间插入 LineBreak
    /// </summary>
    private static void AppendTextWithBreaks(string text, List<Inline> inlines, Brush? brush = null)
    {
        brush ??= DefaultBrush;
        var parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                inlines.Add(new Run(parts[i]) { Foreground = brush });
            if (i < parts.Length - 1)
                inlines.Add(new LineBreak());
        }
    }

    /// <summary>
    /// 从 CSS style 字符串中提取颜色 Brush
    /// </summary>
    private static Brush? ParseColorFromStyle(string style)
    {
        if (string.IsNullOrEmpty(style))
            return null;

        var match = HexColorRegex.Match(style);
        if (!match.Success)
            return null;

        var hex = match.Groups[1].Value;
        try
        {
            return new SolidColorBrush(
                Color.FromRgb(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16)));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析语言标识符
    /// </summary>
    private static ILanguage? ResolveLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        var langId = language.Trim().ToLowerInvariant();

        // 别名映射
        if (LanguageAlias.TryGetValue(langId, out var mapped))
            langId = mapped;

        return Languages.FindById(langId);
    }
}
