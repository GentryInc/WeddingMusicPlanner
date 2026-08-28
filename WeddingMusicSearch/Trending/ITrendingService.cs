using WeddingMusicSearch.Abstractions;

namespace WeddingMusicSearch.Trending;

/// <summary>
/// Query parameters for a "what's popular right now" request. Kept separate from
/// <see cref="SearchQuery"/> because trending is a top-N editorial/chart lookup,
/// not a text fan-out.
/// </summary>
public sealed record TrendingQuery
{
    /// <summary>ISO 3166-1 alpha-2 region for localized charts (e.g. "US", "GB").</summary>
    public string Region { get; init; } = "US";

    /// <summary>Maximum number of trending items to return.</summary>
    public int Limit { get; init; } = 25;
}

/// <summary>
/// Returns a ranked list of currently-trending tracks from a single provider.
/// Implementations short-circuit (return empty) when unconfigured so they never
/// break an aggregate trending view. Results reuse the unified
/// <see cref="SearchResult"/> shape so they flow into the existing add-to-playlist UI.
/// </summary>
public interface ITrendingService
{
    SearchSource Source { get; }
    Task<IReadOnlyList<SearchResult>> GetTrendingAsync(TrendingQuery query, CancellationToken ct = default);
}
