using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;

using DeepSeekCode.Commands;
using DeepSeekCode.Models;
using DeepSeekCode.Services;
using DeepSeekCode.Session;
using DeepSeekCode.UI;

namespace DeepSeekCode;

/// <summary>
/// 命令自动补全 + 历史会话 + @文件引用 + 辅助 UI 操作
/// </summary>
public partial class MainWindow
{
    // ═══════════════════════════════════════════
    //  命令自动补全
    // ═══════════════════════════════════════════

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = InputBox.Text;
        var caretIndex = InputBox.CaretIndex;

        // ── / 命令补全 ──
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('/') && !trimmed.Contains(' ') && text.TrimStart() == trimmed)
        {
            var allCommands = _commandRegistry.GetAll();
            var matching = allCommands
                .Where(c => c.Name.StartsWith(trimmed[1..], StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count > 0)
            {
                FilePopup.IsOpen = false;
                CommandListBox.ItemsSource = matching;
                CommandListBox.DisplayMemberPath = null;
                CommandListBox.ItemTemplate = CreateCommandItemTemplate();
                CommandListBox.SelectedIndex = 0;
                CommandPopup.IsOpen = true;
                return;
            }
        }
        CommandPopup.IsOpen = false;

        // ── @ 文件引用补全 ──
        var atWord = GetAtWordAtCursor(text, caretIndex);
        if (atWord != null)
        {
            var partial = atWord.Length > 1 ? atWord[1..] : ""; // 去掉 @
            DebounceFileSearch(partial);
            return;
        }
        FilePopup.IsOpen = false;
    }

    private DataTemplate CreateCommandItemTemplate()
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        var nameBlock = new FrameworkElementFactory(typeof(TextBlock));
        nameBlock.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding("Name") { StringFormat = "/{0}" });
        nameBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        nameBlock.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(86, 156, 214)));
        factory.AppendChild(nameBlock);

        var descBlock = new FrameworkElementFactory(typeof(TextBlock));
        descBlock.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding("Description"));
        descBlock.SetValue(TextBlock.FontSizeProperty, 12.0);
        descBlock.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(150, 150, 150)));
        descBlock.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
        factory.AppendChild(descBlock);

        template.VisualTree = factory;
        return template;
    }

    private void CommandListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            e.Handled = true;
            SelectCommandFromList();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CommandPopup.IsOpen = false;
            InputBox.Focus();
        }
    }

    private void CommandListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectCommandFromList();
    }

    private void SelectCommandFromList()
    {
        if (CommandListBox.SelectedItem is ISlashCommand cmd)
        {
            InputBox.Text = $"/{cmd.Name} ";
            InputBox.CaretIndex = InputBox.Text.Length;
        }
        CommandPopup.IsOpen = false;
        InputBox.Focus();
    }

    // ═══════════════════════════════════════════
    //  @ 文件引用补全
    // ═══════════════════════════════════════════

    /// <summary>文件建议项</summary>
    private record FileSuggestion(
        string RelativePath,
        string FileName,
        string Icon,
        DateTime LastModified,
        bool IsDirectory
    );

    private CancellationTokenSource? _fileSearchCts;
    private string _lastFileSearchPartial = "";

    /// <summary>从输入文本中提取光标位置的 @word（如 @Main 返回 "@Main"）</summary>
    private static string? GetAtWordAtCursor(string text, int cursorPos)
    {
        // 向前查找最近的 @ 符号（必须在光标之前或光标处）
        var searchEnd = Math.Min(cursorPos, text.Length);
        var atIndex = -1;
        for (var i = searchEnd - 1; i >= 0; i--)
        {
            if (text[i] == '@')
            {
                // @ 必须在词边界（行首或前面是空白/标点，但不能是路径分隔符）
                if (i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] == '(' || text[i - 1] == '[' || text[i - 1] == '"' || text[i - 1] == '\'')
                {
                    atIndex = i;
                    break;
                }
            }
            // 遇到空白或行首就停止（@ 必须在当前词内）
            if (char.IsWhiteSpace(text[i]))
                break;
        }
        if (atIndex < 0) return null;

        // 向后找词尾（空白或行尾）
        var endIndex = atIndex + 1;
        while (endIndex < text.Length && !char.IsWhiteSpace(text[endIndex]))
            endIndex++;

        return text[atIndex..endIndex];
    }

    /// <summary>防抖搜索（150ms 延迟，快速输入时不频繁搜索）</summary>
    private async void DebounceFileSearch(string partial)
    {
        _fileSearchCts?.Cancel();
        _fileSearchCts = new CancellationTokenSource();
        var ct = _fileSearchCts.Token;
        _lastFileSearchPartial = partial;

        try
        {
            await Task.Delay(150, ct);
            if (ct.IsCancellationRequested) return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var results = await Task.Run(() => SearchWorkspaceFiles(partial, ct), ct);

        if (ct.IsCancellationRequested) return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (_lastFileSearchPartial != partial) return; // 用户已继续输入

            if (results.Count == 0)
            {
                FilePopup.IsOpen = false;
                return;
            }

            FileListBox.ItemsSource = results;
            FileListBox.DisplayMemberPath = null;
            FileListBox.ItemTemplate = CreateFileItemTemplate();
            FileListBox.SelectedIndex = 0;
            FilePopup.IsOpen = true;
        });
    }

    /// <summary>在工作区中搜索匹配文件</summary>
    private List<FileSuggestion> SearchWorkspaceFiles(string partial, CancellationToken ct)
    {
        var results = new List<FileSuggestion>();
        var wsPath = _workspaceService.WorkspacePath;
        if (!Directory.Exists(wsPath)) return results;

        // 需要排除的目录名（不区分大小写）
        var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".deepseek-code", "node_modules",
            "bin", "obj", "packages", "dist", "build", "coverage",
            ".next", ".nuget", ".idea", ".vscode"
        };

        try
        {
            var enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                MaxRecursionDepth = 15
            };

            var isPrefixSearch = !string.IsNullOrEmpty(partial);
            var lowerPartial = partial.ToLowerInvariant();

            // 收集匹配的文件和目录
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                wsPath, "*", enumOptions))
            {
                if (ct.IsCancellationRequested) break;
                if (results.Count >= 25) break;

                // 跳过排除目录
                var relativePath = Path.GetRelativePath(wsPath, entry);
                var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Any(p => excludedDirs.Contains(p))) continue;

                // 跳过隐藏文件/目录（.开头）
                if (parts.Any(p => p.StartsWith('.'))) continue;

                var attr = File.GetAttributes(entry);
                var isDir = (attr & FileAttributes.Directory) != 0;
                var fileName = Path.GetFileName(entry);

                // 匹配检查
                if (isPrefixSearch && !fileName.ToLowerInvariant().Contains(lowerPartial))
                    continue;

                var lastModified = isDir ? DateTime.MinValue : File.GetLastWriteTime(entry);

                results.Add(new FileSuggestion(
                    RelativePath: relativePath,
                    FileName: fileName,
                    Icon: isDir ? "📁" : GetFileIcon(fileName),
                    LastModified: lastModified,
                    IsDirectory: isDir
                ));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* 搜索失败静默返回空列表 */ }

        // 排序：精确文件名匹配 > 前缀匹配 > 包含匹配 > 最近修改
        return results
            .OrderByDescending(s =>
            {
                if (!string.IsNullOrEmpty(partial))
                {
                    var fn = s.FileName.ToLowerInvariant();
                    var p = partial.ToLowerInvariant();
                    if (fn == p) return 1000;
                    if (fn.StartsWith(p)) return 500;
                    if (fn.Contains(p)) return 100;
                }
                return 0;
            })
            .ThenByDescending(s => s.LastModified)
            .Take(20)
            .ToList();
    }

    /// <summary>根据文件扩展名返回图标</summary>
    private static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "🟦",
            ".xaml" => "🟪",
            ".csproj" or ".sln" => "🟣",
            ".md" => "📝",
            ".json" or ".xml" or ".yaml" or ".yml" => "📋",
            ".js" or ".ts" or ".jsx" or ".tsx" => "🟨",
            ".css" or ".scss" or ".less" => "🎨",
            ".html" or ".htm" => "🌐",
            ".py" => "🐍",
            ".go" => "🔵",
            ".rs" => "🦀",
            ".java" => "☕",
            ".sh" or ".ps1" or ".bat" or ".cmd" => "⚡",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" => "🖼",
            _ => "📄"
        };
    }

    /// <summary>创建文件列表项模板</summary>
    private DataTemplate CreateFileItemTemplate()
    {
        var template = new DataTemplate();

        var outerStack = new FrameworkElementFactory(typeof(StackPanel));
        outerStack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        // 第一行: 图标 + 文件名
        var row1 = new FrameworkElementFactory(typeof(StackPanel));
        row1.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var iconBlock = new FrameworkElementFactory(typeof(TextBlock));
        iconBlock.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding("Icon"));
        iconBlock.SetValue(TextBlock.FontSizeProperty, 13.0);
        iconBlock.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
        iconBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        row1.AppendChild(iconBlock);

        var nameBlock = new FrameworkElementFactory(typeof(TextBlock));
        nameBlock.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding("FileName"));
        nameBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        nameBlock.SetValue(TextBlock.FontSizeProperty, 13.0);
        nameBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        nameBlock.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        row1.AppendChild(nameBlock);

        outerStack.AppendChild(row1);

        // 第二行: 相对路径（仅当不是根级文件时显示）
        var pathBlock = new FrameworkElementFactory(typeof(TextBlock));
        pathBlock.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding("RelativePath"));
        pathBlock.SetValue(TextBlock.FontSizeProperty, 10.0);
        pathBlock.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(136, 168, 192)));
        pathBlock.SetValue(TextBlock.MarginProperty, new Thickness(19, 1, 0, 0));
        pathBlock.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        outerStack.AppendChild(pathBlock);

        template.VisualTree = outerStack;
        return template;
    }

    /// <summary>选中文件列表中的项</summary>
    private void SelectFileFromList()
    {
        if (FileListBox.SelectedItem is not FileSuggestion suggestion) return;

        var text = InputBox.Text;
        var caretIndex = InputBox.CaretIndex;
        var atWord = GetAtWordAtCursor(text, caretIndex);
        if (atWord == null) return;

        // 在原文中找到 @word 的位置
        var atIndex = text.LastIndexOf(atWord, Math.Min(caretIndex, text.Length),
            StringComparison.Ordinal);
        if (atIndex < 0)
        {
            // 降级：在整个文本中搜索
            atIndex = text.IndexOf(atWord, StringComparison.Ordinal);
        }
        if (atIndex < 0) return;

        // 用相对路径替换 @word，路径含空格时加引号
        var replacement = suggestion.RelativePath.Contains(' ')
            ? $"\"{suggestion.RelativePath}\""
            : suggestion.RelativePath;

        InputBox.Text = text[..atIndex] + replacement + text[(atIndex + atWord.Length)..];
        InputBox.CaretIndex = atIndex + replacement.Length;
        FilePopup.IsOpen = false;
        InputBox.Focus();
    }

    private void FileListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            e.Handled = true;
            SelectFileFromList();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            FilePopup.IsOpen = false;
            InputBox.Focus();
        }
    }

    private void FileListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectFileFromList();
    }

    // ═══════════════════════════════════════════
    //  历史会话
    // ═══════════════════════════════════════════

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPopup.IsOpen)
        {
            HistoryPopup.IsOpen = false;
            return;
        }
        LoadHistorySessions();
        HistoryPopup.IsOpen = true;
    }

    private async void LoadHistorySessions()
    {
        var sessions = await _sessionStore.ListSessionsAsync();
        HistoryListBox.ItemsSource = null;

        if (sessions.Count == 0)
        {
            HistoryListBox.Visibility = Visibility.Collapsed;
            HistoryEmptyLabel.Visibility = Visibility.Visible;
            return;
        }

        HistoryListBox.Visibility = Visibility.Visible;
        HistoryEmptyLabel.Visibility = Visibility.Collapsed;

        HistoryListBox.ItemsSource = sessions;
        HistoryListBox.DisplayMemberPath = null;
        HistoryListBox.ItemTemplate = CreateHistoryItemTemplate();
        HistoryListBox.SelectedIndex = 0;
        HistoryListBox.Focus();
    }

    private DataTemplate CreateHistoryItemTemplate()
    {
        var template = new DataTemplate();

        var outerStack = new FrameworkElementFactory(typeof(StackPanel));
        outerStack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        var row1 = new FrameworkElementFactory(typeof(DockPanel));

        var titleBlock = new FrameworkElementFactory(typeof(TextBlock));
        titleBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
        titleBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        titleBlock.SetValue(TextBlock.FontSizeProperty, 13.0);
        titleBlock.SetValue(DockPanel.DockProperty, Dock.Left);
        row1.AppendChild(titleBlock);

        var timeBlock = new FrameworkElementFactory(typeof(TextBlock));
        timeBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("UpdatedAt")
        {
            StringFormat = "MM-dd HH:mm"
        });
        timeBlock.SetValue(TextBlock.FontSizeProperty, 10.0);
        timeBlock.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(136, 168, 192)));
        timeBlock.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
        timeBlock.SetValue(DockPanel.DockProperty, Dock.Right);
        row1.AppendChild(timeBlock);

        outerStack.AppendChild(row1);

        var row2 = new FrameworkElementFactory(typeof(TextBlock));
        var multiBinding = new System.Windows.Data.MultiBinding();
        multiBinding.StringFormat = "{0} 条消息 · {1}";
        multiBinding.Bindings.Add(new System.Windows.Data.Binding("MessageCount"));
        multiBinding.Bindings.Add(new System.Windows.Data.Binding("Model"));
        row2.SetBinding(TextBlock.TextProperty, multiBinding);
        row2.SetValue(TextBlock.FontSizeProperty, 10.0);
        row2.SetValue(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(136, 168, 192)));
        row2.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
        outerStack.AppendChild(row2);

        template.VisualTree = outerStack;
        return template;
    }

    private void HistoryListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            LoadSessionFromHistory();
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            DeleteSelectedSession();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HistoryPopup.IsOpen = false;
        }
    }

    private void HistoryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        LoadSessionFromHistory();
    }

    private async void LoadSessionFromHistory()
    {
        if (HistoryListBox.SelectedItem is not SessionMetadata meta) return;

        var data = await _sessionStore.LoadAsync(meta.Id);
        if (data == null) return;

        _conversation.DeserializeSession(data.MessagesJson);
        await _chatRenderer.ClearChat();
        _permissionManager.ResetSession();
        _hasUnsavedChanges = false;
        _commandContext.CurrentSessionId = meta.Id;

        foreach (var msg in _conversation.Messages)
        {
            switch (msg.Role)
            {
                case "system":
                    break;

                case "user" when msg.Content != null:
                    await _chatRenderer.AppendUserMessage(msg.Content);
                    break;

                case "assistant":
                    if (!string.IsNullOrWhiteSpace(msg.ReasoningContent))
                    {
                        await _chatRenderer.AppendThinkingCard(msg.ReasoningContent);
                        await _chatRenderer.CollapseThinkingCard();
                    }

                    if (!string.IsNullOrWhiteSpace(msg.Content))
                        await _chatRenderer.AppendAiContent(msg.Content);

                    if (msg.ToolCalls != null)
                    {
                        foreach (var tc in msg.ToolCalls)
                        {
                            var argSummary = tc.Function.Arguments.Length > 80
                                ? tc.Function.Arguments[..80] + "..."
                                : tc.Function.Arguments;
                            await _chatRenderer.AppendToolCard(tc.Id, tc.Function.Name, argSummary);
                        }
                    }
                    break;

                case "tool" when msg.ToolCallId != null:
                    var resultText = msg.Content ?? "";
                    var resultPreview = resultText.Length > 200
                        ? resultText[..200] + "..."
                        : resultText;
                    await _chatRenderer.UpdateToolCardComplete(msg.ToolCallId, true, "", resultPreview);
                    break;
            }
        }

        HistoryPopup.IsOpen = false;
        AppendSystemMessage($"已加载历史会话: {meta.Title} ({meta.MessageCount} 条消息)");
    }

    private void NewSessionButton_Click(object sender, RoutedEventArgs e)
    {
        _conversation.ClearConversation();
        _permissionManager.ResetSession();
        _ = _chatRenderer.ClearChat();
        _hasUnsavedChanges = false;
        _commandContext.CurrentSessionId = null;
        HistoryPopup.IsOpen = false;
        AppendSystemMessage("已创建新对话");
    }

    private void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedSession();
    }

    private async void DeleteSelectedSession()
    {
        if (HistoryListBox.SelectedItem is not SessionMetadata meta) return;

        var result = MessageBox.Show(
            $"确认删除会话 \"{meta.Title}\"？\n此操作不可撤销。",
            "DeepSeek Code",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        await _sessionStore.DeleteAsync(meta.Id);

        if (_commandContext.CurrentSessionId == meta.Id)
            _commandContext.CurrentSessionId = null;

        LoadHistorySessions();
    }

    // ═══════════════════════════════════════════
    //  辅助 UI 操作
    // ═══════════════════════════════════════════

    private void ClearScreen()
    {
        _conversation.ClearConversation();
        _ = _chatRenderer.ClearChat();
        InitializeChat();
    }

    private void CopySelection_Click(object sender, RoutedEventArgs e) { }

    private void OpenSettingsWindow()
    {
        var settingsWindow = new SettingsWindow(_configService, _eventBus)
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsWindow();
    }

    private void WorkspaceBar_Click(object sender, MouseButtonEventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var path = NativeFolderPicker.ShowDialog(handle, "选择工作区目录");
        if (path != null)
            _workspaceService.SetWorkspace(path);
    }
}
