using JellyfinSonarrFlag.Tests.Helpers;

namespace JellyfinSonarrFlag.Tests;

public sealed class PluginTests : IDisposable
{
    private readonly PluginHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void IdMatchesTheRepositoryManifest()
        => Assert.Equal(Guid.Parse("2ca81606-4649-40ac-9283-f61588982f64"), _harness.Plugin.Id);

    [Fact]
    public void ConstructingThePlugin_PublishesItAsTheInstance()
        => Assert.Same(_harness.Plugin, Plugin.Instance);

    [Fact]
    public void EmbeddedImage_Exists()
    {
        using var image = typeof(Plugin).Assembly.GetManifestResourceStream(_harness.Plugin.ImageResourceName);

        Assert.NotNull(image);
        Assert.True(image.Length > 0);
    }

    [Fact]
    public void ConfigurationPage_IsEmbedded()
    {
        var page = Assert.Single(_harness.Plugin.GetPages());
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath);

        Assert.NotNull(stream);
        var html = new StreamReader(stream).ReadToEnd();
        Assert.Contains(_harness.Plugin.Id.ToString(), html, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_RoundTripsThroughDisk()
    {
        _harness.Plugin.Configuration.SonarrUrl = "http://sonarr:8989";
        _harness.Plugin.Configuration.SonarrApiKey = "key";
        _harness.Plugin.SaveConfiguration();

        var reloaded = _harness.Restart().Configuration;

        Assert.Equal("http://sonarr:8989", reloaded.SonarrUrl);
        Assert.Equal("key", reloaded.SonarrApiKey);
    }

    [Fact]
    public void Uninstalling_RemovesAllOfThePluginsScripts()
    {
        using var injector = new FakeJavaScriptInjector();

        _harness.Plugin.OnUninstalling();

        Assert.Equal([$"UnregisterAll:{_harness.Plugin.Id}"], injector.Calls);
    }

    [Fact]
    public void Uninstalling_WithoutInjector_DoesNotThrow()
        => _harness.Plugin.OnUninstalling();
}
