using System.Net;
using System.Reflection;
using JellyfinSonarrFlag.Api;
using JellyfinSonarrFlag.Tests.Helpers;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace JellyfinSonarrFlag.Tests;

public sealed class SonarrFlagControllerTests : IDisposable
{
    private readonly PluginHarness _harness = new();
    private readonly FakeSonarr _sonarr = new();
    private readonly ILibraryManager _libraryManager = Substitute.For<ILibraryManager>();
    private readonly SonarrFlagController _controller;

    public SonarrFlagControllerTests()
    {
        _harness.Plugin.Configuration.SonarrUrl = FakeSonarr.Url;
        _harness.Plugin.Configuration.SonarrApiKey = FakeSonarr.ApiKey;

        var service = new BrokenEpisodeService(_libraryManager, _sonarr.Client, new ListLogger<BrokenEpisodeService>());
        _controller = new SonarrFlagController(service, _sonarr.Client, new ListLogger<SonarrFlagController>());
    }

    public void Dispose()
    {
        _sonarr.Dispose();
        _harness.Dispose();
    }

    [Fact]
    public void RequiresAnAdministrator()
    {
        var authorize = typeof(SonarrFlagController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(Policies.RequiresElevation, authorize?.Policy);
        Assert.DoesNotContain(typeof(SonarrFlagController).GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }

    [Fact]
    public async Task MarkBroken_ReturnsTheResult()
    {
        var episodeId = SetUpEpisode();

        var response = await _controller.MarkBroken(episodeId, CancellationToken.None);

        Assert.Equal("The Show S01E01", response.Value?.Episode);
    }

    [Fact]
    public async Task MarkBroken_ServiceErrors_KeepTheirStatusCode()
    {
        var response = await _controller.MarkBroken(Guid.NewGuid(), CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.IsType<ErrorResponse>(result.Value);
    }

    [Fact]
    public async Task MarkBroken_SonarrErrors_AreBadGateway()
    {
        var episodeId = SetUpEpisode();
        _sonarr.Respond("GET series", string.Empty, HttpStatusCode.Unauthorized);

        var response = await _controller.MarkBroken(episodeId, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Contains("401", Assert.IsType<ErrorResponse>(result.Value).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_UsesTheGivenConnectionNotTheSavedOne()
    {
        _sonarr.Respond("GET system/status", new { version = "4.0.10" });

        var response = await _controller.Test(new TestRequest("http://other.test/", "other-key"), CancellationToken.None);

        Assert.Equal(new TestResult(true, "4.0.10", null), response.Value);
        var request = Assert.Single(_sonarr.Requests);
        Assert.Equal("other.test", request.Uri.Host);
        Assert.Equal("other-key", request.ApiKey);
    }

    [Fact]
    public async Task Test_ReportsFailures()
    {
        _sonarr.Respond("GET system/status", string.Empty, HttpStatusCode.Unauthorized);

        var response = await _controller.Test(new TestRequest(FakeSonarr.Url, "wrong"), CancellationToken.None);

        Assert.False(response.Value?.Ok);
        Assert.Contains("401", response.Value?.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "key")]
    [InlineData("http://sonarr", " ")]
    public async Task Test_RequiresBothFields(string? url, string? apiKey)
    {
        var response = await _controller.Test(new TestRequest(url, apiKey), CancellationToken.None);

        Assert.False(response.Value?.Ok);
        Assert.Empty(_sonarr.Requests);
    }

    private Guid SetUpEpisode()
    {
        var seriesId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var series = new Series { Id = seriesId, Name = "The Show" };
        series.SetProviderId(MetadataProvider.Tvdb, "123");
        _libraryManager.GetItemById(seriesId).Returns(series);
        _libraryManager.GetItemById(episodeId).Returns(new Episode { Id = episodeId, SeriesId = seriesId, ParentIndexNumber = 1, IndexNumber = 1 });

        _sonarr.Respond("GET series", new[] { new { id = 2, title = "The Show", tvdbId = 123 } });
        _sonarr.Respond("GET episode?seriesId=2", new[] { new { id = 11, seasonNumber = 1, episodeNumber = 1, episodeFileId = 100, hasFile = true } });
        _sonarr.Respond("GET history/episode?episodeId=11", Array.Empty<object>());
        _sonarr.Accept("DELETE episodefile/100", "PUT episode/monitor", "POST command");
        return episodeId;
    }
}
