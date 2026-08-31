using Microsoft.EntityFrameworkCore;
using WeddingMusicData;
using WeddingMusicData.Models;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// Records which tracks were actually played during the event and reads them back
/// (de-duplicated, in first-played order) for the wedding keepsake export.
/// </summary>
public interface IPlayHistoryService
{
    /// <summary>Logs that a track started playing on the master deck.</summary>
    Task RecordPlayAsync(int trackId, CancellationToken ct = default);

    /// <summary>
    /// Returns the distinct tracks that were played, ordered by the first time each
    /// was played. Duplicates (a song played more than once) collapse to one entry.
    /// </summary>
    Task<IReadOnlyList<Track>> GetPlayedTracksAsync(CancellationToken ct = default);
}

public sealed class PlayHistoryService : IPlayHistoryService
{
    private readonly IDbContextFactory<WeddingMusicContext> _factory;

    public PlayHistoryService(IDbContextFactory<WeddingMusicContext> factory) => _factory = factory;

    public async Task RecordPlayAsync(int trackId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.PlayHistory.Add(new PlayHistory
        {
            TrackId = trackId,
            PlayedUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Track>> GetPlayedTracksAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // First-played timestamp per track, oldest first, then materialize the tracks
        // in that order. GroupBy in SQLite returns the aggregate; project the key + min.
        var firstPlayed = await db.PlayHistory
            .GroupBy(h => h.TrackId)
            .Select(g => new { TrackId = g.Key, FirstUtc = g.Min(h => h.PlayedUtc) })
            .OrderBy(x => x.FirstUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (firstPlayed.Count == 0)
            return Array.Empty<Track>();

        var ids = firstPlayed.Select(x => x.TrackId).ToList();
        var tracks = await db.Tracks
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Preserve first-played ordering (the DB Where does not guarantee it).
        var byId = tracks.ToDictionary(t => t.Id);
        var ordered = new List<Track>(firstPlayed.Count);
        foreach (var entry in firstPlayed)
            if (byId.TryGetValue(entry.TrackId, out var t))
                ordered.Add(t);

        return ordered;
    }
}
