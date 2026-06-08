using System.IO;
using System.Text.Json;

namespace DeepSeekCode.Session;

public class SessionMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "新对话";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public int MessageCount { get; set; }
    public string Model { get; set; } = "deepseek-v4-pro";
    public string WorkspacePath { get; set; } = "";
}

public interface ISessionStore
{
    void SetWorkspace(string workspacePath);
    Task<List<SessionMetadata>> ListSessionsAsync();
    Task SaveAsync(SessionMetadata metadata, string messagesJson);
    Task<SessionData?> LoadAsync(string sessionId);
    Task DeleteAsync(string sessionId);
    string GetStoragePath();
}

public record SessionData(SessionMetadata Metadata, string MessagesJson);

public class FileSessionStore : ISessionStore
{
    private string _workspacePath = "";
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    /// <summary>设置当前工作区，存储路径变为 {workspace}/.deepseek-code/sessions/</summary>
    public void SetWorkspace(string workspacePath)
    {
        _workspacePath = workspacePath;
        Directory.CreateDirectory(GetStoragePath());
    }

    public Task<List<SessionMetadata>> ListSessionsAsync()
    {
        var result = new List<SessionMetadata>();
        var storagePath = GetStoragePath();
        if (!Directory.Exists(storagePath))
            return Task.FromResult(result);

        var dir = new DirectoryInfo(storagePath);

        foreach (var file in dir.GetFiles("*.json").OrderByDescending(f => f.LastWriteTime))
        {
            try
            {
                var json = File.ReadAllText(file.FullName);
                var wrapper = JsonSerializer.Deserialize<SessionWrapper>(json);
                if (wrapper?.Metadata != null)
                    result.Add(wrapper.Metadata);
            }
            catch
            {
                // 跳过损坏的会话文件
            }
        }

        return Task.FromResult(result);
    }

    public Task SaveAsync(SessionMetadata metadata, string messagesJson)
    {
        metadata.UpdatedAt = DateTime.Now;
        metadata.WorkspacePath = _workspacePath;

        var wrapper = new SessionWrapper
        {
            Metadata = metadata,
            Messages = messagesJson
        };

        var filePath = GetFilePath(metadata.Id);
        var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
        File.WriteAllText(filePath, json);

        return Task.CompletedTask;
    }

    public Task<SessionData?> LoadAsync(string sessionId)
    {
        var filePath = GetFilePath(sessionId);
        if (!File.Exists(filePath))
            return Task.FromResult<SessionData?>(null);

        try
        {
            var json = File.ReadAllText(filePath);
            var wrapper = JsonSerializer.Deserialize<SessionWrapper>(json);
            if (wrapper?.Metadata != null && wrapper.Messages != null)
                return Task.FromResult<SessionData?>(new SessionData(wrapper.Metadata, wrapper.Messages));
        }
        catch { }

        return Task.FromResult<SessionData?>(null);
    }

    public Task DeleteAsync(string sessionId)
    {
        var filePath = GetFilePath(sessionId);
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }

    public string GetStoragePath()
        => Path.Combine(_workspacePath, ".deepseek-code", "sessions");

    private string GetFilePath(string sessionId)
        => Path.Combine(GetStoragePath(), $"{sessionId}.json");

    private class SessionWrapper
    {
        public SessionMetadata Metadata { get; set; } = new();
        public string Messages { get; set; } = "";
    }
}
