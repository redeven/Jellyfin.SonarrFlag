using System.Net;
using JellyfinSonarrFlag.Sonarr;
using JellyfinSonarrFlag.Tests.Helpers;

namespace JellyfinSonarrFlag.Tests;

public sealed class SonarrClientTests : IDisposable
{
    private readonly FakeSonarr _sonarr = new();

    public void Dispose() => _sonarr.Dispose();

    [Fact]
    public async Task SendsTheApiKeyToTheV3Api()
    {
        _sonarr.Respond("GET system/status", new { version = "4.0.10.2544" });

        var status = await _sonarr.Client.GetSystemStatusAsync(_sonarr.Connection, CancellationToken.None);

        Assert.Equal("4.0.10.2544", status.Version);
        var request = Assert.Single(_sonarr.Requests);
        Assert.Equal(new Uri("http://sonarr.test:8989/api/v3/system/status"), request.Uri);
        Assert.Equal(FakeSonarr.ApiKey, request.ApiKey);
    }

    [Theory]
    [InlineData("http://sonarr.test:8989/", "http://sonarr.test:8989/api/v3/system/status")]
    [InlineData(" http://host/sonarr/ ", "http://host/sonarr/api/v3/system/status")]
    public async Task NormalizesTheBaseUrl(string url, string expected)
    {
        _sonarr.Respond("GET system/status", new { version = "4" });

        await _sonarr.Client.GetSystemStatusAsync(new SonarrConnection(url, "key"), CancellationToken.None);

        Assert.Equal(new Uri(expected), Assert.Single(_sonarr.Requests).Uri);
    }

    [Theory]
    [InlineData("sonarr:8989")]
    [InlineData("ftp://sonarr")]
    [InlineData("not a url")]
    public async Task InvalidUrl_Throws(string url)
    {
        var ex = await Assert.ThrowsAsync<SonarrException>(
            () => _sonarr.Client.GetSystemStatusAsync(new SonarrConnection(url, "key"), CancellationToken.None));

        Assert.Contains("http", ex.Message, StringComparison.Ordinal);
        Assert.Empty(_sonarr.Requests);
    }

    [Fact]
    public async Task ErrorStatus_ThrowsWithStatusAndBody()
    {
        _sonarr.Respond("DELETE episodefile/7", new { message = "File is locked" }, HttpStatusCode.Conflict);

        var ex = await Assert.ThrowsAsync<SonarrException>(
            () => _sonarr.Client.DeleteEpisodeFileAsync(_sonarr.Connection, 7, CancellationToken.None));

        Assert.Contains("409", ex.Message, StringComparison.Ordinal);
        Assert.Contains("File is locked", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeSonarr.ApiKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_Throws()
    {
        _sonarr.Respond("GET system/status", string.Empty, HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<SonarrException>(
            () => _sonarr.Client.GetSystemStatusAsync(_sonarr.Connection, CancellationToken.None));

        Assert.Contains("401", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachableSonarr_Throws()
    {
        _sonarr.Throw = new HttpRequestException("Connection refused");

        var ex = await Assert.ThrowsAsync<SonarrException>(
            () => _sonarr.Client.GetSystemStatusAsync(_sonarr.Connection, CancellationToken.None));

        Assert.Contains("Connection refused", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timeout_Throws()
    {
        _sonarr.Throw = new TaskCanceledException("Timed out");

        await Assert.ThrowsAsync<SonarrException>(
            () => _sonarr.Client.GetSystemStatusAsync(_sonarr.Connection, CancellationToken.None));
    }

    [Fact]
    public async Task NonJsonResponse_Throws()
    {
        _sonarr.Routes["GET system/status"] = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>") };

        var ex = await Assert.ThrowsAsync<SonarrException>(
            () => _sonarr.Client.GetSystemStatusAsync(_sonarr.Connection, CancellationToken.None));

        Assert.Contains("URL", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetMonitored_SendsEpisodeIds()
    {
        _sonarr.Accept("PUT episode/monitor");

        await _sonarr.Client.SetMonitoredAsync(_sonarr.Connection, [1, 2], true, CancellationToken.None);

        var body = Assert.Single(_sonarr.Requests).Json;
        Assert.Equal([1, 2], body.GetProperty("episodeIds").EnumerateArray().Select(e => e.GetInt32()));
        Assert.True(body.GetProperty("monitored").GetBoolean());
    }

    [Fact]
    public async Task SearchEpisodes_SendsEpisodeSearchCommand()
    {
        _sonarr.Accept("POST command");

        await _sonarr.Client.SearchEpisodesAsync(_sonarr.Connection, [5], CancellationToken.None);

        var body = Assert.Single(_sonarr.Requests).Json;
        Assert.Equal("EpisodeSearch", body.GetProperty("name").GetString());
        Assert.Equal([5], body.GetProperty("episodeIds").EnumerateArray().Select(e => e.GetInt32()));
    }
}
