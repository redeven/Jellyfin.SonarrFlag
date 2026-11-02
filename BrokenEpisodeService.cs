using JellyfinSonarrFlag.Sonarr;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JellyfinSonarrFlag;

public class BrokenEpisodeService
{
    private const string GrabbedEvent = "grabbed";
    private const string ImportedEvent = "downloadFolderImported";

    private readonly ILibraryManager _libraryManager;
    private readonly SonarrClient _sonarr;
    private readonly ILogger<BrokenEpisodeService> _logger;

    public BrokenEpisodeService(ILibraryManager libraryManager, SonarrClient sonarr, ILogger<BrokenEpisodeService> logger)
    {
        _libraryManager = libraryManager;
        _sonarr = sonarr;
        _logger = logger;
    }

    public async Task<MarkBrokenResult> MarkBrokenAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var connection = GetConnection();

        if (_libraryManager.GetItemById(itemId) is not Episode episode)
        {
            throw new BrokenEpisodeException(StatusCodes.Status400BadRequest, "Only episodes can be marked as broken.");
        }

        if (_libraryManager.GetItemById(episode.SeriesId) is not Series series)
        {
            throw new BrokenEpisodeException(StatusCodes.Status400BadRequest, $"\"{episode.Name}\" doesn't belong to a series.");
        }

        if (episode.ParentIndexNumber is not { } season || episode.IndexNumber is not { } first)
        {
            throw new BrokenEpisodeException(StatusCodes.Status400BadRequest, $"\"{episode.Name}\" has no season or episode number.");
        }

        var last = Math.Max(first, episode.IndexNumberEnd ?? first);
        var label = $"{series.Name} S{season:00}E{first:00}" + (last > first ? $"-E{last:00}" : string.Empty);

        var sonarrSeries = FindSeries(await _sonarr.GetSeriesAsync(connection, cancellationToken).ConfigureAwait(false), series)
            ?? throw new BrokenEpisodeException(StatusCodes.Status404NotFound, $"{series.Name} was not found in Sonarr.");

        var episodes = (await _sonarr.GetEpisodesAsync(connection, sonarrSeries.Id, cancellationToken).ConfigureAwait(false))
            .Where(e => e.SeasonNumber == season && e.EpisodeNumber >= first && e.EpisodeNumber <= last)
            .ToList();
        if (episodes.Count == 0)
        {
            throw new BrokenEpisodeException(StatusCodes.Status404NotFound, $"{label} was not found in Sonarr.");
        }

        var episodeIds = episodes.Select(e => e.Id).ToList();

        var grabIds = new HashSet<int>();
        foreach (var episodeId in episodeIds)
        {
            var history = await _sonarr.GetEpisodeHistoryAsync(connection, episodeId, cancellationToken).ConfigureAwait(false);
            if (FindImportedGrab(history) is { } grab)
            {
                grabIds.Add(grab.Id);
            }
        }

        foreach (var grabId in grabIds)
        {
            await _sonarr.MarkFailedAsync(connection, grabId, cancellationToken).ConfigureAwait(false);
        }

        var fileIds = episodes.Where(e => e.HasFile && e.EpisodeFileId > 0).Select(e => e.EpisodeFileId).Distinct().ToList();
        foreach (var fileId in fileIds)
        {
            await _sonarr.DeleteEpisodeFileAsync(connection, fileId, cancellationToken).ConfigureAwait(false);
        }

        // Deleting a file can unmonitor its episodes, which would stop the search from grabbing anything.
        await _sonarr.SetMonitoredAsync(connection, episodeIds, true, cancellationToken).ConfigureAwait(false);
        await _sonarr.SearchEpisodesAsync(connection, episodeIds, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Marked {Episode} as broken: blocklisted {Grabs} release(s), deleted {Files} file(s) and started a search in Sonarr.",
            label,
            grabIds.Count,
            fileIds.Count);

        return new MarkBrokenResult(
            label,
            grabIds.Count > 0,
            fileIds.Count > 0,
            grabIds.Count > 0 ? null : "No download was found in Sonarr's history, so no release was blocklisted.");
    }

    public static SonarrConnection? GetConfiguredConnection()
    {
        var config = Plugin.Instance?.Configuration;
        return config is null || string.IsNullOrWhiteSpace(config.SonarrUrl) || string.IsNullOrWhiteSpace(config.SonarrApiKey)
            ? null
            : new SonarrConnection(config.SonarrUrl, config.SonarrApiKey);
    }

    private static SonarrConnection GetConnection()
        => GetConfiguredConnection()
            ?? throw new BrokenEpisodeException(StatusCodes.Status400BadRequest, "Sonarr isn't configured. Set its URL and API key on the plugin's settings page.");

    private static SonarrSeries? FindSeries(List<SonarrSeries> candidates, Series series)
    {
        if (int.TryParse(series.GetProviderId(MetadataProvider.Tvdb), out var tvdbId) && tvdbId > 0
            && candidates.FirstOrDefault(s => s.TvdbId == tvdbId) is { } byTvdb)
        {
            return byTvdb;
        }

        if (series.GetProviderId(MetadataProvider.Imdb) is { Length: > 0 } imdbId
            && candidates.FirstOrDefault(s => string.Equals(s.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase)) is { } byImdb)
        {
            return byImdb;
        }

        if (int.TryParse(series.GetProviderId(MetadataProvider.Tmdb), out var tmdbId) && tmdbId > 0)
        {
            return candidates.FirstOrDefault(s => s.TmdbId == tmdbId);
        }

        return null;
    }

    /// <summary>The grab behind the most recent import, falling back to the most recent grab.</summary>
    private static SonarrHistoryRecord? FindImportedGrab(List<SonarrHistoryRecord> history)
    {
        var grabs = history
            .Where(r => string.Equals(r.EventType, GrabbedEvent, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Date)
            .ToList();

        var downloadId = history
            .Where(r => string.Equals(r.EventType, ImportedEvent, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(r.DownloadId))
            .OrderByDescending(r => r.Date)
            .FirstOrDefault()
            ?.DownloadId;

        return grabs.FirstOrDefault(g => downloadId is not null && string.Equals(g.DownloadId, downloadId, StringComparison.OrdinalIgnoreCase))
            ?? grabs.FirstOrDefault();
    }
}

public record MarkBrokenResult(string Episode, bool Blocklisted, bool FileDeleted, string? Warning);

public class BrokenEpisodeException : Exception
{
    public BrokenEpisodeException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
