namespace WeddingMusicSearch.Infrastructure;

public sealed class SpotifyOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class MusicBrainzOptions
{
    /// <summary>MusicBrainz requires a descriptive User-Agent with contact info.</summary>
    public string UserAgent { get; set; } = "WeddingMusicPlannerPro/1.0 (https://github.com/GentryInc/WeddingMusicPlannerPro)";
    public string BaseUrl { get; set; } = "https://musicbrainz.org/ws/2/";

    /// <summary>MusicBrainz asks for no more than 1 request/second.</summary>
    public int RequestsPerInterval { get; set; } = 1;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
}

public sealed class DiscogsOptions
{
    public string UserAgent { get; set; } = "WeddingMusicPlannerPro/1.0 (https://github.com/GentryInc/WeddingMusicPlannerPro)";
    public string BaseUrl { get; set; } = "https://api.discogs.com/";

    /// <summary>Personal access token or consumer key/secret for higher rate limits.</summary>
    public string? PersonalAccessToken { get; set; }

    /// <summary>Discogs authenticated limit is ~60 requests/minute.</summary>
    public int RequestsPerInterval { get; set; } = 60;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class AppleMusicOptions
{
    public string BaseUrl { get; set; } = "https://api.music.apple.com/v1/";

    /// <summary>
    /// Signed MusicKit developer JWT (ES256). Required for catalog search;
    /// when empty the provider short-circuits and returns no results.
    /// </summary>
    public string DeveloperToken { get; set; } = string.Empty;

    /// <summary>Storefront/region for catalog search, e.g. "us", "gb".</summary>
    public string Storefront { get; set; } = "us";

    /// <summary>Apple Music API allows generous throughput; keep a light courtesy cap.</summary>
    public int RequestsPerInterval { get; set; } = 20;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
}

public sealed class YouTubeMusicOptions
{
    public string BaseUrl { get; set; } = "https://www.googleapis.com/youtube/v3/";

    /// <summary>
    /// YouTube Data API v3 key. Required for search; when empty the provider
    /// short-circuits and returns no results so it never fails the aggregate.
    /// Supply securely via configuration/user-secrets, never hard-coded.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Restrict search to the Music category (YouTube category id 10) so results
    /// are songs/official audio rather than arbitrary videos.
    /// </summary>
    public string VideoCategoryId { get; set; } = "10";

    /// <summary>
    /// The Data API enforces a daily quota (a search costs ~100 units of the
    /// default 10,000/day). Keep a light per-second courtesy cap on top of that.
    /// </summary>
    public int RequestsPerInterval { get; set; } = 5;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
}
