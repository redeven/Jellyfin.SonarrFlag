using JellyfinSonarrFlag.Sonarr;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyfinSonarrFlag;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SonarrClient>();
        serviceCollection.AddSingleton<BrokenEpisodeService>();
        serviceCollection.AddHostedService<ScriptRegistration>();
    }
}
