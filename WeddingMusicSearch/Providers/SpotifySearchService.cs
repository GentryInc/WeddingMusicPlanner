using Microsoft.Extensions.Options;
using SpotifyAPI.Web;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Infrastructure;

namespace WeddingMusicSearch.Providers;

/// <summary>
/// Queries the Spotify catalogue using SpotifyAPI-NET. Uses the client-credentials
/// OAuth flow (no user login) and caches/refreshes the bearer token automatically.
/// </summary>
public sealed class SpotifySearchService : ISearchService, IDisposable
{
    private readonly SpotifyOptions _options;
    private readonly RateLimiter _rateLimiter;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    private SpotifyClient? _client;
    private DateTimeOffset _tokenExpiryUtc = DateTimeOffset.MinValue;

    public SpotifySearchService(IOptions<SpotifyOptions> options)
    {
        _options = options.Value;
        // Spotify's limits are generous but bursty; smooth to ~10 req/s.
        _rateLimiter = new RateLimiter(10, TimeSpan.FromSeconds(1));
    }

    public SearchSource Source => SearchSource.Spotify;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct).ConfigureAwait(false);
        await _rateLimiter.WaitAsync(ct).ConfigureAwait(false);

        // NOTE: Spotify development-mode apps now reject search limits above 10
        // with HTTP 400 "Invalid limit" (verified against the live API), even
        // though the documented maximum is 50. Clamp to the working range.
        var request = new SearchRequest(SearchRequest.Types.Track, BuildQuery(query))
        {
            Limit = Math.Clamp(query.Limit, 1, 10)
        };

        var response = await client.Search.Item(request, ct).ConfigureAwait(false);
        var tracks = response.Tracks?.Items ?? new List<FullTrack>();

        // NOTE: Spotify's /audio-features endpoint was deprecated (2024-11-27) and is
        // no longer available to apps, so Energy/Danceability are left null here.
        // Local library tracks still supply these from their own analysis.
        return tracks.Select(t =>
        {
            return new SearchResult
            {
                Source = SearchSource.Spotify,
                ExternalId = t.Uri,
                Title = t.Name,
                Artist = string.Join(", ", t.Artists.Select(a => a.Name)),
                Album = t.Album?.Name,
                DurationMs = t.DurationMs,
                ArtworkUrl = t.Album?.Images?.OrderByDescending(i => i.Width).FirstOrDefault()?.Url,
                Uri = t.ExternalUrls?.TryGetValue("spotify", out var url) == true ? url : t.Uri,
                Relevance = t.Popularity / 100.0
            };
        }).ToList();
    }

    private static string BuildQuery(SearchQuery query)
    {
        // Prefer field filters when we have structured artist/title.
        if (!string.IsNullOrWhiteSpace(query.Artist) || !string.IsNullOrWhiteSpace(query.Title))
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(query.Title)) parts.Add($"track:\"{query.Title}\"");
            if (!string.IsNullOrWhiteSpace(query.Artist)) parts.Add($"artist:\"{query.Artist}\"");
            return string.Join(' ', parts);
        }
        return query.Text;
    }

    private async Task<SpotifyClient> GetClientAsync(CancellationToken ct)
    {
        if (_client is not null && DateTimeOffset.UtcNow < _tokenExpiryUtc)
            return _client;

        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is not null && DateTimeOffset.UtcNow < _tokenExpiryUtc)
                return _client;

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
                throw new InvalidOperationException("Spotify ClientId/ClientSecret are not configured.");

            // A retry handler that honours the Retry-After header and backs off on
            // HTTP 429 / transient 5xx, per Spotify's rate-limit guidance. This avoids
            // tight retry loops on the SpotifyAPI-NET calls (which bypass the Polly
            // pipeline used by the HttpClient-based providers).
            var config = SpotifyClientConfig.CreateDefault()
                .WithRetryHandler(new SimpleRetryHandler
                {
                    RetryAfter = TimeSpan.FromSeconds(1),
                    TooManyRequestsConsumesARetry = true,
                    RetryTimes = 4
                });

            var tokenResponse = await new OAuthClient(config)
                .RequestToken(new ClientCredentialsRequest(_options.ClientId, _options.ClientSecret))
                .ConfigureAwait(false);

            // Refresh a minute before actual expiry to avoid mid-flight 401s.
            _tokenExpiryUtc = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
            _client = new SpotifyClient(config.WithToken(tokenResponse.AccessToken));
            return _client;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
        _tokenGate.Dispose();
    }
}
