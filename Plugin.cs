using JellyfinSonarrFlag.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinSonarrFlag;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasEmbeddedImage
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Sonarr Flag";

    public override string Description => "Mark broken episodes so Sonarr replaces them.";

    public override Guid Id => Guid.Parse("2ca81606-4649-40ac-9283-f61588982f64");

    public string ImageResourceName => "JellyfinSonarrFlag.Images.sonarr-flag.png";

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = "SonarrFlag",
            EmbeddedResourcePath = "JellyfinSonarrFlag.Web.configPage.html"
        }
    ];

    public override void OnUninstalling()
    {
        JavaScriptInjectorBridge.UnregisterAllScripts(Id.ToString());
        base.OnUninstalling();
    }
}
