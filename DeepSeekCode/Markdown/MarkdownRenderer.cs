using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;

namespace DeepSeekCode.Markdown;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static FlowDocument Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new FlowDocument
            {
                FontFamily = new FontFamily("Microsoft YaHei"),
                FontSize = 14,
                PagePadding = new Thickness(0)
            };

        var doc = Markdig.Wpf.Markdown.ToFlowDocument(markdown, Pipeline);
        doc.FontFamily = new FontFamily("Microsoft YaHei");
        doc.FontSize = 14;
        doc.PagePadding = new Thickness(0);

        // 后处理：为代码块应用 ColorCode 语法着色
        ApplySyntaxHighlighting(doc);

        return doc;
    }

    private static void ApplySyntaxHighlighting(FlowDocument doc)
    {
        foreach (Block block in doc.Blocks.ToList())
        {
            HighlightCodeInBlock(block);
        }
    }

    private static void HighlightCodeInBlock(Block block)
    {
        // 递归处理嵌套块
        if (block is Section section)
        {
            foreach (Block child in section.Blocks.ToList())
                HighlightCodeInBlock(child);
            return;
        }

        if (block is BlockUIContainer uiContainer && uiContainer.Child is Border border)
        {
            WalkForCodeParagraphs(border);
        }
    }

    private static void WalkForCodeParagraphs(DependencyObject element)
    {
        // Markdig.Wpf 代码块通常包裹在 Border → StackPanel → Paragraph 中
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);

            if (child is Paragraph para && IsCodeParagraph(para))
            {
                ApplyHighlightToParagraph(para);
            }
            else if (child is TextBlock tb && IsCodeTextBlock(tb))
            {
                ApplyHighlightToTextBlock(tb);
            }
            else
            {
                WalkForCodeParagraphs(child);
            }
        }
    }

    private static bool IsCodeParagraph(Paragraph para)
    {
        return para.FontFamily?.Source?.Contains("Consolas") == true
            || para.FontFamily?.Source?.Contains("monospace") == true
            || para.FontFamily?.Source?.Contains("Cascadia") == true
            || (para.Background != null && IsDarkBackground(para.Background));
    }

    private static bool IsCodeTextBlock(TextBlock tb)
    {
        return tb.FontFamily?.Source?.Contains("Consolas") == true
            || tb.FontFamily?.Source?.Contains("monospace") == true
            || tb.FontFamily?.Source?.Contains("Cascadia") == true;
    }

    private static bool IsDarkBackground(Brush brush)
    {
        if (brush is SolidColorBrush scb)
        {
            var c = scb.Color;
            return c.R < 60 && c.G < 60 && c.B < 80;
        }
        return false;
    }

    /// <summary>
    /// 提取段落纯文本，用 ColorCode 语法高亮后替换 Inlines
    /// </summary>
    private static void ApplyHighlightToParagraph(Paragraph para)
    {
        var text = ExtractText(para);
        if (string.IsNullOrWhiteSpace(text)) return;

        // 尝试从上下文推断语言
        var language = InferLanguage(para);
        var highlighted = SyntaxHighlighter.Highlight(text, language);

        para.Inlines.Clear();
        para.Inlines.AddRange(highlighted);
    }

    private static void ApplyHighlightToTextBlock(TextBlock tb)
    {
        var text = tb.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        var language = InferLanguage(tb);
        var highlighted = SyntaxHighlighter.Highlight(text, language);

        tb.Inlines.Clear();
        tb.Inlines.AddRange(highlighted);
    }

    private static string ExtractText(Paragraph para)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Inline inline in para.Inlines)
        {
            if (inline is Run run)
                sb.Append(run.Text);
            else if (inline is LineBreak)
                sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 从代码块上方语言标签推断编程语言
    /// </summary>
    private static string? InferLanguage(DependencyObject element)
    {
        // 尝试找同级或父级的语言标签 TextBlock
        if (element is FrameworkElement fe && fe.Parent is Panel panel)
        {
            // Markdig.Wpf 的代码块结构: StackPanel → [TextBlock(语言标签), TextBlock(代码)]
            var children = panel.Children;
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i] is TextBlock langLabel
                    && langLabel != element
                    && !string.IsNullOrWhiteSpace(langLabel.Text)
                    && langLabel.FontSize < 13)
                {
                    return langLabel.Text.Trim();
                }
            }
        }
        return null;
    }
}
