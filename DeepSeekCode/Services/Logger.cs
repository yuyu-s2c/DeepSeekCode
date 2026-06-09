using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using DeepSeekCode.MCP;

namespace DeepSeekCode.Services;

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// 文件日志服务 — 写入 ~/.deepseek-code/logs/ 目录。
/// 线程安全，异步写入，不阻塞主线程。
/// </summary>
public class Logger : ILogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".deepseek-code", "logs");

    private readonly string _filePath;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private volatile bool _running = true;

    public Logger(string category = "app")
    {
        Directory.CreateDirectory(LogDir);
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        _filePath = Path.Combine(LogDir, $"{category}-{date}.log");
        Task.Run(FlushLoop);
    }

    public void Debug(string msg) => Enqueue(LogLevel.Debug, msg);
    public void Info(string msg) => Enqueue(LogLevel.Info, msg);
    public void Warn(string msg) => Enqueue(LogLevel.Warn, msg);
    public void Error(string msg) => Enqueue(LogLevel.Error, msg);
    public void Error(Exception ex, string msg) => Enqueue(LogLevel.Error, $"{msg}: {ex}");

    private void Enqueue(LogLevel level, string msg)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        _queue.Enqueue($"[{ts}] [{level.ToString().ToUpperInvariant(),-5}] {msg}");
        _signal.Set();
    }

    private async Task FlushLoop()
    {
        while (_running)
        {
            _signal.WaitOne(500);
            Flush();
        }
        Flush(); // 退出前最后一次刷新
    }

    private void Flush()
    {
        var sb = new StringBuilder();
        while (_queue.TryDequeue(out var entry))
            sb.AppendLine(entry);

        if (sb.Length > 0)
        {
            try { File.AppendAllText(_filePath, sb.ToString(), Encoding.UTF8); }
            catch { /* 日志写入失败不应中断应用 */ }
        }
    }

    public void Dispose()
    {
        _running = false;
        _signal.Set();
    }
}

/// <summary>
/// 全局异常处理 — 注册 AppDomain 和 Dispatcher 未处理异常，写入日志。
/// </summary>
public static class CrashHandler
{
    private static Logger? _logger;

    public static void Initialize(Logger logger)
    {
        _logger = logger;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            _logger.Error($"CRASH: UnhandledException: {ex}");
            _logger.Dispose();
        };
    }
}
