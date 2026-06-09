using DeepSeekCode.Services;

namespace DeepSeekCode.Tests.Services;

public class ServiceLocatorTests : IDisposable
{
    private readonly ServiceLocator _locator = new();

    [Fact]
    public void RegisterInstance_Resolves_SameInstance()
    {
        var svc = new TestService();
        _locator.RegisterInstance<ITestService>(svc);
        var resolved = _locator.Resolve<ITestService>();

        Assert.Same(svc, resolved);
    }

    [Fact]
    public void RegisterSingleton_CreatesAndResolves()
    {
        _locator.RegisterSingleton<ITestService, TestService>();
        var resolved = _locator.Resolve<ITestService>();

        Assert.NotNull(resolved);
    }

    [Fact]
    public void RegisterTransient_CreatesNewInstances_EachTime()
    {
        _locator.RegisterTransient<ITestService, TestService>();
        var a = _locator.Resolve<ITestService>();
        var b = _locator.Resolve<ITestService>();

        Assert.NotSame(a, b);
    }

    [Fact]
    public void Resolve_Throws_WhenNotRegistered()
    {
        Assert.Throws<InvalidOperationException>(() => _locator.Resolve<ITestService>());
    }

    [Fact]
    public void TryResolve_ReturnsNull_WhenNotRegistered()
    {
        Assert.Null(_locator.TryResolve<ITestService>());
    }

    [Fact]
    public void IsRegistered_ReturnsCorrectStatus()
    {
        Assert.False(_locator.IsRegistered<ITestService>());
        _locator.RegisterInstance<ITestService>(new TestService());
        Assert.True(_locator.IsRegistered<ITestService>());
    }

    [Fact]
    public void RegisterFactory_WithCircularDependency_Throws()
    {
        var loc = new ServiceLocator();
        loc.RegisterFactory<ICircularA>(l => new CircularA(l));
        loc.RegisterFactory<ICircularB>(l => new CircularB(l));

        var ex = Assert.Throws<InvalidOperationException>(() => loc.Resolve<ICircularA>());
        Assert.Contains("循环依赖", ex.Message);
    }

    [Fact]
    public void Dispose_DisposesAllRegisteredDisposables()
    {
        var disposable = new DisposableService();
        _locator.RegisterInstance<IDisposableService>(disposable);

        _locator.Dispose();

        Assert.True(disposable.Disposed);
    }

    [Fact]
    public void Resolve_AfterDispose_Throws()
    {
        _locator.RegisterInstance<ITestService>(new TestService());
        _locator.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _locator.Resolve<ITestService>());
    }

    public void Dispose() => _locator.Dispose();

    // ── Test types ──

    public interface ITestService { }
    public class TestService : ITestService { }

    public interface IDisposableService : IDisposable { bool Disposed { get; } }
    public class DisposableService : IDisposableService
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    public interface ICircularA { }
    public interface ICircularB { }
    public class CircularA : ICircularA
    {
        public CircularA(ServiceLocator locator) => locator.Resolve<ICircularB>();
    }
    public class CircularB : ICircularB
    {
        public CircularB(ServiceLocator locator) => locator.Resolve<ICircularA>();
    }
}
