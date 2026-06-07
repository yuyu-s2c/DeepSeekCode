namespace DeepSeekCode.Services;

/// <summary>
/// 轻量级服务定位器 / DI 容器。
/// 避免引入第三方 DI 框架，用最简单的注册-解析模式管理服务生命周期。
/// </summary>
public class ServiceLocator
{
    private readonly Dictionary<Type, Func<ServiceLocator, object>> _factories = new();
    private readonly Dictionary<Type, object> _instances = new();
    private readonly Dictionary<Type, Lazy<object>> _lazySingletons = new();

    // ── 注册 ──

    /// <summary>注册单例（立即实例化）</summary>
    public ServiceLocator RegisterSingleton<TInterface, TImpl>() where TImpl : TInterface, new()
    {
        var instance = Activator.CreateInstance<TImpl>()!;
        _instances[typeof(TInterface)] = instance;
        return this;
    }

    /// <summary>注册单例（延迟实例化，首次 Resolve 时创建）</summary>
    public ServiceLocator RegisterLazySingleton<TInterface, TImpl>() where TImpl : TInterface, new()
    {
        _lazySingletons[typeof(TInterface)] = new Lazy<object>(() => new TImpl());
        return this;
    }

    /// <summary>注册单例（已有实例）</summary>
    public ServiceLocator RegisterInstance<TInterface>(TInterface instance)
    {
        _instances[typeof(TInterface)] = instance!;
        return this;
    }

    /// <summary>注册工厂（每次 Resolve 创建新实例）</summary>
    public ServiceLocator RegisterTransient<TInterface, TImpl>() where TImpl : TInterface, new()
    {
        _factories[typeof(TInterface)] = _ => new TImpl();
        return this;
    }

    // ── 解析 ──

    /// <summary>解析服务</summary>
    public T Resolve<T>() where T : class
    {
        return (T)Resolve(typeof(T));
    }

    /// <summary>解析服务（按类型）</summary>
    public object Resolve(Type type)
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

    /// <summary>尝试解析，返回 null 如果未注册</summary>
    public T? TryResolve<T>() where T : class
    {
        try { return Resolve<T>(); }
        catch (InvalidOperationException)
        {
            // 仅捕获"服务未注册"，其余异常（如构造函数异常）继续传播
            return null;
        }
    }

    /// <summary>检查服务是否已注册</summary>
    public bool IsRegistered<T>()
    {
        return _instances.ContainsKey(typeof(T)) ||
               _lazySingletons.ContainsKey(typeof(T)) ||
               _factories.ContainsKey(typeof(T));
    }
}
