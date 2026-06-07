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
