using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using DeepSeekCode.Models;
using DeepSeekCode.Services;
using DeepSeekCode.UI;

namespace DeepSeekCode;

/// <summary>
/// 左侧边栏：任务面板 + 子代理面板 + 进度指示器
/// </summary>
public partial class MainWindow
{
    // ═══════════════════════════════════════════
    //  Todo 面板（渲染到侧边栏）
    // ═══════════════════════════════════════════

    private List<TodoItem> _currentTodos = new();

    private void UpdateTodoPanel(List<TodoItem> todos)
    {
        _currentTodos = todos;

        if (todos.Count == 0)
        {
            SidebarTasksControl.Visibility = Visibility.Collapsed;
            TasksEmptyHint.Visibility = Visibility.Visible;
            SidebarTasksHeader.Text = "📋 任务";
            return;
        }

        var total = todos.Count;
        var completed = todos.Count(t => t.Status == TodoStatus.Completed);
        var inProgress = todos.Count(t => t.Status == TodoStatus.InProgress);
        var allDone = completed == total;

        TasksEmptyHint.Visibility = Visibility.Collapsed;
        SidebarTasksControl.Visibility = Visibility.Visible;

        if (allDone)
            SidebarTasksHeader.Text = $"📋 任务 ✓ ({completed}/{total})";
        else if (inProgress > 0)
            SidebarTasksHeader.Text = $"📋 任务 ⏳ ({completed}/{total})";
        else
            SidebarTasksHeader.Text = $"📋 任务 ({completed}/{total})";

        SidebarTasksControl.Items.Clear();
        foreach (var todo in todos)
        {
            var icon = todo.Status switch
            {
                TodoStatus.Completed => "✔",
                TodoStatus.InProgress => "⏳",
                TodoStatus.Cancelled => "✘",
                _ => "○"
            };

            var fgColor = todo.Status switch
            {
                TodoStatus.Completed => (Brush)Application.Current.Resources["SuccessGreenBrush"],
                TodoStatus.InProgress => (Brush)Application.Current.Resources["RunningBlueBrush"],
                TodoStatus.Cancelled => (Brush)Application.Current.Resources["PrimaryLightBrush"],
                _ => (Brush)Application.Current.Resources["PrimaryMediumBrush"]
            };

            var bgColor = todo.Status switch
            {
                TodoStatus.InProgress => (Brush)Application.Current.Resources["RunningBgBrush"],
                _ => Brushes.Transparent
            };

            var borderColor = todo.Status switch
            {
                TodoStatus.Completed => (Brush)Application.Current.Resources["SuccessGreenBrush"],
                TodoStatus.InProgress => (Brush)Application.Current.Resources["RunningBorderBrush"],
                _ => (Brush)Application.Current.Resources["BorderCardBrush"]
            };

            var priorityMark = todo.Priority switch
            {
                TodoPriority.High => " ⚡",
                _ => ""
            };

            var textDeco = todo.Status == TodoStatus.Cancelled
                ? TextDecorations.Strikethrough
                : null;

            var itemBorder = new Border
            {
                Background = bgColor,
                BorderBrush = borderColor,
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(6, 3, 4, 3)
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };

            var iconRun = new TextBlock
            {
                Text = icon,
                Foreground = fgColor,
                FontSize = 10,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(iconRun);

            var textRun = new TextBlock
            {
                Text = $"{todo.Content}{priorityMark}",
                Foreground = fgColor,
                FontSize = 10,
                TextDecorations = textDeco,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            row.Children.Add(textRun);

            itemBorder.Child = row;

            // 运行中的任务添加脉冲动画
            if (todo.Status == TodoStatus.InProgress)
            {
                AttachPulseAnimation(itemBorder);
            }

            SidebarTasksControl.Items.Add(itemBorder);
        }
    }

    // ═══════════════════════════════════════════
    //  子代理状态面板（渲染到侧边栏）
    // ═══════════════════════════════════════════

    private record SubagentInfo(string TaskId, string Description, string Type,
        DateTime StartTime, bool Completed, string? Summary);

    private readonly List<SubagentInfo> _subagentList = new();
    private bool _subagentNeedsRebuild;

    private void OnSubagentStarted(SubagentStartedEvent e)
    {
        _subagentList.Add(new SubagentInfo(e.TaskId, e.Description, e.Type,
            DateTime.Now, false, null));
        _subagentNeedsRebuild = true;
        RenderSubagentPanel();
    }

    private void OnSubagentCompleted(SubagentCompletedEvent e)
    {
        var existing = _subagentList.Find(s => s.TaskId == e.TaskId);
        if (existing != null)
        {
            var idx = _subagentList.IndexOf(existing);
            _subagentList[idx] = existing with { Completed = true, Summary = e.Summary };
        }
        _subagentNeedsRebuild = true;
        RenderSubagentPanel();
    }

    private void RenderSubagentPanel()
    {
        // 60 秒后自动移除已完成项
        var removed = _subagentList.RemoveAll(s => s.Completed && (DateTime.Now - s.StartTime).TotalSeconds > 60);
        if (removed > 0) _subagentNeedsRebuild = true;

        if (_subagentList.Count == 0)
        {
            SidebarSubagentControl.Visibility = Visibility.Collapsed;
            SubagentsEmptyHint.Visibility = Visibility.Visible;
            SidebarSubagentsHeader.Text = "🤖 子代理";
            _subagentNeedsRebuild = false;
            return;
        }

        SubagentsEmptyHint.Visibility = Visibility.Collapsed;
        SidebarSubagentControl.Visibility = Visibility.Visible;

        var runningCount = _subagentList.Count(s => !s.Completed);

        // 未完成项：增量更新 spinner + 耗时（不重建 UI）
        if (!_subagentNeedsRebuild)
        {
            if (_subagentList.All(s => s.Completed)) return;

            for (var i = 0; i < _subagentList.Count; i++)
            {
                var info = _subagentList[i];
                if (info.Completed) continue;

                var elapsed = (DateTime.Now - info.StartTime).TotalSeconds;
                var spinFrame = SpinnerFrames[_spinnerIndex % SpinnerFrames.Length];

                if (SidebarSubagentControl.Items[i] is Border itemBorder && itemBorder.Child is StackPanel row)
                {
                    UpdateRunningSubagentRow(row, info, spinFrame, elapsed);
                }
            }
            UpdateSubagentHeader(runningCount);
            return;
        }

        // 全量重建
        var allSubagentDone = _subagentList.All(s => s.Completed);
        UpdateSubagentHeader(runningCount, allSubagentDone);

        SidebarSubagentControl.Items.Clear();

        foreach (var info in _subagentList)
        {
            var elapsed = (DateTime.Now - info.StartTime).TotalSeconds;

            var itemBorder = new Border
            {
                Background = info.Completed ? Brushes.Transparent : (Brush)Application.Current.Resources["RunningBgBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderCardBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 4, 6, 4)
            };

            var outerStack = new StackPanel();

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0)
            };

            if (info.Completed)
            {
                var failed = info.Summary != null && info.Summary.Contains("失败");
                var icon = failed ? "✘" : "✔";
                var fg = failed
                    ? (Brush)Application.Current.Resources["ErrorRedBrush"]
                    : (Brush)Application.Current.Resources["SuccessGreenBrush"];

                row.Children.Add(new TextBlock
                {
                    Text = icon,
                    Foreground = fg,
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var typeBadge = new TextBlock
                {
                    Text = $"[{info.Type}]",
                    Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                row.Children.Add(typeBadge);

                row.Children.Add(new TextBlock
                {
                    Text = info.Description,
                    Foreground = fg,
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });

                outerStack.Children.Add(row);

                if (info.Summary != null)
                {
                    outerStack.Children.Add(new TextBlock
                    {
                        Text = info.Summary.Length > 80 ? info.Summary[..80] + "..." : info.Summary,
                        Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
                        FontSize = 9,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(14, 2, 0, 0)
                    });
                }
            }
            else
            {
                var spinFrame = SpinnerFrames[(int)(elapsed / 0.12) % SpinnerFrames.Length];

                var spinnerTb = new TextBlock
                {
                    Text = spinFrame,
                    Foreground = (Brush)Application.Current.Resources["RunningBlueBrush"],
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                row.Children.Add(spinnerTb);

                var typeBadge = new TextBlock
                {
                    Text = $"[{info.Type}]",
                    Foreground = (Brush)Application.Current.Resources["RunningBlueBrush"],
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                row.Children.Add(typeBadge);

                row.Children.Add(new TextBlock
                {
                    Text = info.Description,
                    Foreground = (Brush)Application.Current.Resources["RunningBlueBrush"],
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var elapsedTb = new TextBlock
                {
                    Text = $"{elapsed:F1}s",
                    Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                row.Children.Add(elapsedTb);

                outerStack.Children.Add(row);

                // 运行中子代理：不定进度条动画
                var progressBar = new ProgressBar
                {
                    IsIndeterminate = true,
                    Height = 2,
                    Foreground = (Brush)Application.Current.Resources["RunningBlueBrush"],
                    Background = (Brush)Application.Current.Resources["ProgressBgBrush"],
                    Margin = new Thickness(14, 3, 4, 0)
                };
                outerStack.Children.Add(progressBar);
            }

            itemBorder.Child = outerStack;
            SidebarSubagentControl.Items.Add(itemBorder);
        }
        _subagentNeedsRebuild = false;
    }

    private void UpdateRunningSubagentRow(StackPanel row, SubagentInfo info,
        string spinFrame, double elapsed)
    {
        if (row.Children.Count >= 4)
        {
            if (row.Children[0] is TextBlock spinnerTb)
                spinnerTb.Text = spinFrame;
            if (row.Children[3] is TextBlock elapsedTb)
                elapsedTb.Text = $"{elapsed:F1}s";
        }
    }

    private void UpdateSubagentHeader(int runningCount, bool? allDone = null)
    {
        if (allDone == true)
            SidebarSubagentsHeader.Text = "🤖 子代理 ✓ 全部完成";
        else if (runningCount > 0)
            SidebarSubagentsHeader.Text = $"🤖 子代理 ⏳ ({runningCount})";
        else
            SidebarSubagentsHeader.Text = "🤖 子代理";
    }

    // ═══════════════════════════════════════════
    //  动画辅助
    // ═══════════════════════════════════════════

    private static void AttachPulseAnimation(Border target)
    {
        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.75,
            Duration = TimeSpan.FromMilliseconds(800),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        target.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    // ═══════════════════════════════════════════
    //  进度指示器 (Spinner)
    // ═══════════════════════════════════════════

    private DispatcherTimer? _spinnerTimer;
    private int _spinnerIndex;
    private static readonly string[] SpinnerFrames = ["⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷"];
    private string _toolProgressText = "";

    private void EnsureSpinnerTimer()
    {
        if (_spinnerTimer != null) return;
        _spinnerTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Normal,
            (_, _) => SpinnerTimer_Tick(null, EventArgs.Empty),
            Dispatcher.CurrentDispatcher);
    }

    private void StartStatusSpinner(string text)
    {
        EnsureSpinnerTimer();
        _toolProgressText = text;
        _spinnerIndex = 0;
        ThinkingBar.Visibility = Visibility.Visible;
        ThinkingSpinner.Text = SpinnerFrames[0];
        ThinkingIntentLabel.Text = _toolProgressText;
        _spinnerTimer!.Start();
    }

    private void UpdateSpinnerText(string text)
    {
        _toolProgressText = text;
        ThinkingIntentLabel.Text = text;
    }

    private void StopStatusSpinner()
    {
        _spinnerTimer?.Stop();
        ThinkingBar.Visibility = Visibility.Collapsed;
    }

    private static string ExtractIntent(string thinkingBuffer)
    {
        if (string.IsNullOrWhiteSpace(thinkingBuffer))
            return "AI 正在思考…";

        var tail = thinkingBuffer.Length > 300
            ? thinkingBuffer[^300..]
            : thinkingBuffer;

        var sentences = System.Text.RegularExpressions.Regex.Split(tail, @"(?<=[.\n])");
        for (var i = sentences.Length - 1; i >= 0; i--)
        {
            var s = sentences[i].Trim();
            if (s.Length < 5) continue;

            if (s.Length > 60) s = s[..60] + "…";

            if (System.Text.RegularExpressions.Regex.IsMatch(s,
                @"\b(Let me|I['']ll|I need to|I should|Now I|Next I|First I|I will|我要|我先|我现在|接下来)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return s;
            }
        }

        return "AI 正在思考…";
    }

    private void SpinnerTimer_Tick(object? sender, EventArgs e)
    {
        _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
        ThinkingSpinner.Text = SpinnerFrames[_spinnerIndex];

        if (_thinkingActive && !string.IsNullOrEmpty(_thinkingBuffer))
        {
            ThinkingIntentLabel.Text = ExtractIntent(_thinkingBuffer);
        }
        else
        {
            ThinkingIntentLabel.Text = _toolProgressText;
        }

        StatusTimingLabel.Text = _timingService.GetRoundSummary();

        if (_thinkingActive && !string.IsNullOrEmpty(_thinkingBuffer))
        {
            _ = _chatRenderer.UpdateThinkingCard(_thinkingBuffer);
        }

        foreach (var toolId in _activeToolCards)
        {
            var elapsed = _timingService.GetElapsed(toolId);
            _ = _chatRenderer.UpdateToolCardElapsed(toolId, TimingService.FormatElapsed(elapsed));
        }

        UpdatePlanIndicator();

        if (_subagentList.Any(s => !s.Completed))
            RenderSubagentPanel();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _currentCancellation?.Cancel();
    }
}
