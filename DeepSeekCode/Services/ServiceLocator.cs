namespace DeepSeekCode.Services;

/// <summary>
/// 轻量级服务定位器 / DI 容器。
/// 支持单例/延迟单例/瞬态注册、循环依赖检测、IDisposable 生命周期管理。
/// </summary>
public class ServiceLocator : IDisposable
{
    private readonly Dictionary<Type, Func<ServiceLocator, object>> _factories = new();
    private readonly Dictionary<Type, object> _instances = new();
    private readonly Dictionary<Type, Lazy<object>> _lazySingletons = new();
    private readonly List<IDisposable> _disposables = new();
    private readonly HashSet<Type> _resolvingStack = new();
    private bool _disposed;

    // ── 注册 ──

    /// <summary>注册单例（立即实例化）</summary>
    public ServiceLocator RegisterSingleton<TInterface, TImpl>() where TImpl : TInterface, new()
    {
        var instance = Activator.CreateInstance<TImpl>()!;
        _instances[typeof(TInterface)] = instance;
        TrackDisposable(instance);
        return this;
    }

    /// <summary>注册单例（延迟实例化，首次 Resolve 时创建）</summary>
    public ServiceLocator RegisterLazySingleton<TInterface, TImpl>() where TImpl : TInterface, new()
    {
        _lazySingletons[typeof(TInterface)] = new Lazy<object>(() =>
        {
            var instance = new TImpl();
            TrackDisposable(instance);
            return instance;
        });
        return this;
    }

    /// <summary>注册单例（已有实例）</summary>
    public ServiceLocator RegisterInstance<TInterface>(TInterface instance)
    {
        _instances[typeof(TInterface)] = instance!;
        TrackDisposable(instance);
        return this;
    }

    /// <summary>注册工厂（每次 Resolve 创建新实例）</summary>
    public ServiceLocator RegisterTransient<TInterface, TImpl>() where TImpl : TInterface, new()
    {
        _factories[typeof(TInterface)] = _ => new TImpl();
        return this;
    }

    /// <summary>注册工厂（自定义创建函数，每次 Resolve 创建新实例）</summary>
    public ServiceLocator RegisterFactory<TInterface>(Func<ServiceLocator, TInterface> factory)
    {
        _factories[typeof(TInterface)] = loc => factory(loc)!;
        return this;
    }

    // ── 解析 ──

    /// <summary>解析服务</summary>
    public T Resolve<T>() where T : class
    {
        return (T)Resolve(typeof(T));
    }

    /// <summary>解析服务（按类型），含循环依赖检测</summary>
    public object Resolve(Type type)
    {
        if (_disposing || _disposed)
            throw new ObjectDisposedException(nameof(ServiceLocator), "容器已销毁");

        // 循环依赖检测 — 在任何查找之前先检查
        if (!_resolvingStack.Add(type))
        {
            var chain = string.Join(" → ", _resolvingStack.Select(t => t.Name)) + $" → {type.Name}";
            throw new InvalidOperationException($"检测到循环依赖: {chain}");
        }

        try
        {
            // 1. 已实例化的单例
            if (_instances.TryGetValue(type, out var instance))
                return instance;

            // 2. 延迟单例
            if (_lazySingletons.TryGetValue(type, out var lazy))
            {
                var obj = lazy.Value;
                _instances[type] = obj;
                _lazySingletons.Remove(type);
                return obj;
            }

            // 3. 工厂（每次新实例）
            if (_factories.TryGetValue(type, out var factory))
                return factory(this);

            throw new InvalidOperationException($"服务未注册: {type.Name}");
        }
        finally
        {
            _resolvingStack.Remove(type);
        }
    }

    /// <summary>尝试解析，返回 null 如果未注册</summary>
    public T? TryResolve<T>() where T : class
    {
        try { return Resolve<T>(); }
        catch (Exception) { return null; }
    }

    /// <summary>检查服务是否已注册</summary>
    public bool IsRegistered<T>()
    {
        return _instances.ContainsKey(typeof(T)) ||
               _lazySingletons.ContainsKey(typeof(T)) ||
               _factories.ContainsKey(typeof(T));
    }

    // ── 生命周期 ──

    private void TrackDisposable(object? instance)
    {
        if (instance is IDisposable d && !ReferenceEquals(instance, this))
            _disposables.Add(d);
    }

    private bool _disposing;

    /// <summary>
    /// 释放所有已注册的 IDisposable 单例（按注册的逆序）
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposing = true;

        for (var i = _disposables.Count - 1; i >= 0; i--)
        {
            try { _disposables[i].Dispose(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ServiceLocator] Dispose 异常: {ex.Message}");
            }
        }

        _disposables.Clear();
        _instances.Clear();
        _lazySingletons.Clear();
        _factories.Clear();
        _resolvingStack.Clear();
        _disposed = true;
    }
}
