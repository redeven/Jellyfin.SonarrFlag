using JellyfinSonarrFlag.Tests.Helpers;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace JellyfinSonarrFlag.Tests;

public sealed class BrokenEpisodeServiceTests : IDisposable
{
    private static readonly Guid SeriesId = Guid.NewGuid();
    private static readonly Guid EpisodeId = Guid.NewGuid();

    private readonly PluginHarness _harness = new();
    private readonly FakeSonarr _sonarr = new();
    private readonly ILibraryManager _libraryManager = Substitute.For<ILibraryManager>();
    private readonly ListLogger<BrokenEpisodeService> _logger = new();
    private readonly BrokenEpisodeService _service;
    private readonly Series _series = new() { Name = "The Show" };

    public BrokenEpisodeServiceTests()
    {
        _harness.Plugin.Configuration.SonarrUrl = FakeSonarr.Url;
        _harness.Plugin.Configuration.SonarrApiKey = FakeSonarr.ApiKey;

        _series.Id = SeriesId;
        _series.SetProviderId(MetadataProvider.Tvdb, "123");
        _libraryManager.GetItemById(SeriesId).Returns(_series);
        SetEpisode(season: 1, episode: 2);

        _sonarr.Respond("GET series", new[]
        {
            new { id = 1, title = "Other Show", tvdbId = 999, imdbId = "tt999", tmdbId = 999 },
            new { id = 2, title = "The Show", tvdbId = 123, imdbId = "tt123", tmdbId = 456 }
        });
        _sonarr.Respond("GET episode?seriesId=2", new[]
        {
            new { id = 10, seasonNumber = 1, episodeNumber = 1, episodeFileId = 99, hasFile = true, monitored = true },
            new { id = 11, seasonNumber = 1, episodeNumber = 2, episodeFileId = 100, hasFile = true, monitored = true },
            new { id = 12, seasonNumber = 1, episodeNumber = 3, episodeFileId = 100, hasFile = true, monitored = true },
            new { id = 13, seasonNumber = 2, episodeNumber = 2, episodeFileId = 101, hasFile = true, monitored = true }
        });
        _sonarr.RespondHistory(11, new object[]
        {
            History(50, 11, "grabbed", "2026-01-01", "A"),
            History(60, 11, "downloadFolderImported", "2026-01-02", "A"),
            History(51, 11, "grabbed", "2026-03-01", "B")
        });
        _sonarr.RespondHistory(12, new object[]
        {
            History(52, 12, "grabbed", "2026-01-01", "A"),
            History(61, 12, "downloadFolderImported", "2026-01-02", "A")
        });
        _sonarr.Accept("POST history/failed/50", "POST history/failed/52", "DELETE episodefile/100", "PUT episode/monitor", "POST command");

        _service = new BrokenEpisodeService(_libraryManager, _sonarr.Client, _logger);
    }

    public void Dispose()
    {
        _sonarr.Dispose();
        _harness.Dispose();
    }

    [Fact]
    public async Task BlocklistsDeletesMonitorsAndSearches_InThatOrder()
    {
        var result = await _service.MarkBrokenAsync(EpisodeId, CancellationToken.None);

        Assert.Equal(
            [
                "GET series",
                "GET episode?seriesId=2",
                "GET history?episodeId=11&pageSize=1000",
                "POST history/failed/50",
                "DELETE episodefile/100",
                "PUT episode/monitor",
                "POST command"
            ],
            _sonarr.Calls);
        Assert.Equal([11], _sonarr.Requests[5].Json.GetProperty("episodeIds").EnumerateArray().Select(e => e.GetInt32()));
        Assert.True(_sonarr.Requests[5].Json.GetProperty("monitored").GetBoolean());
        Assert.Equal([11], _sonarr.Requests[6].Json.GetProperty("episodeIds").EnumerateArray().Select(e => e.GetInt32()));
        Assert.All(_sonarr.Requests, r => Assert.Equal(FakeSonarr.ApiKey, r.ApiKey));

        Assert.Equal(new MarkBrokenResult("The Show S01E02", true, true, null), result);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("The Show S01E02", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BlocklistsTheGrabThatWasImported_NotALaterOne()
    {
        await _service.MarkBrokenAsync(EpisodeId, CancellationToken.None);

        Assert.Contains("POST history/failed/50", _sonarr.Calls);
        Assert.DoesNotContain("POST history/failed/51", _sonarr.Calls);
    }

    [Fact]
    public async Task WithoutAnImport_BlocklistsTheLatestGrab()
    {
        _sonarr.RespondHistory(11, new object[]
        {
            History(50, 11, "grabbed", "2026-01-01", "A"),
            History(51, 11, "grabbed", "2026-03-01", "B")
        });
        _sonarr.Accept("POST history/failed/51");

        await _service.MarkBrokenAsync(EpisodeId, CancellationToken.None);

        Assert.Equal(["POST history/failed/51"], _sonarr.Calls.Where(c => c.StartsWith("POST history", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MultiEpisodeFile_CoversEveryEpisodeAndDeletesTheFileOnce()
    {
        SetEpisode(season: 1, episode: 2, episodeEnd: 3);

        var result = await _service.MarkBrokenAsync(EpisodeId, CancellationToken.None);

        Assert.Equal("The Show S01E02-E03", result.Episode);
        Assert.Contains("POST history/failed/50", _sonarr.Calls);
        Assert.Contains("POST history/failed/52", _sonarr.Calls);
        Assert.Single(_sonarr.Calls, c => c == "DELETE episodefile/100");
        var search = _sonarr.Requests.Single(r => r.Path == "command").Json;
        Assert.Equal([11, 12], search.GetProperty("episodeIds").EnumerateArray().Select(e => e.GetInt32()));
    }

    [Fact]
    public async Task SharedGrab_IsOnlyBlocklistedOnce()
    {
        SetEpisode(season: 1, episode: 2, episodeEnd: 3);
        _sonarr.RespondHistory(12, new object[]
        {
            History(50, 12, "grabbed", "2026-01-01", "A"),
            History(61, 12, "downloadFolderImported", "2026-01-02", "A")
        });

        await _service.MarkBrokenAsync(EpisodeId, CancellationToken.None);

        Assert.Single(_sonarr.Calls, c => c.StartsWith("POST history/failed/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutHistory_StillDeletesAndSearches_AndWarns()
    {
        _sonarr.RespondHistory(11, Array.Empty<object>());

        var result = await _service.MarkBrokenAsync(EpisodeId, CancellationToken.None);

        Assert.DoesNotContain(_sonarr.Calls, c => c.StartsWith("POST history/failed/", StringComparison.Ordinal));
        Assert.Contains("DELETE episodefile/100", _sonarr.Calls);
        Assert.Contains("POST command", _sonarr.Calls);
        Assert.False(result.Blocklisted);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task EpisodeWithoutFile_SkipsDeletion()
    {
        _sonarr.Respond("GET episode?seriesId=2", new[]
        {
            new { id = 11, seasonNumber = 1, episodeNumber = 2, episodeFileId = 0, hasFile = false, monitored = false }
        });

        var result = await _service.MarkBrokenAsync(EpisodeId, CancellationToken.None);

        Assert.DoesNotContain(_sonarr.Calls, c => c.StartsWith("DELETE", StringComparison.Ordinal));
        Assert.Contains("PUT episode/monitor", _sonarr.Calls);
        Assert.False(result.FileDeleted);
    }

    [Theory]
    [InlineData(MetadataProvider.Imdb, "tt123")]
    [InlineData(MetadataProvider.Tmdb, "456")]
    public async Task MatchesSeriesByOtherProviderIds(MetadataProvider provider, string id)
    {
        _series.ProviderIds.Clear();
        _series.SetProviderId(provider, id);

        await _service.MarkBrokenAsync(EpisodeId, CancellationToken.None);

        Assert.Contains("GET episode?seriesId=2", _sonarr.Calls);
    }

    [Fact]
    public async Task NotConfigured_IsBadRequest()
    {
        _harness.Plugin.Configuration.SonarrApiKey = string.Empty;

        var ex = await Assert.ThrowsAsync<BrokenEpisodeException>(() => _service.MarkBrokenAsync(EpisodeId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Empty(_sonarr.Requests);
    }

    [Fact]
    public async Task NotAnEpisode_IsBadRequest()
    {
        var movieId = Guid.NewGuid();
        _libraryManager.GetItemById(movieId).Returns(new Movie { Id = movieId });

        var ex = await Assert.ThrowsAsync<BrokenEpisodeException>(() => _service.MarkBrokenAsync(movieId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Empty(_sonarr.Requests);
    }

    [Fact]
    public async Task EpisodeWithoutNumbers_IsBadRequest()
    {
        _libraryManager.GetItemById(EpisodeId).Returns(new Episode { Id = EpisodeId, SeriesId = SeriesId, Name = "Pilot" });

        var ex = await Assert.ThrowsAsync<BrokenEpisodeException>(() => _service.MarkBrokenAsync(EpisodeId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task SeriesMissingFromSonarr_IsNotFound()
    {
        _series.SetProviderId(MetadataProvider.Tvdb, "777");

        var ex = await Assert.ThrowsAsync<BrokenEpisodeException>(() => _service.MarkBrokenAsync(EpisodeId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
        Assert.Contains("The Show", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EpisodeMissingFromSonarr_IsNotFound()
    {
        SetEpisode(season: 5, episode: 1);

        var ex = await Assert.ThrowsAsync<BrokenEpisodeException>(() => _service.MarkBrokenAsync(EpisodeId, CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
        Assert.Contains("S05E01", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_sonarr.Calls, c => !c.StartsWith("GET", StringComparison.Ordinal));
    }

    private void SetEpisode(int season, int episode, int? episodeEnd = null)
        => _libraryManager.GetItemById(EpisodeId).Returns(new Episode
        {
            Id = EpisodeId,
            SeriesId = SeriesId,
            Name = "An Episode",
            ParentIndexNumber = season,
            IndexNumber = episode,
            IndexNumberEnd = episodeEnd
        });

    private static object History(int id, int episodeId, string eventType, string date, string downloadId)
        => new { id, episodeId, eventType, date = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture), downloadId };
}
