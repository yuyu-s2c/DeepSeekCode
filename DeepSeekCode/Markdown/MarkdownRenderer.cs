using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdContainerInline = Markdig.Syntax.Inlines.ContainerInline;

namespace DeepSeekCode.Markdown;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static FlowDocument Render(string? markdown)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = 14,
            PagePadding = new Thickness(0)
        };

        if (string.IsNullOrWhiteSpace(markdown))
            return doc;

        var parsed = Markdig.Markdown.Parse(markdown, Pipeline);
        foreach (MdBlock block in parsed)
            RenderBlock(doc, block);

        return doc;
    }

    private static void RenderBlock(FlowDocument doc, MdBlock block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                RenderHeading(doc, heading);
                break;
            case CodeBlock code:
                RenderCodeBlock(doc, code);
                break;
            case ListBlock list:
                RenderList(doc, list);
                break;
            case ParagraphBlock para:
                RenderParagraph(doc, para);
                break;
            case ThematicBreakBlock _:
                RenderHorizontalRule(doc);
                break;
            case QuoteBlock quote:
                RenderQuote(doc, quote);
                break;
            default:
                if (block is LeafBlock leaf && leaf.Inline != null)
                {
                    var para = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
                    para.Inlines.AddRange(RenderInlines(leaf.Inline));
                    doc.Blocks.Add(para);
                }
                break;
        }
    }

    private static void RenderHeading(FlowDocument doc, HeadingBlock heading)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(0, heading.Level == 1 ? 12 : 8, 0, 4),
            FontWeight = FontWeights.Bold,
            FontSize = heading.Level switch { 1 => 20, 2 => 17, 3 => 15, _ => 14 }
        };
        para.Inlines.AddRange(RenderInlines(heading.Inline));
        doc.Blocks.Add(para);
    }

    private static void RenderParagraph(FlowDocument doc, ParagraphBlock paraBlock)
    {
        var para = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
        para.Inlines.AddRange(RenderInlines(paraBlock.Inline));
        doc.Blocks.Add(para);
    }

    private static void RenderCodeBlock(FlowDocument doc, CodeBlock codeBlock)
    {
        var code = codeBlock.Lines.ToString();
        var language = (codeBlock is FencedCodeBlock fenced) ? fenced.Info : null;

        var container = new StackPanel();

        // 语言标签
        if (!string.IsNullOrWhiteSpace(language))
        {
            container.Children.Add(new TextBlock
            {
                Text = language,
                FontSize = 11,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                Foreground = new SolidColorBrush(Color.FromRgb(160, 192, 208)),
                Padding = new Thickness(12, 6, 12, 2),
                Background = new SolidColorBrush(Color.FromRgb(26, 42, 58))
            });
        }

        // 代码区域（带语法高亮）
        var richText = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(12, 4, 12, 8),
            Background = new SolidColorBrush(Color.FromRgb(26, 42, 58))
        };

        var inlines = SyntaxHighlighter.Highlight(code, language);
        richText.Inlines.AddRange(inlines);

        container.Children.Add(richText);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 42, 58)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 64, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 8, 0, 8),
            Child = container,
            ClipToBounds = true
        };

        doc.Blocks.Add(new BlockUIContainer(border));
    }

    private static void RenderList(FlowDocument doc, ListBlock listBlock)
    {
        var list = new System.Windows.Documents.List
        {
            Margin = new Thickness(16, 4, 0, 4),
            MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc
        };

        foreach (MdBlock item in listBlock)
        {
            if (item is ListItemBlock listItem)
                RenderListItem(list, listItem, 0);
        }

        doc.Blocks.Add(list);
    }

    private static void RenderListItem(
        System.Windows.Documents.List parentList,
        ListItemBlock listItem,
        int depth)
    {
        var li = new ListItem();

        var para = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };

        // ListItemBlock's inline content is in its first ParagraphBlock child
        var childPara = listItem.OfType<ParagraphBlock>().FirstOrDefault();
        if (childPara?.Inline != null)
            para.Inlines.AddRange(RenderInlines(childPara.Inline));

        li.Blocks.Add(para);

        foreach (MdBlock sub in listItem)
        {
            if (sub is ListBlock subList)
            {
                var nestedList = new System.Windows.Documents.List
                {
                    Margin = new Thickness(16, 0, 0, 0),
                    MarkerStyle = depth == 0
                        ? TextMarkerStyle.Circle
                        : TextMarkerStyle.Box
                };

                foreach (MdBlock subItem in subList)
                {
                    if (subItem is ListItemBlock subListItem)
                        RenderListItem(nestedList, subListItem, depth + 1);
                }

                li.Blocks.Add(nestedList);
            }
            else if (sub is ParagraphBlock subParaBlock && subParaBlock != childPara)
            {
                var subPara = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                subPara.Inlines.AddRange(RenderInlines(subParaBlock.Inline));
                li.Blocks.Add(subPara);
            }
        }

        parentList.ListItems.Add(li);
    }

    private static void RenderQuote(FlowDocument doc, QuoteBlock quoteBlock)
    {
        var quotePara = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 160, 224)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 4, 0, 4),
            Background = new SolidColorBrush(Color.FromRgb(242, 247, 251))
        };

        foreach (MdBlock child in quoteBlock)
        {
            if (child is ParagraphBlock p)
                quotePara.Inlines.AddRange(RenderInlines(p.Inline));
        }

        doc.Blocks.Add(quotePara);
    }

    private static void RenderHorizontalRule(FlowDocument doc)
    {
        doc.Blocks.Add(new BlockUIContainer(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 230, 242)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 8, 0, 8)
        }));
    }

    private static List<System.Windows.Documents.Inline> RenderInlines(MdContainerInline? container)
    {
        var inlines = new List<System.Windows.Documents.Inline>();
        if (container == null) return inlines;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    inlines.Add(new Run(Decode(literal.Content.ToString() ?? "")));
                    break;

                case CodeInline code:
                    inlines.Add(new Run(code.Content)
                    {
                        FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                        FontSize = 13,
                        Background = new SolidColorBrush(Color.FromRgb(242, 247, 251)),
                        Foreground = new SolidColorBrush(Color.FromRgb(192, 64, 64))
                    });
                    break;

                case EmphasisInline emphasis:
                    var children = RenderInlines(emphasis);
                    // DelimiterCount == 2 means Bold (e.g. **text**)
                    if (emphasis.DelimiterCount == 2)
                    {
                        foreach (var b in children)
                            b.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        foreach (var i in children)
                            i.FontStyle = FontStyles.Italic;
                    }
                    inlines.AddRange(children);
                    break;

                case LinkInline link:
                    var linkRun = new Run(link.Title ?? link.Url ?? "")
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(56, 160, 224)),
                        TextDecorations = TextDecorations.Underline
                    };
                    if (!string.IsNullOrEmpty(link.Url))
                    {
                        linkRun.ToolTip = link.Url;
                        linkRun.MouseEnter += (_, _) => linkRun.Cursor = Cursors.Hand;
                    }
                    inlines.Add(linkRun);
                    break;

                case LineBreakInline _:
                    inlines.Add(new LineBreak());
                    break;

                default:
                    if (inline is MdContainerInline ci)
                        inlines.AddRange(RenderInlines(ci));
                    break;
            }
        }

        return inlines;
    }

    private static string Decode(string text)
    {
        return System.Net.WebUtility.HtmlDecode(text);
    }
}
