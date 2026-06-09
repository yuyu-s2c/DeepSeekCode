using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeepSeekCode.UI;

/// <summary>
/// 状态栏 ViewModel（MVVM 数据绑定）
/// </summary>
public class StatusViewModel : INotifyPropertyChanged
{
    private string _model = "deepseek-v4-pro";
    private int _tokenCount;
    private string _sessionTitle = "新对话";
    private string _status = "就绪";
    private bool _isStreaming;
    private int _cacheHitTokens;
    private int _cacheMissTokens;
    private int _reasoningTokens;
    private int _contextTokens;
    private const int MaxContext = 900_000;

    public string Model
    {
        get => _model;
        set { _model = value; OnPropertyChanged(); }
    }

    public int TokenCount
    {
        get => _tokenCount;
        set { _tokenCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(TokenDisplay)); }
    }

    public int ContextTokens
    {
        get => _contextTokens;
        set { _contextTokens = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContextDisplay)); }
    }

    public int CacheHitTokens
    {
        get => _cacheHitTokens;
        set { _cacheHitTokens = value; OnPropertyChanged(); OnPropertyChanged(nameof(TokenDisplay)); }
    }

    public int CacheMissTokens
    {
        get => _cacheMissTokens;
        set { _cacheMissTokens = value; OnPropertyChanged(); OnPropertyChanged(nameof(TokenDisplay)); }
    }

    public int ReasoningTokens
    {
        get => _reasoningTokens;
        set { _reasoningTokens = value; OnPropertyChanged(); }
    }

    public string SessionTitle
    {
        get => _sessionTitle;
        set { _sessionTitle = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set { _isStreaming = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusColor)); }
    }

    public string TokenDisplay => _cacheHitTokens > 0
        ? $"Token: {TokenCount:N0} | 🔥 命中: {CacheHitTokens:N0} ({CacheHitRatio:F0}%)"
        : $"Token: {TokenCount:N0}";

    /// <summary>缓存命中率（百分比）</summary>
    public double CacheHitRatio =>
        _cacheHitTokens + _cacheMissTokens > 0
            ? (double)_cacheHitTokens / (_cacheHitTokens + _cacheMissTokens) * 100
            : 0;

    public string CacheDisplay => _cacheHitTokens > 0
        ? $"🔥 缓存命中率: {CacheHitRatio:F0}% ({_cacheHitTokens:N0} / {_cacheHitTokens + _cacheMissTokens:N0})"
        : "";

    public string ContextDisplay
    {
        get
        {
            if (_contextTokens <= 0) return "";
            var pct = (double)_contextTokens / MaxContext * 100;
            var k = _contextTokens / 1000.0;
            return $"📐 ctx: {pct:F0}% ({k:F0}k / 1M)";
        }
    }

    public string StatusColor => IsStreaming ? "#4EC9B0" : "#808080";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
