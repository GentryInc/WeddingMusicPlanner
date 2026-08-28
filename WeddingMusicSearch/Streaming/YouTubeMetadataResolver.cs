using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Options;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Infrastructure;

namespace WeddingMusicSearch.Streaming;

/// <summary>
/// Refreshes YouTube video display metadata via the Data API v3 <c>videos.list</c>
/// endpoint (a cheap 1-unit lookup by id). Reuses <see cref="YouTubeMusicOptions"/>
/// so it shares the configured key and per-second courtesy limiter.
/// </summary>
public sealed class YouTubeMetadataResolver : IStreamingMetadataResolver
{
    private readonly HttpClient _http;
    private readonly YouTubeMusicOptions _options;
    private readonly RateLimiter _rateLimiter;

    public YouTubeMetadataResolver(HttpClient http, IOptions<YouTubeMusicOptions> options)
    {
        _options = options.Value;
        _http = http;
        _http.BaseAddress ??= new Uri(_options.BaseUrl);
        _rateLimiter = new RateLimiter(_options.RequestsPerInterval, _options.Interval);
    }

    public SearchSource Source => SearchSource.YouTube;

    public async Task<StreamingMetadata?> ResolveAsync(string uri, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return null;

        var videoId = ExtractVideoId(uri);
        if (videoId is null)
            return null; // not a YouTube watch URI this resolver handles.

        await _rateLimiter.WaitAsync(ct).ConfigureAwait(false);

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["part"] = "snippet";
        qs["id"] = videoId;
        qs["key"] = _options.ApiKey;
        var url = $"videos?{qs}";

        var payload = await _http.GetFromJsonAsync<YtVideoListResponse>(url, ct).ConfigureAwait(false);
        var snippet = payload?.Items?.FirstOrDefault()?.Snippet;
        if (snippet is null)
            return null; // deleted/unavailable; caller keeps the previous cache.

        // YouTube has no first-class artist/album on a video; channel is the best proxy.
        return new StreamingMetadata(snippet.Title ?? string.Empty, snippet.ChannelTitle, null);
    }

    /// <summary>Extracts the 11-char video id from a watch URI, or null if not one.</summary>
    private static string? ExtractVideoId(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return null;
        if (!parsed.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) &&
            !parsed.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            return null;

        if (parsed.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            return parsed.AbsolutePath.Trim('/') is { Length: > 0 } shortId ? shortId : null;

        var v = HttpUtility.ParseQueryString(parsed.Query)["v"];
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    // --- DTOs -------------------------------------------------------------

    private sealed class YtVideoListResponse
    {
        [JsonPropertyName("items")] public List<YtVideo>? Items { get; set; }
    }

    private sealed class YtVideo
    {
        [JsonPropertyName("snippet")] public YtSnippet? Snippet { get; set; }
    }

    private sealed class YtSnippet
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("channelTitle")] public string? ChannelTitle { get; set; }
    }
}
