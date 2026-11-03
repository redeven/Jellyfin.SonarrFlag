using MediaBrowser.Model.Plugins;

namespace JellyfinSonarrFlag.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string SonarrUrl { get; set; } = string.Empty;

    public string SonarrApiKey { get; set; } = string.Empty;

    /// <summary>Non-administrators who may mark episodes as broken. Administrators always may.</summary>
    public Guid[] AllowedUserIds { get; set; } = [];
}
