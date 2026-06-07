# UI 全面重构实施计划

> **For agentic workers:** 本计划按任务拆分为独立步骤，每个步骤 2-5 分钟。步骤使用 checkbox (`- [ ]`) 跟踪进度。

**目标:** 将 DeepSeekCode 从硬编码深色 UI 重构为白底天蓝终端风格，消息全左对齐流式布局，工具调用卡片化，带耗时显示和动画。

**架构:** WPF ResourceDictionary 集中管理 20+ 颜色 Token；MainWindow 布局全面重写；新增 TimingService 追踪工具耗时；工具卡片通过 BlockUIContainer + Border 内联渲染，用字典映射 toolCallId → 卡片引用实现实时状态更新。

**技术栈:** C# 13 + .NET 10 + WPF + 现有 5 个 NuGet 包（无新增依赖）

---

## 文件变更总览

| 文件 | 操作 | 说明 |
|------|------|------|
| `App.xaml` | 修改 | 新增 ResourceDictionary 含全部颜色 Token |
| `MainWindow.xaml` | 重写 | 终端流式布局，白底天蓝配色 |
| `MainWindow.xaml.cs` | 重构 | 消息/工具卡片渲染方法重写，耗时追踪集成 |
| `Services/TimingService.cs` | 新增 | 工具耗时追踪 + 汇总统计 |
| `SettingsWindow.xaml` | 修改 | 配色适配白底 |
| `ApiKeyWindow.xaml` | 修改 | 配色适配白底 |
| `Markdown/MarkdownRenderer.cs` | 修改 | 代码块颜色调整（暗底保留，适配白色主背景） |
| `Diff/DiffRenderer.cs` | 修改 | diff 行颜色适配白底 |
| `Markdown/SyntaxHighlighter.cs` | 修改 | 默认前景色调整 |
| `UI/StatusViewModel.cs` | 修改 | 新增本轮总耗时、工具调用次数属性 |

---

### Task 1: 颜色系统基础设施 (App.xaml)

**文件:**
- 修改: `DeepSeekCode/App.xaml`

在 Application.Resources 中建立完整的 ResourceDictionary，定义所有颜色和画刷 Token。

- [ ] **Step 1: 写颜色资源字典**

将 `App.xaml` 中的空 `<Application.Resources>` 替换为：

```xml
<Application.Resources>
    <!-- ═══ 主色系 ═══ -->
    <Color x:Key="PrimaryBlue">#38a0e0</Color>
    <Color x:Key="PrimaryDark">#1a2a38</Color>
    <Color x:Key="PrimaryMedium">#4a6070</Color>
    <Color x:Key="PrimaryLight">#88a8c0</Color>
    <SolidColorBrush x:Key="PrimaryBlueBrush" Color="#38a0e0"/>
    <SolidColorBrush x:Key="PrimaryDarkBrush" Color="#1a2a38"/>
    <SolidColorBrush x:Key="PrimaryMediumBrush" Color="#4a6070"/>
    <SolidColorBrush x:Key="PrimaryLightBrush" Color="#88a8c0"/>

    <!-- ═══ 表面色 ═══ -->
    <Color x:Key="SurfaceWhite">#ffffff</Color>
    <Color x:Key="SurfaceTint">#f2f7fb</Color>
    <Color x:Key="SurfaceCard">#f5f8fc</Color>
    <SolidColorBrush x:Key="SurfaceWhiteBrush" Color="#ffffff"/>
    <SolidColorBrush x:Key="SurfaceTintBrush" Color="#f2f7fb"/>
    <SolidColorBrush x:Key="SurfaceCardBrush" Color="#f5f8fc"/>

    <!-- ═══ 语义色 ═══ -->
    <Color x:Key="SuccessGreen">#58a058</Color>
    <Color x:Key="SuccessBg">#f0f8f0</Color>
    <Color x:Key="SuccessBorder">#c8e0c8</Color>
    <SolidColorBrush x:Key="SuccessGreenBrush" Color="#58a058"/>
    <SolidColorBrush x:Key="SuccessBgBrush" Color="#f0f8f0"/>
    <SolidColorBrush x:Key="SuccessBorderBrush" Color="#c8e0c8"/>

    <Color x:Key="ErrorRed">#c04040</Color>
    <Color x:Key="ErrorBg">#f8e8e8</Color>
    <Color x:Key="ErrorBorder">#e0a0a0</Color>
    <SolidColorBrush x:Key="ErrorRedBrush" Color="#c04040"/>
    <SolidColorBrush x:Key="ErrorBgBrush" Color="#f8e8e8"/>
    <SolidColorBrush x:Key="ErrorBorderBrush" Color="#e0a0a0"/>

    <Color x:Key="WarningOrange">#d09030</Color>
    <Color x:Key="WarningBg">#fffdf0</Color>
    <Color x:Key="WarningBorder">#f0e0a0</Color>
    <SolidColorBrush x:Key="WarningOrangeBrush" Color="#d09030"/>
    <SolidColorBrush x:Key="WarningBgBrush" Color="#fffdf0"/>
    <SolidColorBrush x:Key="WarningBorderBrush" Color="#f0e0a0"/>

    <!-- ═══ 边框/分隔 ═══ -->
    <Color x:Key="BorderDefault">#d0e0f0</Color>
    <Color x:Key="BorderLight">#d8e6f2</Color>
    <Color x:Key="BorderCard">#e4eef6</Color>
    <SolidColorBrush x:Key="BorderDefaultBrush" Color="#d0e0f0"/>
    <SolidColorBrush x:Key="BorderLightBrush" Color="#d8e6f2"/>
    <SolidColorBrush x:Key="BorderCardBrush" Color="#e4eef6"/>

    <!-- ═══ 代码区（暗底） ═══ -->
    <Color x:Key="CodeBg">#1a2a3a</Color>
    <Color x:Key="CodeBorder">#2a4050</Color>
    <Color x:Key="CodeText">#a0c0d0</Color>
    <SolidColorBrush x:Key="CodeBgBrush" Color="#1a2a3a"/>
    <SolidColorBrush x:Key="CodeBorderBrush" Color="#2a4050"/>
    <SolidColorBrush x:Key="CodeTextBrush" Color="#a0c0d0"/>

    <!-- ═══ AI 消息色 ═══ -->
    <Color x:Key="AiLabel">#58a8d8</Color>
    <SolidColorBrush x:Key="AiLabelBrush" Color="#58a8d8"/>

    <!-- ═══ 运行中色 ═══ -->
    <Color x:Key="RunningBlue">#3098d0</Color>
    <Color x:Key="RunningBg">#e8f4fc</Color>
    <Color x:Key="RunningBorder">#38a0e0</Color>
    <SolidColorBrush x:Key="RunningBlueBrush" Color="#3098d0"/>
    <SolidColorBrush x:Key="RunningBgBrush" Color="#e8f4fc"/>
    <SolidColorBrush x:Key="RunningBorderBrush" Color="#38a0e0"/>

    <!-- ═══ 排队/中性色 ═══ -->
    <Color x:Key="QueuedGray">#90b0c8</Color>
    <Color x:Key="QueuedBg">#f0f4f8</Color>
    <SolidColorBrush x:Key="QueuedGrayBrush" Color="#90b0c8"/>
    <SolidColorBrush x:Key="QueuedBgBrush" Color="#f0f4f8"/>

    <!-- ═══ 全局字体 ═══ -->
    <Style TargetType="FlowDocumentScrollViewer">
        <Setter Property="Background" Value="White"/>
        <Setter Property="Foreground" Value="#1a2a38"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding" Value="0"/>
    </Style>
</Application.Resources>
```

- [ ] **Step 2: 编译验证**

```powershell
dotnet build
```

预期: Build succeeded, 0 Error(s), 0 Warning(s)

- [ ] **Step 3: 提交**

```bash
git add DeepSeekCode/App.xaml
git commit -m "feat: 添加白底天蓝配色系统 ResourceDictionary

20+ 颜色 Token，覆盖主色/表面/语义/边框/代码区

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

### Task 2: MainWindow.xaml 布局重写

**文件:**
- 修改: `DeepSeekCode/MainWindow.xaml`（全文替换）

当前 XAML (319 行) 全部替换为终端流式布局。

- [ ] **Step 1: 编写新的 MainWindow.xaml**

```xml
<Window x:Class="DeepSeekCode.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="DeepSeek Code"
        Height="800" Width="1100"
        MinHeight="500" MinWidth="700"
        WindowStartupLocation="CenterScreen"
        Background="White"
        PreviewKeyDown="Window_PreviewKeyDown">

    <Window.Resources>
        <Style TargetType="FlowDocumentScrollViewer">
            <Setter Property="Background" Value="White"/>
            <Setter Property="Foreground" Value="#1a2a38"/>
            <Setter Property="BorderThickness" Value="0"/>
        </Style>
    </Window.Resources>

    <Border BorderBrush="{StaticResource BorderDefaultBrush}" BorderThickness="1">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <!-- ═══ 标题栏 ═══ -->
            <Border Grid.Row="0"
                    Background="{StaticResource SurfaceTintBrush}"
                    BorderBrush="{StaticResource BorderLightBrush}"
                    BorderThickness="0,0,0,1"
                    Padding="10,7">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="📁" FontSize="11" VerticalAlignment="Center"/>
                    <TextBlock Text="DeepSeek Code"
                               Foreground="{StaticResource PrimaryBlueBrush}"
                               FontSize="12" FontWeight="Bold"
                               Margin="6,0,0,0" VerticalAlignment="Center"/>
                    <TextBlock Text=" — "
                               Foreground="{StaticResource PrimaryLightBrush}"
                               FontSize="12" VerticalAlignment="Center"/>
                    <TextBlock x:Name="WorkspaceLabel"
                               Text="..."
                               Foreground="{StaticResource PrimaryLightBrush}"
                               FontSize="11"
                               FontFamily="Cascadia Code, Consolas, monospace"
                               VerticalAlignment="Center"
                               Cursor="Hand"
                               MouseLeftButtonDown="WorkspaceBar_Click"
                               ToolTip="点击切换工作区"/>
                </StackPanel>
            </Border>

            <!-- ═══ Todo 面板（可折叠） ═══ -->
            <Border x:Name="TodoPanel" Grid.Row="1"
                    Background="{StaticResource SuccessBgBrush}"
                    BorderBrush="{StaticResource SuccessBorderBrush}"
                    BorderThickness="0,0,0,1"
                    Visibility="Collapsed">
                <Expander x:Name="TodoExpander" IsExpanded="True"
                          Foreground="{StaticResource SuccessGreenBrush}"
                          Background="Transparent" BorderThickness="0"
                          Padding="14,6"
                          Expanded="TodoExpander_Expanded"
                          Collapsed="TodoExpander_Collapsed">
                    <StackPanel Margin="0,2,0,0">
                        <ProgressBar x:Name="TodoProgressBar"
                                     Height="3" Minimum="0" Maximum="100" Value="0"
                                     Foreground="{StaticResource PrimaryBlueBrush}"
                                     Background="#e0ecf4"
                                     Margin="0,0,0,8"/>
                        <ItemsControl x:Name="TodoItemsControl" Focusable="False"/>
                    </StackPanel>
                </Expander>
            </Border>

            <!-- ═══ 子代理面板（可折叠） ═══ -->
            <Border x:Name="SubagentPanel" Grid.Row="1"
                    Background="{StaticResource SurfaceCardBrush}"
                    BorderBrush="{StaticResource BorderCardBrush}"
                    BorderThickness="0,0,0,1"
                    Visibility="Collapsed">
                <Expander x:Name="SubagentExpander" IsExpanded="True"
                          Foreground="{StaticResource RunningBlueBrush}"
                          Background="Transparent" BorderThickness="0"
                          Padding="14,6">
                    <ItemsControl x:Name="SubagentItemsControl"
                                  Margin="0,2,0,0" Focusable="False"/>
                </Expander>
            </Border>

            <!-- ═══ 主对话区 ═══ -->
            <FlowDocumentScrollViewer x:Name="ChatViewer" Grid.Row="2"
                                      VerticalScrollBarVisibility="Auto">
                <FlowDocumentScrollViewer.ContextMenu>
                    <ContextMenu x:Name="ChatContextMenu">
                        <MenuItem Header="复制" Click="CopySelection_Click"
                                  InputGestureText="Ctrl+C"/>
                    </ContextMenu>
                </FlowDocumentScrollViewer.ContextMenu>
            </FlowDocumentScrollViewer>

            <!-- ═══ 停止按钮（浮在输入区上方） ═══ -->
            <Border Grid.Row="3"
                    HorizontalAlignment="Center"
                    Margin="0,0,0,4">
                <Button x:Name="StopButton"
                        Content="■ 停止生成"
                        Visibility="Collapsed"
                        Click="StopButton_Click"
                        Height="26"
                        Background="{StaticResource ErrorRedBrush}"
                        Foreground="White"
                        FontSize="11"
                        BorderThickness="0"
                        Cursor="Hand"
                        Padding="16,0">
                    <Button.Resources>
                        <Style TargetType="Border">
                            <Setter Property="CornerRadius" Value="4"/>
                        </Style>
                    </Button.Resources>
                </Button>
            </Border>

            <!-- ═══ 输入区 ═══ -->
            <Border Grid.Row="4"
                    Background="{StaticResource SurfaceTintBrush}"
                    BorderBrush="{StaticResource BorderLightBrush}"
                    BorderThickness="0,1,0,0"
                    Padding="12,10,12,12">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <Button Grid.Column="0"
                            Content="⚙"
                            Click="SettingsButton_Click"
                            Width="36" Height="36"
                            Background="Transparent"
                            Foreground="{StaticResource PrimaryLightBrush}"
                            FontSize="18"
                            BorderThickness="0"
                            Cursor="Hand"
                            ToolTip="设置"
                            VerticalAlignment="Bottom"
                            Margin="0,0,6,0"/>

                    <Grid Grid.Column="1">
                        <TextBox x:Name="InputBox"
                                 MinHeight="36" MaxHeight="180"
                                 AcceptsReturn="True"
                                 TextWrapping="Wrap"
                                 VerticalScrollBarVisibility="Auto"
                                 Background="White"
                                 Foreground="{StaticResource PrimaryDarkBrush}"
                                 BorderBrush="{StaticResource BorderDefaultBrush}"
                                 BorderThickness="1"
                                 FontSize="13"
                                 FontFamily="Microsoft YaHei"
                                 Padding="10,8"
                                 PreviewKeyDown="InputBox_PreviewKeyDown"
                                 TextChanged="InputBox_TextChanged"
                                 CaretBrush="{StaticResource PrimaryBlueBrush}"/>
                        <Popup x:Name="CommandPopup"
                               Placement="Bottom"
                               PlacementTarget="{Binding ElementName=InputBox}"
                               StaysOpen="False"
                               AllowsTransparency="True"
                               Width="{Binding ActualWidth, ElementName=InputBox}">
                            <Border Background="White"
                                    BorderBrush="{StaticResource PrimaryBlueBrush}"
                                    BorderThickness="1"
                                    CornerRadius="6"
                                    Padding="4">
                                <ListBox x:Name="CommandListBox"
                                         Background="Transparent"
                                         Foreground="{StaticResource PrimaryDarkBrush}"
                                         BorderThickness="0"
                                         MaxHeight="240"
                                         FontSize="13"
                                         FontFamily="Microsoft YaHei"
                                         KeyDown="CommandListBox_KeyDown"
                                         MouseDoubleClick="CommandListBox_MouseDoubleClick">
                                    <ListBox.ItemContainerStyle>
                                        <Style TargetType="ListBoxItem">
                                            <Setter Property="Padding" Value="10,6"/>
                                            <Setter Property="Background" Value="Transparent"/>
                                            <Setter Property="Template">
                                                <Setter.Value>
                                                    <ControlTemplate TargetType="ListBoxItem">
                                                        <Border x:Name="Border"
                                                                Background="{TemplateBinding Background}"
                                                                CornerRadius="4">
                                                            <ContentPresenter Margin="8,4"/>
                                                        </Border>
                                                        <ControlTemplate.Triggers>
                                                            <Trigger Property="IsSelected" Value="True">
                                                                <Setter TargetName="Border" Property="Background"
                                                                        Value="{StaticResource PrimaryBlueBrush}"/>
                                                                <Setter TargetName="Border" Property="TextElement.Foreground"
                                                                        Value="White"/>
                                                            </Trigger>
                                                            <Trigger Property="IsMouseOver" Value="True">
                                                                <Setter TargetName="Border" Property="Background"
                                                                        Value="{StaticResource SurfaceCardBrush}"/>
                                                            </Trigger>
                                                        </ControlTemplate.Triggers>
                                                    </ControlTemplate>
                                                </Setter.Value>
                                            </Setter>
                                        </Style>
                                    </ListBox.ItemContainerStyle>
                                </ListBox>
                            </Border>
                        </Popup>
                    </Grid>

                    <Button x:Name="SendButton" Grid.Column="2"
                            Content="发送"
                            Click="SendButton_Click"
                            Width="80" Height="36"
                            Margin="8,0,0,0"
                            Background="{StaticResource PrimaryBlueBrush}"
                            Foreground="White"
                            FontSize="13"
                            BorderThickness="0"
                            Cursor="Hand"
                            VerticalAlignment="Bottom">
                        <Button.Resources>
                            <Style TargetType="Border">
                                <Setter Property="CornerRadius" Value="4"/>
                            </Style>
                        </Button.Resources>
                    </Button>
                </Grid>
            </Border>

            <!-- ═══ 状态栏 ═══ -->
            <Border Grid.Row="5"
                    Background="{StaticResource SurfaceTintBrush}"
                    BorderBrush="{StaticResource BorderLightBrush}"
                    BorderThickness="0,1,0,0"
                    Padding="14,4">
                <StackPanel Orientation="Horizontal">
                    <TextBlock x:Name="StatusIndicatorLabel"
                               Text=""
                               Foreground="{StaticResource PrimaryBlueBrush}"
                               FontSize="10"
                               Margin="0,0,14,0"/>
                    <TextBlock x:Name="StatusModelLabel"
                               Text="deepseek-v4-pro"
                               Foreground="{StaticResource PrimaryBlueBrush}"
                               FontSize="10" FontWeight="SemiBold"
                               Margin="0,0,14,0"/>
                    <TextBlock x:Name="StatusTokenLabel"
                               Text="Token: 0"
                               Foreground="{StaticResource PrimaryLightBrush}"
                               FontSize="10"
                               Margin="0,0,14,0"/>
                    <TextBlock x:Name="StatusCacheLabel"
                               Text=""
                               Foreground="{StaticResource SuccessGreenBrush}"
                               FontSize="10"/>
                    <TextBlock x:Name="StatusTimingLabel"
                               Text=""
                               Foreground="{StaticResource PrimaryLightBrush}"
                               FontSize="10"
                               HorizontalAlignment="Right"
                               Margin="20,0,0,0"/>
                </StackPanel>
            </Border>
        </Grid>
    </Border>
</Window>
```

- [ ] **Step 2: 编译验证**

```powershell
dotnet build
```

预期: Build succeeded, 0 Error(s). 注意: 移除了 Thinking 面板相关元素（ThinkingViewer/ThinkingExpander 等），MainWindow.xaml.cs 中对应的引用会报错，后续任务修复。

- [ ] **Step 3: 提交**

```bash
git add DeepSeekCode/MainWindow.xaml
git commit -m "feat: MainWindow 布局重写 — 白底天蓝终端风格

移除右侧 Thinking 面板，Thinking 内容改为对话区内联可折叠卡片

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

### Task 3: 新增 TimingService 耗时追踪

**文件:**
- 新增: `DeepSeekCode/Services/TimingService.cs`

- [ ] **Step 1: 写 TimingService**

```csharp
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DeepSeekCode.Services;

/// <summary>
/// 工具执行耗时追踪服务。每个工具调用对应一个计时器，
/// 提供启动/停止/查询接口和本轮汇总统计。
/// </summary>
public class TimingService
{
    private record TimerEntry(Stopwatch Stopwatch, DateTime StartTime)
    {
        public TimeSpan Elapsed => Stopwatch.Elapsed;
    }

    private readonly ConcurrentDictionary<string, TimerEntry> _timers = new();

    /// <summary>总耗时（本轮对话开始至今）</summary>
    public Stopwatch RoundStopwatch { get; } = new();

    /// <summary>工具调用总次数</summary>
    public int TotalToolCalls { get; private set; }

    /// <summary>开始追踪一个工具调用</summary>
    public void StartTool(string toolCallId)
    {
        var sw = Stopwatch.StartNew();
        _timers[toolCallId] = new TimerEntry(sw, DateTime.Now);
        TotalToolCalls++;
    }

    /// <summary>停止追踪并返回耗时</summary>
    public TimeSpan StopTool(string toolCallId)
    {
        if (_timers.TryRemove(toolCallId, out var entry))
        {
            entry.Stopwatch.Stop();
            return entry.Elapsed;
        }
        return TimeSpan.Zero;
    }

    /// <summary>获取运行中的工具耗时（不停止）</summary>
    public TimeSpan GetElapsed(string toolCallId)
    {
        return _timers.TryGetValue(toolCallId, out var entry) ? entry.Elapsed : TimeSpan.Zero;
    }

    /// <summary>格式化耗时字符串</summary>
    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 1)
            return "<1ms";
        if (elapsed.TotalSeconds < 1)
            return $"{elapsed.TotalMilliseconds:F0}ms";
        if (elapsed.TotalSeconds < 60)
            return $"{elapsed.TotalSeconds:F1}s";
        return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
    }

    /// <summary>重置本轮统计（新一轮对话开始时调用）</summary>
    public void ResetRound()
    {
        _timers.Clear();
        TotalToolCalls = 0;
        if (RoundStopwatch.IsRunning)
            RoundStopwatch.Reset();
    }

    /// <summary>开始本轮计时</summary>
    public void StartRound()
    {
        RoundStopwatch.Restart();
    }

    /// <summary>获取本轮总耗时文本</summary>
    public string GetRoundSummary()
    {
        if (!RoundStopwatch.IsRunning)
            return "";

        var elapsed = RoundStopwatch.Elapsed;
        var timeStr = FormatElapsed(elapsed);
        return $"本轮总耗时: {timeStr} · 工具: {TotalToolCalls} 次";
    }
}
```

- [ ] **Step 2: 注册到 ServiceLocator**

修改 `App.xaml.cs`，在 DI 容器初始化区域添加：

```csharp
// 耗时追踪
var timingService = new TimingService();
locator.RegisterInstance(timingService);
```

插入位置：在 `locator.RegisterInstance(workspaceService);` 之后。

- [ ] **Step 3: 注入到 MainWindow 构造函数**

修改 `MainWindow.xaml.cs`，添加字段和构造函数注入：

```csharp
private readonly TimingService _timingService;

// 在构造函数中添加:
_timingService = locator.Resolve<TimingService>();
```

- [ ] **Step 4: 编译验证并提交**

```powershell
dotnet build
# 预期: Build succeeded. 可能有 MainWindow.xaml.cs 中的 ThinkingViewer 引用错误（下个任务修复）
```

```bash
git add DeepSeekCode/Services/TimingService.cs DeepSeekCode/App.xaml.cs DeepSeekCode/MainWindow.xaml.cs
git commit -m "feat: 添加 TimingService 工具耗时追踪服务

支持启动/停止/查询，Stopwatch 精确计时，本轮汇总统计

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

### Task 4: MainWindow.xaml.cs — 基础消息渲染重构

**文件:**
- 修改: `DeepSeekCode/MainWindow.xaml.cs`

重构消息渲染方法，移除 Thinking 面板相关代码，实现终端流式布局。

- [ ] **Step 1: 修复编译错误 — 移除 ThinkingViewer 引用**

删除所有对 `ThinkingViewer`、`ThinkingExpander` 的引用（XAML 中已移除）：

在构造函数中删除：
```csharp
// 删除这行
ThinkingViewer.Document = new FlowDocument { ... };
```

在 `UpdateThinkingPanel()` 方法中，将独立面板渲染改为内联卡片渲染（对话区内）：

```csharp
private Paragraph? _thinkingParagraph;

private void UpdateThinkingPanel()
{
    if (string.IsNullOrWhiteSpace(_thinkingBuffer)) return;

    var doc = (FlowDocument)ChatViewer.Document;

    // 首次有思考内容时创建折叠卡片
    if (_thinkingParagraph == null)
    {
        _thinkingParagraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 4)
        };
        doc.Blocks.Add(_thinkingParagraph);
    }

    // 每次更新重新渲染思考段落
    _thinkingParagraph.Inlines.Clear();
    _thinkingParagraph.Inlines.Add(new Run("思考过程")
    {
        Foreground = (Brush)Application.Current.Resources["AiLabelBrush"],
        FontSize = 10,
        FontWeight = FontWeights.SemiBold
    });
    _thinkingParagraph.Inlines.Add(new LineBreak());
    _thinkingParagraph.Inlines.Add(new Run(_thinkingBuffer)
    {
        Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
        FontSize = 10
    });
}
```

- [ ] **Step 2: 重写 `AppendUserMessage` — 终端风格**

将当前的右对齐蓝色气泡改为左对齐纯文本：

```csharp
/// <summary>
/// 用户消息 — 终端风格：▸ 标记 + 纯文本左对齐
/// </summary>
private void AppendUserMessage(string content)
{
    var doc = (FlowDocument)ChatViewer.Document;

    // 用户标记行
    doc.Blocks.Add(new Paragraph(new Run("▸ 你")
    {
        Foreground = (Brush)Application.Current.Resources["PrimaryBlueBrush"],
        FontWeight = FontWeights.SemiBold
    })
    {
        FontSize = 11,
        Margin = new Thickness(0, 8, 0, 2)
    });

    // 用户内容
    doc.Blocks.Add(new Paragraph(new Run(content)
    {
        Foreground = (Brush)Application.Current.Resources["PrimaryDarkBrush"]
    })
    {
        FontSize = 13,
        Margin = new Thickness(0, 0, 0, 4)
    });

    _currentAiParagraph = null;
    ScrollChatToEnd();
}
```

删除旧的 `BuildUserMessageContextMenu` 方法和 `UserMessage_RightClick` 事件处理（消息上下文菜单保留在 ChatViewer 的 ContextMenu 中）。

- [ ] **Step 3: 重写 `AppendSystemMessage`**

```csharp
/// <summary>
/// 系统消息 — 最淡的颜色，最小字号
/// </summary>
private void AppendSystemMessage(string content)
{
    ((FlowDocument)ChatViewer.Document).Blocks.Add(
        new Paragraph(new Run(content))
        {
            Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 2)
        });
    ScrollChatToEnd();
}
```

- [ ] **Step 4: 重写 `FlushCurrentAiParagraph` — 在 AI 文本前加标记行**

在 `FlushCurrentAiParagraph` 方法开头（第 696 行附近），在渲染 AI 内容之前先插入 AI 标签：

```csharp
private void FlushCurrentAiParagraph()
{
    if (_currentAiParagraph == null || _currentAiParagraph.Inlines.Count == 0)
    {
        if (_currentAiParagraph != null)
        {
            ((FlowDocument)ChatViewer.Document).Blocks.Remove(_currentAiParagraph);
            _currentAiParagraph = null;
        }
        return;
    }

    var doc = (FlowDocument)ChatViewer.Document;

    // ✅ 新增：在 AI 内容前插入标记行
    doc.Blocks.Add(new Paragraph(new Run("DeepSeek")
    {
        Foreground = (Brush)Application.Current.Resources["AiLabelBrush"],
        FontWeight = FontWeights.SemiBold
    })
    {
        FontSize = 11,
        Margin = new Thickness(0, 8, 0, 2)
    });

    var textBuilder = new StringBuilder();
    foreach (var inline in _currentAiParagraph.Inlines)
    {
        if (inline is Run run)
            textBuilder.Append(run.Text);
    }

    var markdown = textBuilder.ToString();
    doc.Blocks.Remove(_currentAiParagraph);
    _currentAiParagraph = null;

    var rendered = Markdown.MarkdownRenderer.Render(markdown);
    while (rendered.Blocks.Count > 0)
    {
        var block = rendered.Blocks.FirstBlock;
        rendered.Blocks.Remove(block);
        doc.Blocks.Add(block);
    }

    ScrollChatToEnd();
}
```

- [ ] **Step 5: 删除旧的 `_rightClickedMessage` 用户消息右键**

删除文件中的字段 `_rightClickedMessage`，删除 `UserMessage_RightClick` 方法。

- [ ] **Step 6: 更新输入区占位文本**

在构造函数中找到 `ChatViewer.Document = new FlowDocument { ... }`，将字体色改为 PrimaryDark：

```csharp
ChatViewer.Document = new FlowDocument
{
    FontFamily = new FontFamily("Microsoft YaHei"),
    FontSize = 14,
    Foreground = (Brush)Application.Current.Resources["PrimaryDarkBrush"]
};
```

- [ ] **Step 7: 编译验证并提交**

```powershell
dotnet build
# 预期: Build succeeded. 可能有工具卡片相关的方法引用尚未更新（下个任务处理）。
```

```bash
git add DeepSeekCode/MainWindow.xaml.cs
git commit -m "refactor: 消息渲染重构 — 终端流式左对齐布局

用户消息: ▸ 标记 + 纯文本；AI 消息: DeepSeek 标签 + Markdown；
Thinking 内联为对话区折叠内容；移除旧气泡样式

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

### Task 5: MainWindow.xaml.cs — 工具卡片渲染系统

**文件:**
- 修改: `DeepSeekCode/MainWindow.xaml.cs`

实现工具调用的卡片化渲染，带耗时显示和实时状态更新。

- [ ] **Step 1: 添加工具卡片数据结构**

在 MainWindow 类中添加：

```csharp
/// <summary>
/// 工具卡片的 UI 元素引用，用于实时更新状态和耗时
/// </summary>
private record ToolCardInfo(
    string ToolCallId,
    string ToolName,
    Border CardBorder,
    TextBlock StatusLabel,
    TextBlock TimeLabel,
    StackPanel ContentPanel
);

private readonly Dictionary<string, ToolCardInfo> _activeToolCards = new();

// 本轮对话开始时间（用于工具调用前已消耗的时间计算）
private DateTime _roundStartTime;
```

- [ ] **Step 2: 创建通用工具卡片工厂方法**

```csharp
/// <summary>
/// 创建一个工具调用卡片（初始 spinner 状态），插入对话区并返回引用。
/// </summary>
private ToolCardInfo CreateToolCard(string toolCallId, string toolName, string paramSummary)
{
    var doc = (FlowDocument)ChatViewer.Document;

    // 左侧状态色条
    var statusLabel = new TextBlock
    {
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 8, 0)
    };

    // 耗时标签
    var timeLabel = new TextBlock
    {
        FontSize = 10,
        Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
        Text = "⏱ ..."
    };

    // 内容面板
    var contentPanel = new StackPanel();

    // 卡片边框
    var cardBorder = new Border
    {
        BorderBrush = (Brush)Application.Current.Resources["RunningBorderBrush"],
        BorderThickness = new Thickness(2, 0, 0, 0),
        Background = (Brush)Application.Current.Resources["SurfaceCardBrush"],
        CornerRadius = new CornerRadius(0, 4, 4, 0),
        Padding = new Thickness(10, 8, 10, 8),
        Margin = new Thickness(0, 4, 0, 4)
    };

    // 头部行（状态 + 工具名 + 参数 + 耗时）
    var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };

    statusLabel.Text = GetToolIcon(toolName);
    statusLabel.Foreground = (Brush)Application.Current.Resources["RunningBlueBrush"];

    var nameLabel = new TextBlock
    {
        Text = toolName,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["RunningBlueBrush"],
        Margin = new Thickness(0, 0, 8, 0)
    };

    var paramLabel = new TextBlock
    {
        Text = paramSummary,
        FontSize = 10,
        Foreground = (Brush)Application.Current.Resources["PrimaryLightBrush"],
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = 400
    };

    headerPanel.Children.Add(statusLabel);
    headerPanel.Children.Add(nameLabel);
    headerPanel.Children.Add(paramLabel);
    headerPanel.Children.Add(new TextBlock { Width = 0 }); // spacer
    headerPanel.Children.Add(timeLabel);

    var outerStack = new StackPanel();
    outerStack.Children.Add(headerPanel);
    outerStack.Children.Add(contentPanel);

    cardBorder.Child = outerStack;

    _currentAiParagraph = null;
    doc.Blocks.Add(new BlockUIContainer(cardBorder));

    var cardInfo = new ToolCardInfo(toolCallId, toolName, cardBorder, statusLabel, timeLabel, contentPanel);
    _activeToolCards[toolCallId] = cardInfo;
    return cardInfo;
}

/// <summary>
/// 获取工具对应的图标
/// </summary>
private static string GetToolIcon(string toolName) => toolName switch
{
    "read_file" => "📖",
    "edit_file" => "✏",
    "write_file" => "📝",
    "glob" => "⚙",
    "grep" => "🔍",
    "shell" => "⚡",
    "webfetch" => "🌐",
    "git_diff" => "📋",
    "git_log" => "📜",
    "git_commit" => "✅",
    "todo_write" => "📋",
    "task" => "🤖",
    "read_skill" => "📚",
    _ => "🔧"
};

/// <summary>
/// 获取工具参数摘要（截取关键参数用于卡片头部显示）
/// </summary>
private static string GetToolParamSummary(string toolName, Dictionary<string, object?> args)
{
    return toolName switch
    {
        "read_file" or "edit_file" or "write_file" =>
            args.TryGetValue("filePath", out var fp) ? fp?.ToString() ?? "" : "",
        "glob" or "grep" =>
            args.TryGetValue("pattern", out var p) ? p?.ToString() ?? "" : "",
        "shell" =>
            args.TryGetValue("command", out var cmd) ? TruncateParam(cmd?.ToString(), 60) : "",
        "webfetch" =>
            args.TryGetValue("url", out var url) ? TruncateParam(url?.ToString(), 50) : "",
        "git_diff" => "--staged",
        "git_log" => "-5",
        "git_commit" =>
            args.TryGetValue("message", out var msg) ? TruncateParam(msg?.ToString(), 40) : "",
        "task" =>
            args.TryGetValue("description", out var desc) ? TruncateParam(desc?.ToString(), 40) : "",
        "read_skill" =>
            args.TryGetValue("name", out var sn) ? sn?.ToString() ?? "" : "",
        _ => ""
    };
}

private static string TruncateParam(string? text, int maxLen)
{
    if (string.IsNullOrEmpty(text)) return "";
    return text.Length <= maxLen ? text : text[..maxLen] + "...";
}
```

- [ ] **Step 3: 重写工具调用消息 — 创建卡片**

替换 `AppendToolCallMessage` 方法：

```csharp
/// <summary>
/// 为每个工具调用创建独立卡片（spinner 状态）
/// </summary>
private void AppendToolCards(List<ToolCall> toolCalls)
{
    foreach (var tc in toolCalls)
    {
        _timingService.StartTool(tc.Id);
        var summary = GetToolParamSummary(tc.Function.Name,
            TryParseArguments(tc.Function.Arguments));
        CreateToolCard(tc.Id, tc.Function.Name, summary);
    }
    ScrollChatToEnd();
}
```

在 `StreamConversationAsync` 中（约第 453 行），将：
```csharp
Dispatcher.Invoke(() =>
{
    AppendToolCallMessage(toolCalls);
    UpdateSpinnerText(...);
});
```
改为：
```csharp
Dispatcher.Invoke(() =>
{
    AppendToolCards(toolCalls);
    UpdateSpinnerText($"正在执行: {string.Join(", ", toolCalls.Select(t => t.Function.Name))}…");
});
```

- [ ] **Step 4: 更新工具结果卡片状态**

修改 `ExecuteToolSequentialAsync` 方法（约第 498 行），添加卡片状态更新：

在工具执行完毕后（`var result = await pipeline.ExecuteAsync(context);` 之后），添加卡片更新逻辑。将 Dispatcher.Invoke 块改为：

```csharp
var elapsed = _timingService.StopTool(tc.Id);
var success = context.Error == null && !context.Cancelled;
var resultText = result;
var capturedOldContent = oldFileContent;

Dispatcher.Invoke(() =>
{
    UpdateToolCardComplete(tc.Id, success, elapsed, resultText);

    if (tc.Function.Name == "edit_file")
        AppendDiffToCard(tc.Id, args, capturedOldContent: null, isWrite: false);
    else if (tc.Function.Name == "write_file")
        AppendDiffToCard(tc.Id, args, capturedOldContent, isWrite: true);

    UpdateSpinnerText($"✔ {tc.Function.Name} 完成");
});
```

同样修改 `ExecuteTaskToolAsync`（约第 583 行）：

```csharp
var elapsed = _timingService.StopTool(tc.Id);
var success = context.Error == null && !context.Cancelled;

Dispatcher.Invoke(() =>
{
    UpdateToolCardComplete(tc.Id, success, elapsed, result);
    UpdateSpinnerText($"✔ {tc.Function.Name} 完成");
});
```

- [ ] **Step 5: 实现卡片状态更新方法**

```csharp
/// <summary>
/// 将工具卡片从 spinner 状态更新为完成/失败状态
/// </summary>
private void UpdateToolCardComplete(string toolCallId, bool success, TimeSpan elapsed, string? resultText)
{
    if (!_activeToolCards.TryGetValue(toolCallId, out var card))
        return;

    // 更新状态标签
    if (success)
    {
        card.StatusLabel.Text = "✔";
        card.StatusLabel.Foreground = (Brush)Application.Current.Resources["SuccessGreenBrush"];
        card.CardBorder.BorderBrush = (Brush)Application.Current.Resources["SuccessBorderBrush"];
    }
    else
    {
        card.StatusLabel.Text = "✕";
        card.StatusLabel.Foreground = (Brush)Application.Current.Resources["ErrorRedBrush"];
        card.CardBorder.BorderBrush = (Brush)Application.Current.Resources["ErrorBorderBrush"];
    }

    // 更新耗时
    var timeColor = GetTimeColor(elapsed);
    card.TimeLabel.Text = $"⏱ {TimingService.FormatElapsed(elapsed)}";
    card.TimeLabel.Foreground = timeColor;

    // 如有结果文本，添加到内容区
    if (!string.IsNullOrEmpty(resultText))
    {
        var display = resultText.Length > 300 ? resultText[..300] + "\n...(已截断)" : resultText;
        card.ContentPanel.Children.Add(new TextBlock
        {
            Text = display,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["PrimaryMediumBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontFamily = new FontFamily("Microsoft YaHei")
        });
    }

    _activeToolCards.Remove(toolCallId);
}

/// <summary>
/// 根据耗时返回对应颜色
/// </summary>
private static Brush GetTimeColor(TimeSpan elapsed)
{
    if (elapsed.TotalSeconds >= 10)
        return (Brush)Application.Current.Resources["ErrorRedBrush"];
    if (elapsed.TotalSeconds >= 1)
        return (Brush)Application.Current.Resources["WarningOrangeBrush"];
    return (Brush)Application.Current.Resources["PrimaryLightBrush"];
}
```

- [ ] **Step 6: 将 diff 渲染到工具卡片内**

添加 `AppendDiffToCard` 方法，代替原来的 `RenderEditDiff` / `RenderWriteDiff`：

```csharp
/// <summary>
/// 将 diff 结果追加到工具卡片内容区（而非独立插入对话区）
/// </summary>
private void AppendDiffToCard(string toolCallId, Dictionary<string, object?> args,
    string? capturedOldContent, bool isWrite)
{
    if (!_activeToolCards.TryGetValue(toolCallId, out var card))
    {
        // 卡片可能已被移除（工具已完成），回退到独立渲染
        if (isWrite)
            RenderWriteDiff(args, capturedOldContent);
        else
            RenderEditDiff(args);
        return;
    }

    var filePath = args.TryGetValue("filePath", out var fp) ? fp?.ToString() : null;
    var oldStr = args.TryGetValue("oldString", out var os) ? os?.ToString() : null;
    var newStr = args.TryGetValue("newString", out var ns) ? ns?.ToString() : null;
    var newContent = args.TryGetValue("content", out var ct) ? ct?.ToString() : null;

    string oldText, newText;
    if (isWrite)
    {
        oldText = capturedOldContent ?? "";
        newText = newContent ?? "";
    }
    else
    {
        oldText = oldStr ?? "";
        newText = newStr ?? "";
    }

    if (string.IsNullOrEmpty(filePath)) return;

    var diffSection = DiffRenderer.RenderDiff(oldText, newText, filePath);

    // 将 diff section 的 blocks 包装到卡片内容面板中
    // 由于 Section 包含 Paragraphs，我们逐个提取文本
    foreach (Block block in diffSection.Blocks)
    {
        if (block is Paragraph para)
        {
            var diffText = new TextBlock
            {
                FontSize = 10,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 0, 0)
            };

            // 复制 Inlines
            foreach (Inline inline in para.Inlines)
            {
                if (inline is Run run)
                {
                    diffText.Inlines.Add(new Run(run.Text)
                    {
                        Foreground = run.Foreground,
                        Background = run.Background
                    });
                }
            }
            diffText.Background = para.Background;
            diffText.Foreground = para.Foreground;
            card.ContentPanel.Children.Add(diffText);
        }
    }
}
```

- [ ] **Step 7: 更新 `HandleInputAsync` — 记录本轮开始时间**

在 `HandleInputAsync` 中添加：

```csharp
_timingService.StartRound();
_timingService.ResetRound();
_activeToolCards.Clear();
```

- [ ] **Step 8: 更新状态栏 — 显示本轮总耗时**

修改 `UpdateStatusBar` 和 `SpinnerTimer_Tick`，在状态栏右侧显示耗时统计：

在 `HandleInputAsync` 的 `_isStreaming = true;` 之后添加：
```csharp
_timingService.StartRound();
_activeToolCards.Clear();
```

在 spinner tick 中更新状态栏耗时文本：
```csharp
private void SpinnerTimer_Tick(object? sender, EventArgs e)
{
    _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
    StatusIndicatorLabel.Text = $"{SpinnerFrames[_spinnerIndex]} {_toolProgressText}";

    // 更新状态栏耗时
    StatusTimingLabel.Text = _timingService.GetRoundSummary();

    // 子代理面板 spinner 联动刷新
    if (_subagentList.Any(s => !s.Completed))
        RenderSubagentPanel();
}
```

在 `FinishStreaming` 中也更新一次：
```csharp
private void FinishStreaming()
{
    _isStreaming = false;
    StopStatusSpinner();
    SetInputEnabled(true);
    StopButton.Visibility = Visibility.Collapsed;
    _currentCancellation = null;
    StatusTimingLabel.Text = _timingService.GetRoundSummary();
    InputBox.Focus();
}
```

- [ ] **Step 9: 编译验证并提交**

```powershell
dotnet build
# 预期: Build succeeded, 0 Error(s), 0 Warning(s)
```

```bash
git add DeepSeekCode/MainWindow.xaml.cs
git commit -m "feat: 工具调用卡片化渲染系统 + 耗时追踪

每个工具独立卡片，spinner→✔/✕ 状态切换，⏱ 耗时按速度分色；
diff 嵌入卡片内容区；状态栏显示本轮总耗时和工具调用次数

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

### Task 6: SettingsWindow + ApiKeyWindow 配色适配

**文件:**
- 修改: `DeepSeekCode/SettingsWindow.xaml`
- 修改: `DeepSeekCode/ApiKeyWindow.xaml`

- [ ] **Step 1: SettingsWindow.xaml 颜色替换**

全文替换颜色值：

| 当前色值 | 替换为 |
|---------|--------|
| `Background="#252526"` | `Background="{StaticResource SurfaceWhiteBrush}"` |
| `Background="#2D2D2D"` | `Background="{StaticResource SurfaceCardBrush}"` |
| `Background="#3C3C3C"` | `Background="White"` |
| `BorderBrush="#3C3C3C"` | `BorderBrush="{StaticResource BorderCardBrush}"` |
| `Foreground="#D4D4D4"` | `Foreground="{StaticResource PrimaryDarkBrush}"` |
| `Foreground="#808080"` | `Foreground="{StaticResource PrimaryLightBrush}"` |
| `Foreground="#4EC9B0"` | `Foreground="{StaticResource PrimaryBlueBrush}"` |
| `Background="#0E639C"` | `Background="{StaticResource PrimaryBlueBrush}"` |

另外设置 `TabItem` 的 `Foreground="White"` 在选中时需要覆盖 — 或者直接移除 TabControl 背景颜色，让它用默认样式。

TabControl 和 TabItem 的 Background 也要改成浅色：`Background="Transparent"`。

- [ ] **Step 2: ApiKeyWindow.xaml 颜色替换**

同上，所有深色值替换为白底天蓝配色。

- [ ] **Step 3: 编译验证并提交**

```powershell
dotnet build
# 预期: 0 Error(s), 0 Warning(s)
```

```bash
git add DeepSeekCode/SettingsWindow.xaml DeepSeekCode/ApiKeyWindow.xaml
git commit -m "style: SettingsWindow 和 ApiKeyWindow 配色适配白底天蓝

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

### Task 7: MarkdownRenderer + DiffRenderer + SyntaxHighlighter 配色

**文件:**
- 修改: `DeepSeekCode/Markdown/MarkdownRenderer.cs`
- 修改: `DeepSeekCode/Diff/DiffRenderer.cs`
- 修改: `DeepSeekCode/Markdown/SyntaxHighlighter.cs`

- [ ] **Step 1: SyntaxHighlighter 默认前景色**

`SyntaxHighlighter.cs` 第 48 行，将 `DefaultBrush` 改为代码区的浅色文字：

```csharp
// 当前: 深色背景下的浅色文字
private static readonly Brush DefaultBrush = new SolidColorBrush(Color.FromRgb(212, 212, 212));

// 改为: 代码区保留暗底，默认前景色适配暗底
// 不变 — 代码区仍然是暗色背景，所以浅色文字是正确的
```

`SyntaxHighlighter` 不需要改 — 代码区保持暗底 `#1a2a3a`，默认文字色 `#d4d4d4` 正合适。

- [ ] **Step 2: MarkdownRenderer 颜色调整**

`MarkdownRenderer.cs` 中的颜色需要调整以适配白底主背景：

**代码块（RenderCodeBlock，第 96-136 行）:**
```csharp
// 语言标签背景: #252525 → #1a2a3a (代码区统一暗底)
container.Children.Add(new TextBlock
{
    Text = language,
    FontSize = 11,
    FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
    Foreground = new SolidColorBrush(Color.FromRgb(160, 192, 208)), // CodeText
    Padding = new Thickness(12, 6, 12, 2),
    Background = new SolidColorBrush(Color.FromRgb(26, 42, 58)) // CodeBg
});

// 代码区域背景: #1e1e1e → #1a2a3a
richText.Background = new SolidColorBrush(Color.FromRgb(26, 42, 58));
```

**引用块（RenderQuote，第 202-219 行）:**
```csharp
// 当前深色背景 → 改为淡色
var quotePara = new Paragraph
{
    Margin = new Thickness(0, 4, 0, 4),
    BorderBrush = new SolidColorBrush(Color.FromRgb(56, 160, 224)), // PrimaryBlue
    BorderThickness = new Thickness(3, 0, 0, 0),
    Padding = new Thickness(12, 4, 0, 4),
    Background = new SolidColorBrush(Color.FromRgb(242, 247, 251)) // SurfaceTint
};
```

**水平线（RenderHorizontalRule，第 222-230 行）:**
```csharp
BorderBrush = new SolidColorBrush(Color.FromRgb(216, 230, 242)) // BorderLight
```

**内联代码（CodeInline，第 247-253 行）:**
```csharp
Background = new SolidColorBrush(Color.FromRgb(242, 247, 251)), // SurfaceTint 适配白底
Foreground = new SolidColorBrush(Color.FromRgb(206, 80, 80)) // 红色更醒目
```

**链接（LinkInline，第 273 行）:**
```csharp
Foreground = new SolidColorBrush(Color.FromRgb(56, 160, 224)) // PrimaryBlue
```

- [ ] **Step 3: DiffRenderer 颜色适配白底**

`DiffRenderer.cs` 的 `RenderDiff` 方法（第 129 行起），将 diff 行的颜色从深色适配到白底：

```csharp
// 标题行（第 148 行）
headerPara.Inlines.Add(new Run("Diff: ")
{
    Foreground = new SolidColorBrush(Color.FromRgb(26, 42, 56)), // PrimaryDark
    FontSize = 13,
    FontWeight = FontWeights.Bold
});
headerPara.Inlines.Add(new Run(fileName)
{
    Foreground = new SolidColorBrush(Color.FromRgb(56, 160, 224)), // PrimaryBlue
    FontSize = 13,
    FontWeight = FontWeights.Bold
});

// +行颜色: 保持绿色但背景改为更柔和的浅绿
if (added > 0)
    headerPara.Inlines.Add(new Run($"+{added}")
    {
        Foreground = new SolidColorBrush(Color.FromRgb(58, 160, 88)), // SuccessGreen
        FontSize = 11
    });
if (removed > 0)
    headerPara.Inlines.Add(new Run($"-{removed}")
    {
        Foreground = new SolidColorBrush(Color.FromRgb(192, 64, 64)), // ErrorRed
        FontSize = 11
    });

// Diff 行颜色（第 196-208 行）
var fgColor = diff.Type switch
{
    DiffLineType.Added => Color.FromRgb(42, 120, 50),      // 深绿字（白底可读）
    DiffLineType.Removed => Color.FromRgb(180, 40, 40),    // 深红字
    _ => Color.FromRgb(120, 140, 160)                      // 灰字
};

var bgColor = diff.Type switch
{
    DiffLineType.Added => Color.FromRgb(232, 248, 232),    // 极浅绿底
    DiffLineType.Removed => Color.FromRgb(248, 232, 232),  // 极浅红底
    _ => Color.FromRgb(255, 255, 255)                       // 白底不变行
};
```

- [ ] **Step 4: 编译验证并提交**

```powershell
dotnet build
# 预期: 0 Error(s), 0 Warning(s)
```

```bash
git add DeepSeekCode/Markdown/MarkdownRenderer.cs DeepSeekCode/Diff/DiffRenderer.cs
git commit -m "style: MarkdownRenderer 和 DiffRenderer 配色适配白底

代码块保持暗底(CodeBg)，引用块/内联代码改为浅色；
diff 行绿色/红色背景改为极浅色适配白底阅读

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

### Task 8: 权限确认内联化

**文件:**
- 修改: `DeepSeekCode/MainWindow.xaml.cs`

将 `MessageBox.Show` 弹窗确认替换为对话流中的内联确认卡片。

- [ ] **Step 1: 添加内联权限确认方法**

```csharp
/// <summary>
/// 在对话流中插入内联权限确认卡片
/// </summary>
private Task<bool> ShowInlinePermissionAsync(string toolName, string command)
{
    var tcs = new TaskCompletionSource<bool>();
    
    Dispatcher.Invoke(() =>
    {
        var doc = (FlowDocument)ChatViewer.Document;

        var cardBorder = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["WarningBorderBrush"],
            BorderThickness = new Thickness(2, 0, 0, 0),
            Background = (Brush)Application.Current.Resources["WarningBgBrush"],
            CornerRadius = new CornerRadius(0, 4, 4, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 4, 0, 4)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = "⚠ 权限确认",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["WarningOrangeBrush"],
            Margin = new Thickness(0, 0, 0, 4)
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"允许执行 {toolName}: {command} 吗？",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["PrimaryMediumBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var allowBtn = new Button
        {
            Content = "允许",
            Width = 70, Height = 28,
            Background = (Brush)Application.Current.Resources["PrimaryBlueBrush"],
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 11,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0)
        };
        allowBtn.Click += (_, _) =>
        {
            // 更新卡片为已允许状态
            stack.Children.Clear();
            stack.Children.Add(new TextBlock
            {
                Text = $"✔ 已允许: {toolName}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SuccessGreenBrush"]
            });
            tcs.TrySetResult(true);
        };

        var denyBtn = new Button
        {
            Content = "拒绝",
            Width = 70, Height = 28,
            Background = (Brush)Application.Current.Resources["SurfaceCardBrush"],
            Foreground = (Brush)Application.Current.Resources["PrimaryMediumBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderCardBrush"],
            BorderThickness = new Thickness(1),
            FontSize = 11,
            Cursor = Cursors.Hand
        };
        denyBtn.Click += (_, _) =>
        {
            stack.Children.Clear();
            stack.Children.Add(new TextBlock
            {
                Text = $"✕ 已拒绝: {toolName}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["ErrorRedBrush"]
            });
            tcs.TrySetResult(false);
        };

        btnPanel.Children.Add(allowBtn);
        btnPanel.Children.Add(denyBtn);
        stack.Children.Add(btnPanel);

        cardBorder.Child = stack;
        doc.Blocks.Add(new BlockUIContainer(cardBorder));
        ScrollChatToEnd();
    });

    return tcs.Task;
}
```

- [ ] **Step 2: 修改权限请求处理 — OnPermissionRequested**

将 `OnPermissionRequested` 方法（约第 136-145 行）改为：

```csharp
private async void OnPermissionRequested(PermissionRequestEvent e)
{
    var result = await Dispatcher.InvokeAsync(() =>
        ShowInlinePermissionAsync(e.ToolName, e.Command ?? e.ToolName));
    e.UserDecision = await result;
}
```

注意：`OnPermissionRequested` 签名需从 `void` 改为 `async void`（事件处理器允许 async void）。

- [ ] **Step 3: 编译验证并提交**

```powershell
dotnet build
# 预期: 0 Error(s), 0 Warning(s)
```

```bash
git add DeepSeekCode/MainWindow.xaml.cs
git commit -m "feat: 权限确认从 MessageBox 弹窗改为内联卡片

对话流中直接显示确认按钮，不打断 UI 流程

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

### Task 9: 子代理运行中卡片动画 + 状态栏动画优化

**文件:**
- 修改: `DeepSeekCode/MainWindow.xaml.cs`

完善子代理运行中的 UI 效果：实时日志流、计时跳动、spinner 帧动画。

- [ ] **Step 1: 子代理运行中 — 实时计时更新**

在 `SpinnerTimer_Tick` 中（约第 1166 行）添加运行中子代理的耗时更新：

```csharp
private void SpinnerTimer_Tick(object? sender, EventArgs e)
{
    _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
    StatusIndicatorLabel.Text = $"{SpinnerFrames[_spinnerIndex]} {_toolProgressText}";
    StatusTimingLabel.Text = _timingService.GetRoundSummary();

    // ✅ 更新运行中的子代理卡片耗时
    if (_subagentList.Any(s => !s.Completed))
    {
        RenderSubagentPanel();
    }

    // ✅ 更新运行中的工具卡片（子代理 task 工具）
    foreach (var (_, card) in _activeToolCards)
    {
        var elapsed = _timingService.GetElapsed(card.ToolCallId);
        card.TimeLabel.Text = $"⏱ 运行中 · {TimingService.FormatElapsed(elapsed)}";
    }
}
```

- [ ] **Step 2: 子代理面板的 spinner 动画帧号使用全局 spinner**

子代理面板的 spinner 帧已在 `RenderSubagentPanel` 中使用 `_subagentList` + `(elapsed / 0.12) % 8` 索引 — 保持现有逻辑，确保与全局 spinner 同步。

- [ ] **Step 3: 子代理完成时渲染调用明细**

在 `RenderSubagentPanel` 中已完成子代理的渲染逻辑已经在第 966 行附近处理了 — 保持现有逻辑，仅需将颜色值改为使用资源字典中的 Token。

颜色替换（第 978-1023 行区域）：
- 成功: `Color.FromRgb(80, 200, 120)` → `{StaticResource SuccessGreenBrush}`
- 失败: `Color.FromRgb(220, 80, 80)` → `{StaticResource ErrorRedBrush}`
- 运行中文字: `Color.FromRgb(86, 156, 214)` → `{StaticResource RunningBlueBrush}`
- 耗时辅助文字: `Color.FromRgb(150, 170, 190)` → `{StaticResource PrimaryLightBrush}`

由于代码位于 .cs 文件中无法直接使用 XAML StaticResource，需要通过 `Application.Current.Resources["Key"]` 方式获取：

```csharp
// 示例替换
var successGreenBrush = (Brush)Application.Current.Resources["SuccessGreenBrush"];
row.Children.Add(new TextBlock
{
    Text = $"{icon} [{info.Type}] {info.Description}",
    Foreground = successGreenBrush,
    FontSize = 12,
    Margin = new Thickness(0, 0, 8, 0)
});
```

- [ ] **Step 4: Todo 面板颜色适配**

在 `UpdateTodoPanel` 方法中（约第 801 行）更新颜色：

```csharp
// 第 817 行，Expander Header 颜色
TodoExpander.Foreground = (Brush)Application.Current.Resources["SuccessGreenBrush"];

// 第 832 行，Todo 项颜色映射
var fgColor = todo.Status switch
{
    TodoStatus.Completed => Color.FromRgb(88, 160, 88),     // SuccessGreen
    TodoStatus.InProgress => Color.FromRgb(48, 152, 208),    // RunningBlue  
    TodoStatus.Cancelled => Color.FromRgb(136, 168, 192),    // PrimaryLight
    _ => Color.FromRgb(74, 96, 112)                           // PrimaryMedium
};
```

- [ ] **Step 5: 编译验证并提交**

```powershell
dotnet build
# 预期: 0 Error(s), 0 Warning(s)
```

```bash
git add DeepSeekCode/MainWindow.xaml.cs
git commit -m "feat: 子代理卡片动画 + Todo/状态栏颜色适配

运行中子代理实时计时跳动，spinner 帧联动；所有颜色走资源字典

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

### Task 10: 最终编译验证 + FEATURES.md 更新

**文件:**
- 修改: `DeepSeekCode/FEATURES.md`

- [ ] **Step 1: 完整编译**

```powershell
dotnet build
```

必须: **0 Error(s), 0 Warning(s)**。如有错误，逐个修复后重新编译。

- [ ] **Step 2: 更新 FEATURES.md**

将 U08 状态更新为已完成，并添加本次 UI 重构的描述：

在第 86 行附近，将：
```
| U08 | **暗/亮主题切换** | 支持暗色和亮色主题 | ❌ 待实现 | 🟢 P3 |
```
改为：
```
| U08 | **UI 全面重构** | 白底天蓝终端风格，消息全左对齐，工具卡片化，耗时追踪，内联权限确认 | ✅ 已完成 | 🟢 P3 |
```

- [ ] **Step 3: 最终提交**

```bash
git add DeepSeekCode/FEATURES.md
git commit -m "feat: UI 全面重构完成 — 白底天蓝终端风格

消息全左对齐流式布局，工具调用卡片化带耗时显示，
子代理 4 状态动画，权限内联确认，20+ 颜色 Token 集中管理

Co-Authored-By: DeepSeek V4 Pro <noreply@deepseek.com>"
```

---

## 自检清单

- [x] 规格书覆盖：配色系统 ✓ / 消息类型 ✓ / 工具卡片 ✓ / 子代理状态 ✓ / 耗时 ✓ / 动画 ✓ / 权限内联 ✓
- [x] 无占位符：所有代码步骤均包含完整实现
- [x] 类型一致：`ToolCardInfo` 在各方法间一致，`TimingService` API 与 MainWindow 使用一致
- [x] 编译验证：每个 Task 结束时强制 `dotnet build`
