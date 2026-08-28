using Microsoft.EntityFrameworkCore;
using WeddingMusicData;
using WeddingMusicSearch.Abstractions;

namespace WeddingMusicSearch.Providers;

/// <summary>Searches the local SQLite catalogue via EF Core.</summary>
public sealed class LocalSearchService : ISearchService
{
    private readonly WeddingMusicContext _db;

    public LocalSearchService(WeddingMusicContext db) => _db = db;

    public SearchSource Source => SearchSource.Local;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var text = query.Text?.Trim() ?? string.Empty;

        var q = _db.Tracks
            .AsNoTracking()
            .Where(t => t.IsAvailable);

        if (!string.IsNullOrEmpty(text))
            q = q.Where(t => EF.Functions.Like(t.Title, $"%{text}%")
                          || (t.Artist != null && EF.Functions.Like(t.Artist, $"%{text}%")));

        // --- Smart filters pushed down to SQL where possible ------------------
        if (query.Genres is { Count: > 0 } genres)
        {
            // Any-of genre match (case-insensitive). SQLite LIKE is case-insensitive for ASCII.
            q = q.Where(t => t.Genre != null && genres.Contains(t.Genre));
        }

        if (!string.IsNullOrWhiteSpace(query.Mood))
            q = q.Where(t => t.Mood != null && EF.Functions.Like(t.Mood, $"%{query.Mood}%"));

        if (query.MinEnergy is { } minEnergy)
            q = q.Where(t => t.Energy != null && t.Energy >= minEnergy);

        if (query.MinDanceability is { } minDance)
            q = q.Where(t => t.Danceability != null && t.Danceability >= minDance);

        if (query.MinBpm is { } minBpm)
            q = q.Where(t => t.Bpm != null && t.Bpm >= minBpm);

        if (query.MaxBpm is { } maxBpm)
            q = q.Where(t => t.Bpm != null && t.Bpm <= maxBpm);

        if (query.MinYear is { } minYear)
            q = q.Where(t => t.Year != null && t.Year >= minYear);

        if (query.MaxYear is { } maxYear)
            q = q.Where(t => t.Year != null && t.Year <= maxYear);

        var rows = await q
            .OrderBy(t => t.Title)
            .Take(query.Limit)
            .Select(t => new
            {
                t.Id, t.Title, t.Artist, t.Album, t.Genre, t.Bpm, t.DurationMs, t.ExternalUri,
                t.Mood, t.Energy, t.Danceability, t.Year
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(t => new SearchResult
        {
            Source = SearchSource.Local,
            ExternalId = t.Id.ToString(),
            Title = t.Title,
            Artist = t.Artist,
            Album = t.Album,
            Genres = string.IsNullOrWhiteSpace(t.Genre) ? Array.Empty<string>() : new[] { t.Genre! },
            Bpm = t.Bpm,
            DurationMs = t.DurationMs,
            Year = t.Year,
            Mood = t.Mood,
            Energy = t.Energy,
            Danceability = t.Danceability,
            Uri = t.ExternalUri,
            Relevance = 1.0
        }).ToList();
    }
}
