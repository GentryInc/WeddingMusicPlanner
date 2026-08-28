using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Infrastructure;

namespace WeddingMusicSearch.Providers;

/// <summary>
/// Queries the Apple Music (MusicKit) catalog search endpoint for songs, mapping
/// Apple's genre taxonomy, artwork template and preview URL into the unified
/// <see cref="SearchResult"/>. Requires a signed MusicKit developer JWT; when no
/// token is configured the provider short-circuits so it never fails the aggregate.
/// </summary>
public sealed class AppleMusicSearchService : ISearchService
{
    private readonly HttpClient _http;
    private readonly AppleMusicOptions _options;
    private readonly RateLimiter _rateLimiter;

    public AppleMusicSearchService(HttpClient http, IOptions<AppleMusicOptions> options)
    {
        _options = options.Value;
        _http = http;
        _http.BaseAddress ??= new Uri(_options.BaseUrl);
        if (!string.IsNullOrWhiteSpace(_options.DeveloperToken) &&
            _http.DefaultRequestHeaders.Authorization is null)
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.DeveloperToken);
        }
        _rateLimiter = new RateLimiter(_options.RequestsPerInterval, _options.Interval);
    }

    public SearchSource Source => SearchSource.Apple;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        // No developer token -> Apple Music is simply not configured; skip gracefully.
        if (string.IsNullOrWhiteSpace(_options.DeveloperToken))
            return Array.Empty<SearchResult>();

        await _rateLimiter.WaitAsync(ct).ConfigureAwait(false);

        var term = BuildTerm(query);
        int limit = Math.Clamp(query.Limit, 1, 25); // Apple caps search page size at 25.
        var storefront = Uri.EscapeDataString(_options.Storefront);
        var url = $"catalog/{storefront}/search?term={Uri.EscapeDataString(term)}&types=songs&limit={limit}";

        var payload = await _http.GetFromJsonAsync<AppleSearchResponse>(url, ct).ConfigureAwait(false);
        var songs = payload?.Results?.Songs?.Data;
        if (songs is null || songs.Count == 0) return Array.Empty<SearchResult>();

        return songs.Select(s =>
        {
            var a = s.Attributes;
            return new SearchResult
            {
                Source = SearchSource.Apple,
                ExternalId = s.Id,
                Title = a?.Name ?? string.Empty,
                Artist = a?.ArtistName,
                Album = a?.AlbumName,
                Genres = a?.GenreNames?.Where(g => !string.IsNullOrWhiteSpace(g)).ToList()
                         ?? (IReadOnlyList<string>)Array.Empty<string>(),
                DurationMs = a?.DurationInMillis,
                ArtworkUrl = BuildArtworkUrl(a?.Artwork),
                Uri = a?.Url,
                Relevance = 0.9 // Apple returns curated relevance order but no numeric score.
            };
        }).ToList();
    }

    private static string BuildTerm(SearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Artist) || !string.IsNullOrWhiteSpace(query.Title))
            return string.Join(' ', new[] { query.Artist, query.Title }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
        return query.Text;
    }

    /// <summary>Apple artwork URLs are templates containing {w}/{h}; request a 500px square.</summary>
    private static string? BuildArtworkUrl(AppleArtwork? artwork)
    {
        if (string.IsNullOrWhiteSpace(artwork?.Url)) return null;
        return artwork!.Url!
            .Replace("{w}", "500", StringComparison.Ordinal)
            .Replace("{h}", "500", StringComparison.Ordinal);
    }

    // --- DTOs -------------------------------------------------------------

    private sealed class AppleSearchResponse
    {
        [JsonPropertyName("results")] public AppleResults? Results { get; set; }
    }

    private sealed class AppleResults
    {
        [JsonPropertyName("songs")] public AppleSongs? Songs { get; set; }
    }

    private sealed class AppleSongs
    {
        [JsonPropertyName("data")] public List<AppleSong>? Data { get; set; }
    }

    private sealed class AppleSong
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("attributes")] public AppleSongAttributes? Attributes { get; set; }
    }

    private sealed class AppleSongAttributes
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
        [JsonPropertyName("albumName")] public string? AlbumName { get; set; }
        [JsonPropertyName("genreNames")] public List<string>? GenreNames { get; set; }
        [JsonPropertyName("durationInMillis")] public long? DurationInMillis { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("artwork")] public AppleArtwork? Artwork { get; set; }
    }

    private sealed class AppleArtwork
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
