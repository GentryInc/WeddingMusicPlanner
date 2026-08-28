using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Infrastructure;

namespace WeddingMusicSearch.Providers;

/// <summary>
/// Queries the MusicBrainz web service for recordings and their genre/tag
/// taxonomy. Honours the ~1 req/s courtesy limit via an injected rate limiter.
/// Cover art is resolved through the Cover Art Archive by release MBID.
/// </summary>
public sealed class MusicBrainzSearchService : ISearchService
{
    private readonly HttpClient _http;
    private readonly RateLimiter _rateLimiter;

    public MusicBrainzSearchService(HttpClient http, IOptions<MusicBrainzOptions> options)
    {
        var o = options.Value;
        _http = http;
        _http.BaseAddress ??= new Uri(o.BaseUrl);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(o.UserAgent);
        _rateLimiter = new RateLimiter(o.RequestsPerInterval, o.Interval);
    }

    public SearchSource Source => SearchSource.MusicBrainz;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        await _rateLimiter.WaitAsync(ct).ConfigureAwait(false);

        var lucene = BuildLucene(query);
        var url = $"recording?query={Uri.EscapeDataString(lucene)}&fmt=json&limit={Math.Clamp(query.Limit, 1, 100)}&inc=tags+genres+releases";

        var payload = await _http.GetFromJsonAsync<MbRecordingResponse>(url, ct).ConfigureAwait(false);
        if (payload?.Recordings is null) return Array.Empty<SearchResult>();

        return payload.Recordings.Select(r =>
        {
            var genres = (r.Genres?.Select(g => g.Name) ?? Enumerable.Empty<string>())
                .Concat(r.Tags?.Select(t => t.Name) ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var release = r.Releases?.FirstOrDefault();
            return new SearchResult
            {
                Source = SearchSource.MusicBrainz,
                ExternalId = r.Id,
                Title = r.Title ?? string.Empty,
                Artist = r.ArtistCredit?.FirstOrDefault()?.Name,
                Album = release?.Title,
                Genres = genres,
                DurationMs = r.Length,
                ArtworkUrl = release?.Id is { } relId
                    ? $"https://coverartarchive.org/release/{relId}/front-500"
                    : null,
                Uri = $"https://musicbrainz.org/recording/{r.Id}",
                Relevance = r.Score.HasValue ? r.Score.Value / 100.0 : null
            };
        }).ToList();
    }

    private static string BuildLucene(SearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Artist) || !string.IsNullOrWhiteSpace(query.Title))
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(query.Title)) parts.Add($"recording:\"{query.Title}\"");
            if (!string.IsNullOrWhiteSpace(query.Artist)) parts.Add($"artist:\"{query.Artist}\"");
            return string.Join(" AND ", parts);
        }
        return query.Text;
    }

    // --- DTOs -------------------------------------------------------------

    private sealed class MbRecordingResponse
    {
        [JsonPropertyName("recordings")] public List<MbRecording>? Recordings { get; set; }
    }

    private sealed class MbRecording
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("length")] public long? Length { get; set; }
        [JsonPropertyName("score")] public int? Score { get; set; }
        [JsonPropertyName("artist-credit")] public List<MbArtistCredit>? ArtistCredit { get; set; }
        [JsonPropertyName("releases")] public List<MbRelease>? Releases { get; set; }
        [JsonPropertyName("tags")] public List<MbNamed>? Tags { get; set; }
        [JsonPropertyName("genres")] public List<MbNamed>? Genres { get; set; }
    }

    private sealed class MbArtistCredit
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class MbRelease
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
    }

    private sealed class MbNamed
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }
}
