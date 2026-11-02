using System.Reflection;
using System.Runtime.Loader;

namespace JellyfinSonarrFlag;

public static class JavaScriptInjectorBridge
{
    private const string AssemblyName = "Jellyfin.Plugin.JavaScriptInjector";
    private const string InterfaceTypeName = "Jellyfin.Plugin.JavaScriptInjector.PluginInterface";

    public static bool IsAvailable => FindInterface() is not null;

    public static bool RegisterScript(string payloadJson)
    {
        var method = FindInterface()?.GetMethod("RegisterScript");
        var payloadType = method?.GetParameters().SingleOrDefault()?.ParameterType;
        var parse = payloadType?.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        if (method is null || parse is null)
        {
            return false;
        }

        var payload = parse.Invoke(null, [payloadJson]);
        return method.Invoke(null, [payload]) is true;
    }

    public static bool UnregisterScript(string scriptId)
        => FindInterface()?.GetMethod("UnregisterScript")?.Invoke(null, [scriptId]) is true;

    public static int UnregisterAllScripts(string pluginId)
        => FindInterface()?.GetMethod("UnregisterAllScriptsFromPlugin")?.Invoke(null, [pluginId]) as int? ?? 0;

    private static Type? FindInterface()
        => AssemblyLoadContext.All
            .SelectMany(c => c.Assemblies)
            .FirstOrDefault(a => a.FullName?.Contains(AssemblyName, StringComparison.Ordinal) ?? false)
            ?.GetType(InterfaceTypeName);
}
