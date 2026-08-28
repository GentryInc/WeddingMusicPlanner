using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Infrastructure;

namespace WeddingMusicSearch.Providers;

/// <summary>
/// Queries the Discogs database for releases, surfacing its rich genre/style
/// taxonomy and high-resolution cover art. Authenticates with a personal access
/// token when supplied (raises the rate limit to ~60 req/min).
/// </summary>
public sealed class DiscogsSearchService : ISearchService
{
    private readonly HttpClient _http;
    private readonly RateLimiter _rateLimiter;

    public DiscogsSearchService(HttpClient http, IOptions<DiscogsOptions> options)
    {
        var o = options.Value;
        _http = http;
        _http.BaseAddress ??= new Uri(o.BaseUrl);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(o.UserAgent);
        if (!string.IsNullOrWhiteSpace(o.PersonalAccessToken) &&
            _http.DefaultRequestHeaders.Authorization is null)
        {
            _http.DefaultRequestHeaders.Add("Authorization", $"Discogs token={o.PersonalAccessToken}");
        }
        _rateLimiter = new RateLimiter(o.RequestsPerInterval, o.Interval);
    }

    public SearchSource Source => SearchSource.Discogs;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        await _rateLimiter.WaitAsync(ct).ConfigureAwait(false);

        var qs = new List<string> { "type=release", $"per_page={Math.Clamp(query.Limit, 1, 100)}" };
        if (!string.IsNullOrWhiteSpace(query.Artist)) qs.Add($"artist={Uri.EscapeDataString(query.Artist)}");
        if (!string.IsNullOrWhiteSpace(query.Title)) qs.Add($"release_title={Uri.EscapeDataString(query.Title)}");
        if (string.IsNullOrWhiteSpace(query.Artist) && string.IsNullOrWhiteSpace(query.Title))
            qs.Add($"q={Uri.EscapeDataString(query.Text)}");

        var url = $"database/search?{string.Join('&', qs)}";
        var payload = await _http.GetFromJsonAsync<DiscogsSearchResponse>(url, ct).ConfigureAwait(false);
        if (payload?.Results is null) return Array.Empty<SearchResult>();

        return payload.Results.Select(r =>
        {
            var (artist, title) = SplitTitle(r.Title);
            var genres = (r.Genre ?? Enumerable.Empty<string>())
                .Concat(r.Style ?? Enumerable.Empty<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new SearchResult
            {
                Source = SearchSource.Discogs,
                ExternalId = r.Id?.ToString(),
                Title = title,
                Artist = artist,
                Album = title,
                Genres = genres,
                ArtworkUrl = r.CoverImage ?? r.Thumb,
                Uri = r.ResourceUrl,
                Relevance = null
            };
        }).ToList();
    }

    /// <summary>Discogs release titles are typically "Artist - Album".</summary>
    private static (string? Artist, string Title) SplitTitle(string? combined)
    {
        if (string.IsNullOrWhiteSpace(combined)) return (null, string.Empty);
        var idx = combined.IndexOf(" - ", StringComparison.Ordinal);
        return idx < 0
            ? (null, combined)
            : (combined[..idx].Trim(), combined[(idx + 3)..].Trim());
    }

    // --- DTOs -------------------------------------------------------------

    private sealed class DiscogsSearchResponse
    {
        [JsonPropertyName("results")] public List<DiscogsResult>? Results { get; set; }
    }

    private sealed class DiscogsResult
    {
        [JsonPropertyName("id")] public long? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("genre")] public List<string>? Genre { get; set; }
        [JsonPropertyName("style")] public List<string>? Style { get; set; }
        [JsonPropertyName("thumb")] public string? Thumb { get; set; }
        [JsonPropertyName("cover_image")] public string? CoverImage { get; set; }
        [JsonPropertyName("resource_url")] public string? ResourceUrl { get; set; }
    }
}
