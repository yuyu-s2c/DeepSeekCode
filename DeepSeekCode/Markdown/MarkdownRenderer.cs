using System.Windows;
using System.Windows.Documents;
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
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei"),
                FontSize = 14,
                PagePadding = new Thickness(0)
            };

        var doc = Markdig.Wpf.Markdown.ToFlowDocument(markdown, Pipeline);
        doc.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei");
        doc.FontSize = 14;
        doc.PagePadding = new Thickness(0);
        return doc;
    }
}
