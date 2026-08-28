using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Options;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Infrastructure;

namespace WeddingMusicSearch.Trending;

/// <summary>
/// Returns currently-trending music via the YouTube Data API v3 <c>videos.list</c>
/// endpoint with <c>chart=mostPopular</c> constrained to the Music category
/// (category id 10) for a given region. Reuses <see cref="YouTubeMusicOptions"/>
/// (API key + rate limiter) shared with the search provider.
///
/// Unlike <c>search.list</c>, <c>videos.list</c> returns <c>contentDetails</c>, so
/// this provider can populate duration from the ISO-8601 video length.
/// When no API key is configured the provider short-circuits and returns nothing.
/// </summary>
public sealed class YouTubeTrendingService : ITrendingService
{
    private readonly HttpClient _http;
    private readonly YouTubeMusicOptions _options;
    private readonly RateLimiter _rateLimiter;

    public YouTubeTrendingService(HttpClient http, IOptions<YouTubeMusicOptions> options)
    {
        _options = options.Value;
        _http = http;
        _http.BaseAddress ??= new Uri(_options.BaseUrl);
        _rateLimiter = new RateLimiter(_options.RequestsPerInterval, _options.Interval);
    }

    public SearchSource Source => SearchSource.YouTube;

    public async Task<IReadOnlyList<SearchResult>> GetTrendingAsync(TrendingQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return Array.Empty<SearchResult>();

        await _rateLimiter.WaitAsync(ct).ConfigureAwait(false);

        int limit = Math.Clamp(query.Limit, 1, 50); // Data API caps maxResults at 50.

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["part"] = "snippet,contentDetails";
        qs["chart"] = "mostPopular";
        qs["videoCategoryId"] = _options.VideoCategoryId;
        qs["regionCode"] = query.Region;
        qs["maxResults"] = limit.ToString();
        qs["key"] = _options.ApiKey;
        var url = $"videos?{qs}";

        var payload = await _http.GetFromJsonAsync<YtVideoListResponse>(url, ct).ConfigureAwait(false);
        if (payload?.Items is null || payload.Items.Count == 0)
            return Array.Empty<SearchResult>();

        // Preserve chart order as descending relevance so top-trending ranks first.
        int rank = payload.Items.Count;
        return payload.Items
            .Where(v => !string.IsNullOrWhiteSpace(v.Id))
            .Select(v =>
            {
                var s = v.Snippet;
                var result = new SearchResult
                {
                    Source = SearchSource.YouTube,
                    ExternalId = v.Id,
                    Title = s?.Title ?? string.Empty,
                    Artist = s?.ChannelTitle,
                    ArtworkUrl = BestThumbnail(s?.Thumbnails),
                    DurationMs = ParseIsoDuration(v.ContentDetails?.Duration),
                    Uri = $"https://www.youtube.com/watch?v={v.Id}",
                    Relevance = (double)(rank--) / payload.Items.Count
                };
                return result;
            })
            .ToList();
    }

    /// <summary>Prefers the highest-resolution thumbnail YouTube supplies.</summary>
    private static string? BestThumbnail(YtThumbnails? thumbs) =>
        thumbs?.High?.Url ?? thumbs?.Medium?.Url ?? thumbs?.Default?.Url;

    /// <summary>
    /// Converts an ISO-8601 duration (e.g. "PT3M45S") to milliseconds. Returns null
    /// when absent or unparseable so callers treat duration as simply unknown.
    /// </summary>
    private static long? ParseIsoDuration(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        try
        {
            return (long)System.Xml.XmlConvert.ToTimeSpan(iso).TotalMilliseconds;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // --- DTOs -------------------------------------------------------------

    private sealed class YtVideoListResponse
    {
        [JsonPropertyName("items")] public List<YtVideo>? Items { get; set; }
    }

    private sealed class YtVideo
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("snippet")] public YtSnippet? Snippet { get; set; }
        [JsonPropertyName("contentDetails")] public YtContentDetails? ContentDetails { get; set; }
    }

    private sealed class YtContentDetails
    {
        [JsonPropertyName("duration")] public string? Duration { get; set; }
    }

    private sealed class YtSnippet
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("channelTitle")] public string? ChannelTitle { get; set; }
        [JsonPropertyName("thumbnails")] public YtThumbnails? Thumbnails { get; set; }
    }

    private sealed class YtThumbnails
    {
        [JsonPropertyName("default")] public YtThumbnail? Default { get; set; }
        [JsonPropertyName("medium")] public YtThumbnail? Medium { get; set; }
        [JsonPropertyName("high")] public YtThumbnail? High { get; set; }
    }

    private sealed class YtThumbnail
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
