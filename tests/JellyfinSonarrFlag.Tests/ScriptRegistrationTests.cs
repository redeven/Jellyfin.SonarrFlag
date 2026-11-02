using System.Text.Json;
using JellyfinSonarrFlag.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace JellyfinSonarrFlag.Tests;

public sealed class ScriptRegistrationTests : IDisposable
{
    private readonly PluginHarness _harness = new();
    private readonly ListLogger<ScriptRegistration> _logger = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task RegistersTheEmbeddedScriptWithInjector()
    {
        using var injector = new FakeJavaScriptInjector();
        var plugin = _harness.Plugin;
        var scriptId = $"{plugin.Id}-mark-broken";

        await new ScriptRegistration(_logger).StartAsync(CancellationToken.None);

        Assert.Equal(2, injector.Calls.Count);
        Assert.Equal($"Unregister:{scriptId}", injector.Calls[0]);
        Assert.StartsWith("Register:", injector.Calls[1], StringComparison.Ordinal);

        using var payload = JsonDocument.Parse(injector.Calls[1]["Register:".Length..]);
        var root = payload.RootElement;
        Assert.Equal(scriptId, root.GetProperty("id").GetString());
        Assert.Equal("Mark as Broken", root.GetProperty("name").GetString());
        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.True(root.GetProperty("requiresAuthentication").GetBoolean());
        Assert.Equal(plugin.Id.ToString(), root.GetProperty("pluginId").GetString());
        Assert.Equal(plugin.Name, root.GetProperty("pluginName").GetString());
        Assert.Equal(plugin.Version.ToString(), root.GetProperty("pluginVersion").GetString());
        Assert.Equal(File.ReadAllText(RepositoryFile("Web", "markBroken.js")), root.GetProperty("script").GetString());

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Information);
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task RestartingRegistersAgainWithoutDuplicating()
    {
        using var injector = new FakeJavaScriptInjector();

        await new ScriptRegistration(_logger).StartAsync(CancellationToken.None);
        await new ScriptRegistration(_logger).StartAsync(CancellationToken.None);

        Assert.Equal(["Unregister", "Register", "Unregister", "Register"], injector.Calls.Select(c => c.Split(':')[0]));
    }

    [Fact]
    public async Task WithoutInjector_WarnsAndCarriesOn()
    {
        await new ScriptRegistration(_logger).StartAsync(CancellationToken.None);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("JavaScript Injector", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedRegistration_IsLoggedAsWarning()
    {
        using var injector = new FakeJavaScriptInjector { RegisterResult = false };

        await new ScriptRegistration(_logger).StartAsync(CancellationToken.None);

        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public async Task InjectorFailure_IsLoggedAndDoesNotBreakServerStartup()
    {
        using var injector = new FakeJavaScriptInjector { ThrowOnRegister = true };

        await new ScriptRegistration(_logger).StartAsync(CancellationToken.None);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.NotNull(entry.Exception);
    }

    [Fact]
    public async Task BeforeThePluginIsLoaded_DoesNothing()
    {
        using var injector = new FakeJavaScriptInjector();
        PluginHarness.ClearInstance();

        await new ScriptRegistration(_logger).StartAsync(CancellationToken.None);

        Assert.Empty(injector.Calls);
        Assert.Empty(_logger.Entries);
    }

    private static string RepositoryFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "JellyfinSonarrFlag.csproj")))
            {
                return Path.Combine([dir.FullName, .. parts]);
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
