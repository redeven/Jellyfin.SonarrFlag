using JellyfinSonarrFlag.Api;
using JellyfinSonarrFlag.Sonarr;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace JellyfinSonarrFlag.Tests;

public class PluginServiceRegistratorTests
{
    private readonly ServiceProvider _services;

    public PluginServiceRegistratorTests()
    {
        var collection = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Substitute.For<ILibraryManager>())
            .AddSingleton(Substitute.For<IHttpClientFactory>());
        new PluginServiceRegistrator().RegisterServices(collection, Substitute.For<IServerApplicationHost>());
        _services = collection.BuildServiceProvider();
    }

    [Fact]
    public void Services_AreSingletons()
    {
        Assert.Same(_services.GetRequiredService<SonarrClient>(), _services.GetRequiredService<SonarrClient>());
        Assert.Same(_services.GetRequiredService<BrokenEpisodeService>(), _services.GetRequiredService<BrokenEpisodeService>());
    }

    [Fact]
    public void ScriptRegistration_RunsAsAHostedService()
        => Assert.Contains(_services.GetServices<IHostedService>(), s => s is ScriptRegistration);

    [Fact]
    public void Controller_IsResolvable()
        => Assert.IsType<SonarrFlagController>(ActivatorUtilities.CreateInstance<SonarrFlagController>(_services));
}
