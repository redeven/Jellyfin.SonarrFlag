using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JellyfinSonarrFlag.Tests.Helpers;

/// <summary>
/// Compiles and loads a stand-in for the JavaScript Injector plugin assembly, so the reflection-based
/// <see cref="JavaScriptInjectorBridge"/> is exercised for real. Disposing unloads it again.
/// </summary>
public sealed class FakeJavaScriptInjector : IDisposable
{
    /// <summary>Mirrors the shape of the real plugin: RegisterScript takes a type with a static Parse(string).</summary>
    public const string FullApi = """
        namespace Jellyfin.Plugin.JavaScriptInjector;

        public sealed class ScriptPayload
        {
            public string Json { get; private set; } = "";

            public static ScriptPayload Parse(string json) => new ScriptPayload { Json = json };
        }

        public static class PluginInterface
        {
            public static System.Collections.Generic.List<string> Calls = new();
            public static bool RegisterResult = true;
            public static bool ThrowOnRegister;

            public static bool RegisterScript(ScriptPayload payload)
            {
                if (ThrowOnRegister)
                {
                    throw new System.InvalidOperationException("Injector failure");
                }

                Calls.Add("Register:" + payload.Json);
                return RegisterResult;
            }

            public static bool UnregisterScript(string scriptId)
            {
                Calls.Add("Unregister:" + scriptId);
                return true;
            }

            public static int UnregisterAllScriptsFromPlugin(string pluginId)
            {
                Calls.Add("UnregisterAll:" + pluginId);
                return 3;
            }
        }
        """;

    /// <summary>An injector build that exposes none of the methods the bridge relies on.</summary>
    public const string EmptyApi = """
        namespace Jellyfin.Plugin.JavaScriptInjector;

        public static class PluginInterface
        {
        }
        """;

    private readonly AssemblyLoadContext _context = new("FakeJavaScriptInjector", isCollectible: true);
    private readonly Type _interface;

    public FakeJavaScriptInjector(string source = FullApi)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "Jellyfin.Plugin.JavaScriptInjector",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
        }

        image.Position = 0;
        _interface = _context.LoadFromStream(image).GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface")!;
    }

    public IReadOnlyList<string> Calls => (List<string>?)Field("Calls")?.GetValue(null) ?? [];

    public bool RegisterResult
    {
        set => Field("RegisterResult")!.SetValue(null, value);
    }

    public bool ThrowOnRegister
    {
        set => Field("ThrowOnRegister")!.SetValue(null, value);
    }

    private FieldInfo? Field(string name) => _interface.GetField(name, BindingFlags.Public | BindingFlags.Static);

    public void Dispose() => _context.Unload();
}
