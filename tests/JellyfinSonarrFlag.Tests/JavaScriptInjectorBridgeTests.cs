using JellyfinSonarrFlag.Tests.Helpers;

namespace JellyfinSonarrFlag.Tests;

public class JavaScriptInjectorBridgeTests
{
    [Fact]
    public void WithoutInjector_EverythingIsANoOp()
    {
        Assert.False(JavaScriptInjectorBridge.IsAvailable);
        Assert.False(JavaScriptInjectorBridge.RegisterScript("{}"));
        Assert.False(JavaScriptInjectorBridge.UnregisterScript("id"));
        Assert.Equal(0, JavaScriptInjectorBridge.UnregisterAllScripts("plugin"));
    }

    [Fact]
    public void WithInjector_CallsAreForwarded()
    {
        using var injector = new FakeJavaScriptInjector();

        Assert.True(JavaScriptInjectorBridge.IsAvailable);
        Assert.True(JavaScriptInjectorBridge.RegisterScript("""{"id":"x"}"""));
        Assert.True(JavaScriptInjectorBridge.UnregisterScript("x"));
        Assert.Equal(3, JavaScriptInjectorBridge.UnregisterAllScripts("plugin"));
        Assert.Equal(["Register:{\"id\":\"x\"}", "Unregister:x", "UnregisterAll:plugin"], injector.Calls);
    }

    [Fact]
    public void RegisterScript_ReportsRejection()
    {
        using var injector = new FakeJavaScriptInjector { RegisterResult = false };

        Assert.False(JavaScriptInjectorBridge.RegisterScript("{}"));
        Assert.Single(injector.Calls);
    }

    [Fact]
    public void InjectorWithoutTheExpectedApi_IsTreatedAsUnsupported()
    {
        using var injector = new FakeJavaScriptInjector(FakeJavaScriptInjector.EmptyApi);

        Assert.True(JavaScriptInjectorBridge.IsAvailable);
        Assert.False(JavaScriptInjectorBridge.RegisterScript("{}"));
        Assert.False(JavaScriptInjectorBridge.UnregisterScript("id"));
        Assert.Equal(0, JavaScriptInjectorBridge.UnregisterAllScripts("plugin"));
    }

    [Fact]
    public void UnloadedInjector_IsNoLongerFound()
    {
        new FakeJavaScriptInjector().Dispose();

        Assert.False(JavaScriptInjectorBridge.IsAvailable);
    }
}
