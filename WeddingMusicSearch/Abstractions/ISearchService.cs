namespace WeddingMusicSearch.Abstractions;

/// <summary>Origin of a search result.</summary>
public enum SearchSource
{
    Local,
    Spotify,
    MusicBrainz,
    Discogs,
    Apple,
    YouTube
}

/// <summary>
/// A normalized search hit unified across the local DB and every remote provider.
/// </summary>
public sealed record SearchResult
{
    public required SearchSource Source { get; init; }

    /// <summary>Provider-native identifier (Spotify URI, MBID, Discogs release id, local PK).</summary>
    public string? ExternalId { get; init; }

    public required string Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }

    /// <summary>Genre taxonomy (MusicBrainz/Discogs can supply several).</summary>
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    public double? Bpm { get; init; }
    public long? DurationMs { get; init; }

    /// <summary>Release year when known (local catalogue or enriched remote).</summary>
    public int? Year { get; init; }

    /// <summary>Curated mood/vibe tag when known (local catalogue or enriched remote).</summary>
    public string? Mood { get; init; }

    /// <summary>Energy 0..1 when the source exposes it (e.g. Spotify audio features).</summary>
    public double? Energy { get; init; }

    /// <summary>Danceability 0..1 when the source exposes it.</summary>
    public double? Danceability { get; init; }

    /// <summary>High-resolution album artwork URL when available.</summary>
    public string? ArtworkUrl { get; init; }

    /// <summary>Streaming/preview or canonical web URI.</summary>
    public string? Uri { get; init; }

    /// <summary>Provider relevance score (0..1) when exposed; used for ranking.</summary>
    public double? Relevance { get; init; }
}

/// <summary>Query parameters for a unified search.</summary>
public sealed record SearchQuery
{
    public required string Text { get; init; }
    public string? Artist { get; init; }
    public string? Title { get; init; }
    public int Limit { get; init; } = 20;

    // --- Smart filters (all optional; null/empty = no constraint) --------

    /// <summary>Genre tags to match (case-insensitive, any-of).</summary>
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    /// <summary>Mood/vibe tag to match (case-insensitive).</summary>
    public string? Mood { get; init; }

    /// <summary>Minimum energy 0..1.</summary>
    public double? MinEnergy { get; init; }

    /// <summary>Minimum danceability 0..1.</summary>
    public double? MinDanceability { get; init; }

    /// <summary>Inclusive BPM range bounds.</summary>
    public double? MinBpm { get; init; }
    public double? MaxBpm { get; init; }

    /// <summary>Inclusive release-year range bounds.</summary>
    public int? MinYear { get; init; }
    public int? MaxYear { get; init; }
}

/// <summary>Aggregated response with per-source diagnostics.</summary>
public sealed record AggregateSearchResponse
{
    public required IReadOnlyList<SearchResult> Results { get; init; }
    public required IReadOnlyDictionary<SearchSource, SourceStatus> SourceStatuses { get; init; }
}

public sealed record SourceStatus(bool Succeeded, int Count, string? Error, TimeSpan Elapsed);

/// <summary>Unified search abstraction implemented by each source and the aggregator.</summary>
public interface ISearchService
{
    SearchSource Source { get; }
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default);
}
