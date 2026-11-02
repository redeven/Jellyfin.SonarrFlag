namespace JellyfinSonarrFlag.Sonarr;

public record SonarrConnection(string Url, string ApiKey);

public record SonarrSystemStatus(string Version);

public record SonarrSeries(int Id, string Title, int TvdbId, string? ImdbId, int TmdbId);

public record SonarrEpisode(int Id, int SeasonNumber, int EpisodeNumber, int EpisodeFileId, bool HasFile, bool Monitored);

public record SonarrHistoryRecord(int Id, int EpisodeId, string EventType, DateTime Date, string? DownloadId);

public class SonarrException : Exception
{
    public SonarrException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
