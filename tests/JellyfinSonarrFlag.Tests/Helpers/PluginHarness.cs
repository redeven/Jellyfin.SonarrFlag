using System.Reflection;
using MediaBrowser.Common.Configuration;
using NSubstitute;

namespace JellyfinSonarrFlag.Tests.Helpers;

/// <summary>
/// Creates a real <see cref="Plugin"/> whose configuration lives in a temporary directory,
/// and clears the static <see cref="Plugin.Instance"/> again when disposed.
/// </summary>
public sealed class PluginHarness : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SonarrFlagTests", Guid.NewGuid().ToString("N"));
    private readonly IApplicationPaths _paths = Substitute.For<IApplicationPaths>();
    private readonly int _baselineWrites;

    public PluginHarness()
    {
        Directory.CreateDirectory(_root);
        _paths.PluginsPath.Returns(Path.Combine(_root, "plugins"));
        _paths.PluginConfigurationsPath.Returns(Path.Combine(_root, "plugins", "configurations"));

        Plugin = new Plugin(_paths, Serializer);
        _ = Plugin.Configuration;
        _baselineWrites = Serializer.FilesWritten;
    }

    public XmlFileSerializer Serializer { get; } = new();

    public Plugin Plugin { get; private set; }

    /// <summary>Number of times the configuration was written to disk since the harness was created.</summary>
    public int Saves => Serializer.FilesWritten - _baselineWrites;

    /// <summary>Simulates a server restart: a new plugin instance loads the configuration from disk.</summary>
    public Plugin Restart()
    {
        Plugin = new Plugin(_paths, new XmlFileSerializer());
        return Plugin;
    }

    public static void ClearInstance()
        => typeof(Plugin)
            .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);

    public void Dispose()
    {
        ClearInstance();
        Directory.Delete(_root, recursive: true);
    }
}
