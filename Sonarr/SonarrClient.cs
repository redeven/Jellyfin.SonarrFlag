using System.Net.Http.Json;
using System.Text.Json;
using MediaBrowser.Common.Net;

namespace JellyfinSonarrFlag.Sonarr;

public class SonarrClient
{
    private const int MaxErrorBodyLength = 300;

    private const int HistoryPageSize = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;

    public SonarrClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<SonarrSystemStatus> GetSystemStatusAsync(SonarrConnection connection, CancellationToken cancellationToken)
        => SendAsync<SonarrSystemStatus>(connection, HttpMethod.Get, "system/status", null, cancellationToken);

    public Task<List<SonarrSeries>> GetSeriesAsync(SonarrConnection connection, CancellationToken cancellationToken)
        => SendAsync<List<SonarrSeries>>(connection, HttpMethod.Get, "series", null, cancellationToken);

    public Task<List<SonarrEpisode>> GetEpisodesAsync(SonarrConnection connection, int seriesId, CancellationToken cancellationToken)
        => SendAsync<List<SonarrEpisode>>(connection, HttpMethod.Get, $"episode?seriesId={seriesId}", null, cancellationToken);

    public async Task<List<SonarrHistoryRecord>> GetEpisodeHistoryAsync(SonarrConnection connection, int episodeId, CancellationToken cancellationToken)
    {
        var page = await SendAsync<SonarrPage<SonarrHistoryRecord>>(connection, HttpMethod.Get, $"history?episodeId={episodeId}&pageSize={HistoryPageSize}", null, cancellationToken).ConfigureAwait(false);
        return page.Records.Where(r => r.EpisodeId == episodeId).ToList();
    }

    /// <summary>Marks a grab as failed, which blocklists its release.</summary>
    public Task MarkFailedAsync(SonarrConnection connection, int historyId, CancellationToken cancellationToken)
        => SendAsync(connection, HttpMethod.Post, $"history/failed/{historyId}", null, cancellationToken);

    public Task DeleteEpisodeFileAsync(SonarrConnection connection, int episodeFileId, CancellationToken cancellationToken)
        => SendAsync(connection, HttpMethod.Delete, $"episodefile/{episodeFileId}", null, cancellationToken);

    public Task SetMonitoredAsync(SonarrConnection connection, IReadOnlyList<int> episodeIds, bool monitored, CancellationToken cancellationToken)
        => SendAsync(connection, HttpMethod.Put, "episode/monitor", new { episodeIds, monitored }, cancellationToken);

    public Task SearchEpisodesAsync(SonarrConnection connection, IReadOnlyList<int> episodeIds, CancellationToken cancellationToken)
        => SendAsync(connection, HttpMethod.Post, "command", new { name = "EpisodeSearch", episodeIds }, cancellationToken);

    private async Task<T> SendAsync<T>(SonarrConnection connection, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(connection, method, path, body, cancellationToken).ConfigureAwait(false);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new SonarrException($"Sonarr returned an empty response for {method} {path}.");
        }
        catch (JsonException ex)
        {
            throw new SonarrException($"Sonarr returned an unexpected response for {method} {path}. Check the Sonarr URL.", ex);
        }
    }

    private async Task SendAsync(SonarrConnection connection, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(connection, method, path, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(SonarrConnection connection, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(connection, path));
        request.Headers.Add("X-Api-Key", connection.ApiKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new SonarrException($"Could not reach Sonarr: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (content.Length > MaxErrorBodyLength)
                {
                    content = content[..MaxErrorBodyLength] + "...";
                }

                throw new SonarrException(
                    $"Sonarr returned {(int)response.StatusCode} {response.ReasonPhrase} for {method} {path}."
                    + (string.IsNullOrWhiteSpace(content) ? string.Empty : $" {content}"));
            }
        }

        return response;
    }

    private static Uri BuildUri(SonarrConnection connection, string path)
    {
        if (!Uri.TryCreate(connection.Url.Trim().TrimEnd('/') + "/api/v3/" + path, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new SonarrException("The Sonarr URL must be an absolute http:// or https:// URL.");
        }

        return uri;
    }
}
