using WeddingMusicSearch.Abstractions;

namespace WeddingMusicSearch;

/// <summary>
/// Applies the smart genre/mood/energy/danceability/BPM filters uniformly across
/// every provider's results. Semantics: a result is only excluded when the
/// relevant attribute is <em>known</em> and fails the constraint. Unknown
/// attributes are kept, so a provider that doesn't expose (say) danceability
/// never has its results silently dropped.
/// </summary>
public static class SearchFilter
{
    public static bool Matches(SearchResult result, SearchQuery query)
    {
        // Genre: any-of, case-insensitive. Only filters when the result has genres.
        if (query.Genres is { Count: > 0 } wanted && result.Genres.Count > 0)
        {
            bool any = result.Genres.Any(g =>
                wanted.Any(w => string.Equals(g, w, StringComparison.OrdinalIgnoreCase)));
            if (!any) return false;
        }

        // Mood: substring/equals, case-insensitive. Only filters when mood is known.
        if (!string.IsNullOrWhiteSpace(query.Mood) && !string.IsNullOrWhiteSpace(result.Mood))
        {
            if (result.Mood!.IndexOf(query.Mood!, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        if (query.MinEnergy is { } minE && result.Energy is { } e && e < minE)
            return false;

        if (query.MinDanceability is { } minD && result.Danceability is { } d && d < minD)
            return false;

        if (result.Bpm is { } bpm)
        {
            if (query.MinBpm is { } minB && bpm < minB) return false;
            if (query.MaxBpm is { } maxB && bpm > maxB) return false;
        }

        // Year: only filters when the result's year is known.
        if (result.Year is { } year)
        {
            if (query.MinYear is { } minY && year < minY) return false;
            if (query.MaxYear is { } maxY && year > maxY) return false;
        }

        return true;
    }

    /// <summary>Returns true when the query carries at least one smart filter.</summary>
    public static bool HasFilters(SearchQuery query) =>
        query.Genres.Count > 0 ||
        !string.IsNullOrWhiteSpace(query.Mood) ||
        query.MinEnergy is not null ||
        query.MinDanceability is not null ||
        query.MinBpm is not null ||
        query.MaxBpm is not null ||
        query.MinYear is not null ||
        query.MaxYear is not null;
}
