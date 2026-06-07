using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DeepSeekCode.Diff;

public enum DiffLineType { Unchanged, Added, Removed }

public record DiffLine(DiffLineType Type, string Text);

public static class DiffRenderer
{
    private const int MaxContextLines = 8;
    private const int MaxTotalLines = 60;

    /// <summary>
    /// 计算两段文本的行级差异
    /// </summary>
    public static List<DiffLine> ComputeDiff(string oldText, string newText)
    {
        var oldLines = oldText.Replace("\r\n", "\n").Split('\n');
        var newLines = newText.Replace("\r\n", "\n").Split('\n');

        var lcs = ComputeLcs(oldLines, newLines);
        var result = new List<DiffLine>();
        int oi = 0, ni = 0, li = 0;

        while (li < lcs.Count)
        {
            while (oi < oldLines.Length && oldLines[oi] != lcs[li])
            {
                result.Add(new DiffLine(DiffLineType.Removed, oldLines[oi]));
                oi++;
            }
            while (ni < newLines.Length && newLines[ni] != lcs[li])
            {
                result.Add(new DiffLine(DiffLineType.Added, newLines[ni]));
                ni++;
            }
            result.Add(new DiffLine(DiffLineType.Unchanged, lcs[li]));
            oi++; ni++; li++;
        }

        while (oi < oldLines.Length)
            result.Add(new DiffLine(DiffLineType.Removed, oldLines[oi++]));
        while (ni < newLines.Length)
            result.Add(new DiffLine(DiffLineType.Added, newLines[ni++]));

        return result;
    }

    private static List<string> ComputeLcs(string[] a, string[] b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 1; i <= m; i++)
            for (var j = 1; j <= n; j++)
                dp[i, j] = a[i - 1] == b[j - 1]
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        var result = new List<string>();
        var x = m;
        var y = n;
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1])
            {
                result.Add(a[x - 1]);
                x--; y--;
            }
            else if (dp[x - 1, y] > dp[x, y - 1])
                x--;
            else
                y--;
        }
        result.Reverse();
        return result;
    }

    /// <summary>
    /// 精简 diff 结果：只保留有变更的区域 + 少量上下文
    /// </summary>
    private static List<DiffLine> TrimContext(List<DiffLine> diffs)
    {
        var result = new List<DiffLine>();
        var changeIndexes = new HashSet<int>();
        for (var i = 0; i < diffs.Count; i++)
        {
            if (diffs[i].Type != DiffLineType.Unchanged)
                changeIndexes.Add(i);
        }

        if (changeIndexes.Count == 0 && diffs.Count > 10)
            return diffs.Take(10).ToList();

        var shown = new HashSet<int>();
        foreach (var ci in changeIndexes)
        {
            var start = Math.Max(0, ci - MaxContextLines);
            var end = Math.Min(diffs.Count - 1, ci + MaxContextLines);
            for (var i = start; i <= end; i++)
                shown.Add(i);
        }

        // 添加被截断的标记
        var lastShown = -2;
        foreach (var i in shown.OrderBy(i => i))
        {
            if (i > lastShown + 1 && result.Count > 0)
                result.Add(new DiffLine(DiffLineType.Unchanged, "..."));
            result.Add(diffs[i]);
            lastShown = i;
        }

        if (result.Count > MaxTotalLines)
            result = result.Take(MaxTotalLines).ToList();

        return result;
    }

    /// <summary>
    /// 将 diff 渲染为 WPF Section，可直接插入 FlowDocument
    /// </summary>
    public static Section RenderDiff(string oldText, string newText, string fileName)
    {
        var diffs = ComputeDiff(oldText, newText);
        diffs = TrimContext(diffs);

        var section = new Section
        {
            Margin = new Thickness(0, 8, 0, 4)
        };

        // 统计变更行数
        var added = diffs.Count(d => d.Type == DiffLineType.Added);
        var removed = diffs.Count(d => d.Type == DiffLineType.Removed);

        // 标题行
        var headerPara = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 4)
        };
        headerPara.Inlines.Add(new Run("Diff: ")
        {
            Foreground = new SolidColorBrush(Color.FromRgb(26, 42, 56)),
            FontSize = 13,
            FontWeight = FontWeights.Bold
        });
        headerPara.Inlines.Add(new Run(fileName)
        {
            Foreground = new SolidColorBrush(Color.FromRgb(56, 160, 224)),
            FontSize = 13,
            FontWeight = FontWeights.Bold
        });
        if (added > 0 || removed > 0)
        {
            headerPara.Inlines.Add(new Run("  ")
            {
                FontSize = 11
            });
            if (added > 0)
                headerPara.Inlines.Add(new Run($"+{added}")
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(58, 160, 88)),
                    FontSize = 11
                });
            if (added > 0 && removed > 0)
                headerPara.Inlines.Add(new Run("  ")
                {
                    FontSize = 11
                });
            if (removed > 0)
                headerPara.Inlines.Add(new Run($"-{removed}")
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(192, 64, 64)),
                    FontSize = 11
                });
        }
        section.Blocks.Add(headerPara);

        // Diff 行
        foreach (var diff in diffs)
        {
            var prefix = diff.Type switch
            {
                DiffLineType.Added => "+ ",
                DiffLineType.Removed => "- ",
                _ => "  "
            };

            var fgColor = diff.Type switch
            {
                DiffLineType.Added => Color.FromRgb(42, 120, 50),
                DiffLineType.Removed => Color.FromRgb(180, 40, 40),
                _ => Color.FromRgb(120, 140, 160)
            };

            var bgColor = diff.Type switch
            {
                DiffLineType.Added => Color.FromRgb(232, 248, 232),
                DiffLineType.Removed => Color.FromRgb(248, 232, 232),
                _ => diff.Text == "..." ? Color.FromRgb(248, 248, 248) : Color.FromRgb(255, 255, 255)
            };

            section.Blocks.Add(new Paragraph(new Run($"{prefix}{diff.Text}"))
            {
                FontSize = 11,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                Foreground = new SolidColorBrush(fgColor),
                Background = new SolidColorBrush(bgColor),
                Margin = new Thickness(0),
                Padding = new Thickness(8, 1, 8, 1),
                LineHeight = 1.3
            });
        }

        return section;
    }
}
