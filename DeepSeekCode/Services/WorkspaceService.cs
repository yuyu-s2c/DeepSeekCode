using System.IO;

namespace DeepSeekCode.Services;

/// <summary>
/// 工作区服务：管理项目根目录，所有工具操作均以此为默认路径。
/// 启动时优先恢复上次退出的工作区，否则自动检测。
/// </summary>
public class WorkspaceService
{
    private string _workspacePath;
    private readonly EventBus? _eventBus;
    private readonly ConfigService? _configService;

    public string WorkspacePath
    {
        get => _workspacePath;
        set
        {
            if (_workspacePath == value) return;
            _workspacePath = value;
            PersistWorkspace();
            _eventBus?.Publish(new WorkspaceChangedEvent { Path = value });
        }
    }

    public string WorkspaceName => Path.GetFileName(_workspacePath.TrimEnd(Path.DirectorySeparatorChar));

    public WorkspaceService(ConfigService? configService = null, EventBus? eventBus = null)
    {
        _configService = configService;
        _eventBus = eventBus;
        _workspacePath = ResolveInitialWorkspace();
    }

    /// <summary>解析初始工作区：优先用上次保存的路径，否则自动检测</summary>
    private string ResolveInitialWorkspace()
    {
        var saved = _configService?.Config.LastWorkspacePath;
        if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
            return saved;
        return DetectWorkspace();
    }

    /// <summary>持久化当前工作区到配置文件</summary>
    private void PersistWorkspace()
    {
        if (_configService == null) return;
        _configService.Config.LastWorkspacePath = _workspacePath;
        _configService.Save(_configService.Config);
    }

    /// <summary>
    /// 自动检测工作区：向上查找 .git / .sln / .csproj 目录
    /// </summary>
    public static string DetectWorkspace()
    {
        var current = Environment.CurrentDirectory;
        var dir = current;

        while (dir.Length > 3) // 到盘符根为止
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            if (Directory.GetFiles(dir, "*.sln").Length > 0)
                return dir;
            if (Directory.GetFiles(dir, "*.csproj").Length > 0)
                return dir;
            if (Directory.Exists(Path.Combine(dir, ".deepseek-code")))
                return dir;
            if (File.Exists(Path.Combine(dir, "DEEPSEEK.md")))
                return dir;

            var parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }

        return current;
    }

    /// <summary>
    /// 切换工作区
    /// </summary>
    public void SetWorkspace(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"工作区不存在: {path}");
        WorkspacePath = Path.GetFullPath(path);
    }

    /// <summary>
    /// 将相对路径解析为绝对路径（相对于工作区）
    /// </summary>
    public string ResolvePath(string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
            return relativeOrAbsolutePath;
        return Path.Combine(WorkspacePath, relativeOrAbsolutePath);
    }

    /// <summary>
    /// 临时切换到工作区目录（用于工具执行期间）
    /// </summary>
    public IDisposable EnterWorkspace()
    {
        var previousDir = Environment.CurrentDirectory;
        Environment.CurrentDirectory = WorkspacePath;
        return new WorkspaceScope(() => Environment.CurrentDirectory = previousDir);
    }

    private class WorkspaceScope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

public class WorkspaceChangedEvent
{
    public string Path { get; init; } = "";
}
