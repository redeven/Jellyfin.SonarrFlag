using System.Net;
using System.Text;
using System.Text.Json;
using JellyfinSonarrFlag.Sonarr;
using NSubstitute;

namespace JellyfinSonarrFlag.Tests.Helpers;

/// <summary>
/// An in-memory Sonarr API. Routes map "METHOD path" (relative to /api/v3/, with query) to a JSON-serializable
/// response; unrouted requests return 404. Every request is recorded.
/// </summary>
public sealed class FakeSonarr : HttpMessageHandler
{
    public const string Url = "http://sonarr.test:8989";
    public const string ApiKey = "secret-key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FakeSonarr()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(this, disposeHandler: false));
        Client = new SonarrClient(factory);
    }

    public SonarrClient Client { get; }

    public SonarrConnection Connection { get; } = new(Url, ApiKey);

    public Dictionary<string, Func<FakeRequest, HttpResponseMessage>> Routes { get; } = [];

    public List<FakeRequest> Requests { get; } = [];

    /// <summary>The recorded requests as "METHOD path" strings.</summary>
    public IEnumerable<string> Calls => Requests.Select(r => $"{r.Method} {r.Path}");

    public Exception? Throw { get; set; }

    public void Respond(string route, object body, HttpStatusCode status = HttpStatusCode.OK)
        => Routes[route] = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body as string ?? JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };

    public void Accept(params string[] routes)
    {
        foreach (var route in routes)
        {
            Respond(route, new { });
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Throw is not null)
        {
            throw Throw;
        }

        var uri = request.RequestUri!;
        const string ApiRoot = "/api/v3/";
        var root = uri.AbsolutePath.IndexOf(ApiRoot, StringComparison.Ordinal);
        var path = root < 0 ? uri.AbsolutePath : uri.AbsolutePath[(root + ApiRoot.Length)..];
        var recorded = new FakeRequest(
            request.Method.Method,
            path + uri.Query,
            uri,
            request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        Requests.Add(recorded);

        return Routes.TryGetValue($"{recorded.Method} {recorded.Path}", out var handler)
            ? handler(recorded)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

public sealed record FakeRequest(string Method, string Path, Uri Uri, string? ApiKey, string? Body)
{
    public JsonElement Json => JsonDocument.Parse(Body!).RootElement;
}
