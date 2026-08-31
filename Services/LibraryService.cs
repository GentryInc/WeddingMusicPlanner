using Microsoft.EntityFrameworkCore;
using WeddingMusicData;
using WeddingMusicData.Analysis;
using WeddingMusicData.Models;
using WeddingMusicSearch.Abstractions;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>Read/write access to sections and their tracks, isolating EF Core from the UI.</summary>
public interface ILibraryService
{
    Task<IReadOnlyList<PlaylistSection>> GetSectionsWithTracksAsync(CancellationToken ct = default);
    Task<CueSettings?> GetCueAsync(int trackId, CancellationToken ct = default);

    /// <summary>
    /// Sets the outro / fade-out completion point (<paramref name="stopMs"/>) and fade-out
    /// duration (<paramref name="fadeOutMs"/>) for a track, creating its <see cref="CueSettings"/>
    /// row if needed. A null <paramref name="stopMs"/> plays the track to its natural end.
    /// Example: fade out at 3:10 => stopMs = 190000.
    /// </summary>
    Task SetFadeOutAsync(int trackId, long? stopMs, int fadeOutMs, CancellationToken ct = default);

    /// <summary>
    /// Persists a full per-song edit in one transaction: the <see cref="Track"/> metadata
    /// (title/artist/album/genre/year/etc.) and the complete <see cref="CueSettings"/>
    /// (start/stop/fades/curves/mix points/gain). The <see cref="CueSettings"/> row is
    /// created on demand. This backs the double-click song-properties editor.
    /// </summary>
    Task SaveTrackEditAsync(TrackEdit edit, CancellationToken ct = default);

    Task MoveTrackAsync(int trackId, int fromSectionId, int toSectionId, int newPosition, CancellationToken ct = default);

    /// <summary>
    /// Returns the id of the first section, creating a default one if the playlist
    /// is empty. Guarantees callers (e.g. add-from-search) always have a valid target.
    /// </summary>
    Task<int> EnsureDefaultSectionAsync(CancellationToken ct = default);

    /// <summary>Creates a new playlist section appended after the last one and returns its id.</summary>
    Task<int> CreateSectionAsync(string name, CancellationToken ct = default);

    /// <summary>Renames an existing playlist section.</summary>
    Task RenameSectionAsync(int sectionId, string newName, CancellationToken ct = default);

    /// <summary>Deletes a playlist section (and its item placements) and re-sequences the rest.</summary>
    Task DeleteSectionAsync(int sectionId, CancellationToken ct = default);

    /// <summary>
    /// Links <paramref name="sectionId"/> to play <paramref name="nextSectionId"/> after its
    /// last track. Pass null to clear the link. Self-links are rejected.
    /// </summary>
    Task SetNextSectionAsync(int sectionId, int? nextSectionId, CancellationToken ct = default);

    /// <summary>Sets whether a playlist section loops back to its first track after the last.</summary>
    Task SetLoopingAsync(int sectionId, bool isLooping, CancellationToken ct = default);

    /// <summary>
    /// Ensures a <see cref="Track"/> exists for a search hit and returns its id.
    /// Local hits resolve to their existing PK; remote hits are persisted as a new
    /// streaming-backed track (keyed by provider URI so re-adding is idempotent).
    /// </summary>
    Task<int> EnsureTrackAsync(SearchResult result, CancellationToken ct = default);

    /// <summary>Appends a track to the end of a section (no duplicates within the section).</summary>
    Task AddTrackToSectionAsync(int trackId, int sectionId, CancellationToken ct = default);

    /// <summary>Removes a track placement from a section and re-sequences positions.</summary>
    Task RemoveTrackFromSectionAsync(int trackId, int sectionId, CancellationToken ct = default);

    /// <summary>
    /// Suggests harmonically compatible tracks (Camelot wheel) to follow the given
    /// track, ordered by closeness of tempo. Returns an empty list when the source
    /// track has no analysable key.
    /// </summary>
    Task<IReadOnlyList<Track>> GetHarmonicSuggestionsAsync(int trackId, int limit = 10, CancellationToken ct = default);
}

public sealed class LibraryService : ILibraryService
{
    private readonly IDbContextFactory<WeddingMusicContext> _factory;

    public LibraryService(IDbContextFactory<WeddingMusicContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<PlaylistSection>> GetSectionsWithTracksAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.PlaylistSections
            .AsNoTracking()
            .OrderBy(s => s.Order)
            .Include(s => s.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.Track)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> EnsureDefaultSectionAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var first = await db.PlaylistSections
            .OrderBy(s => s.Order)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (first is not null)
            return first.Id;

        var section = new PlaylistSection { Name = "My Playlist", Order = 0 };
        db.PlaylistSections.Add(section);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return section.Id;
    }

    public async Task SetFadeOutAsync(int trackId, long? stopMs, int fadeOutMs, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var cue = await db.CueSettings
            .FirstOrDefaultAsync(c => c.TrackId == trackId, ct)
            .ConfigureAwait(false);
        if (cue is null)
        {
            cue = new CueSettings { TrackId = trackId };
            db.CueSettings.Add(cue);
        }

        // Negative/absent stop = play to natural end; clamp the fade to a sane minimum.
        cue.StopMs = stopMs is { } s && s > 0 ? s : null;
        cue.FadeOutMs = Math.Max(0, fadeOutMs);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveTrackEditAsync(TrackEdit edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var track = await db.Tracks
            .Include(t => t.CueSettings)
            .FirstOrDefaultAsync(t => t.Id == edit.TrackId, ct)
            .ConfigureAwait(false);
        if (track is null)
            throw new InvalidOperationException($"Track {edit.TrackId} no longer exists.");

        // --- Metadata ---
        track.Title = string.IsNullOrWhiteSpace(edit.Title) ? track.Title : edit.Title.Trim();
        track.Artist = edit.Artist?.Trim();
        track.Album = edit.Album?.Trim();
        track.Genre = edit.Genre?.Trim();
        track.Year = edit.Year;
        track.TrackNumber = edit.TrackNumber;
        track.MusicalKey = edit.MusicalKey?.Trim();
        track.Mood = edit.Mood?.Trim();
        track.Energy = edit.Energy;
        track.Danceability = edit.Danceability;
        track.Bpm = edit.Bpm;

        // --- Cue / fade settings ---
        var cue = track.CueSettings;
        if (cue is null)
        {
            cue = new CueSettings { TrackId = track.Id };
            db.CueSettings.Add(cue);
        }

        cue.StartMs = Math.Max(0, edit.StartMs);
        cue.StopMs = edit.StopMs is { } s && s > 0 ? s : null;
        cue.FadeInMs = Math.Max(0, edit.FadeInMs);
        cue.FadeOutMs = Math.Max(0, edit.FadeOutMs);
        cue.FadeInCurve = edit.FadeInCurve;
        cue.FadeOutCurve = edit.FadeOutCurve;
        cue.GainTrimDb = edit.GainTrimDb;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CreateSectionAsync(string name, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        int nextOrder = await db.PlaylistSections
            .Select(s => (int?)s.Order)
            .MaxAsync(ct)
            .ConfigureAwait(false) is { } max ? max + 1 : 0;

        var section = new PlaylistSection
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New Playlist" : name.Trim(),
            Order = nextOrder
        };
        db.PlaylistSections.Add(section);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return section.Id;
    }

    public async Task RenameSectionAsync(int sectionId, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return;

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var section = await db.PlaylistSections
            .FirstOrDefaultAsync(s => s.Id == sectionId, ct)
            .ConfigureAwait(false);
        if (section is null)
            return;

        section.Name = newName.Trim();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteSectionAsync(int sectionId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var section = await db.PlaylistSections
            .FirstOrDefaultAsync(s => s.Id == sectionId, ct)
            .ConfigureAwait(false);
        if (section is null)
            return;

        db.PlaylistSections.Remove(section);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Re-sequence the remaining sections so Order stays contiguous.
        var remaining = await db.PlaylistSections
            .OrderBy(s => s.Order)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int order = 0;
        foreach (var s in remaining)
            s.Order = order++;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task SetNextSectionAsync(int sectionId, int? nextSectionId, CancellationToken ct = default)
    {
        if (nextSectionId == sectionId)
            throw new InvalidOperationException("A playlist cannot chain to itself.");

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var section = await db.PlaylistSections
            .FirstOrDefaultAsync(s => s.Id == sectionId, ct)
            .ConfigureAwait(false);
        if (section is null)
            return;

        if (nextSectionId is { } target)
        {
            bool exists = await db.PlaylistSections
                .AnyAsync(s => s.Id == target, ct)
                .ConfigureAwait(false);
            if (!exists)
                throw new InvalidOperationException("The linked playlist no longer exists.");
        }

        section.NextSectionId = nextSectionId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SetLoopingAsync(int sectionId, bool isLooping, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var section = await db.PlaylistSections
            .FirstOrDefaultAsync(s => s.Id == sectionId, ct)
            .ConfigureAwait(false);
        if (section is null)
            return;

        section.IsLooping = isLooping;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<CueSettings?> GetCueAsync(int trackId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.CueSettings.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TrackId == trackId, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Track>> GetHarmonicSuggestionsAsync(int trackId, int limit = 10, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var source = await db.Tracks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == trackId, ct)
            .ConfigureAwait(false);

        var compatible = HarmonicMixing.CompatibleCodes(source?.MusicalKey);
        if (source is null || compatible.Count == 0)
            return Array.Empty<Track>();

        var compatibleSet = new HashSet<string>(compatible, StringComparer.Ordinal);

        // Candidate keys can't be filtered in SQL (Camelot conversion is in-memory),
        // so pull tracks that have a key and evaluate compatibility client-side.
        var candidates = await db.Tracks.AsNoTracking()
            .Where(t => t.Id != trackId && t.IsAvailable && t.MusicalKey != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return candidates
            .Where(t => HarmonicMixing.ToCamelotCode(t.MusicalKey) is { } code && compatibleSet.Contains(code))
            .OrderBy(t => TempoDistance(source.Bpm, t.Bpm))
            .ThenBy(t => t.Title)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    // Tracks without a known BPM sort last; otherwise order by absolute tempo delta.
    private static double TempoDistance(double? a, double? b)
        => a is null || b is null ? double.MaxValue : Math.Abs(a.Value - b.Value);

    /// <summary>Atomically moves a track between sections and re-sequences positions.</summary>
    public async Task MoveTrackAsync(int trackId, int fromSectionId, int toSectionId, int newPosition, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var item = await db.PlaylistItems
            .FirstOrDefaultAsync(i => i.TrackId == trackId && i.PlaylistSectionId == fromSectionId, ct)
            .ConfigureAwait(false);

        if (item is null)
        {
            item = new PlaylistItem { TrackId = trackId, PlaylistSectionId = toSectionId };
            db.PlaylistItems.Add(item);
        }

        item.PlaylistSectionId = toSectionId;
        item.Position = newPosition;

        // Re-sequence the destination section so positions stay contiguous.
        var destItems = await db.PlaylistItems
            .Where(i => i.PlaylistSectionId == toSectionId && i.TrackId != trackId)
            .OrderBy(i => i.Position)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int pos = 0;
        foreach (var di in destItems)
        {
            if (pos == newPosition) pos++;
            di.Position = pos++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> EnsureTrackAsync(SearchResult result, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Local hits already have a PK in ExternalId.
        if (result.Source == SearchSource.Local &&
            int.TryParse(result.ExternalId, out var localId))
        {
            return localId;
        }

        // Remote hit: de-dup by streaming URI (used as the synthetic FilePath key).
        var key = result.Uri ?? $"{result.Source}:{result.ExternalId}";
        var existing = await db.Tracks
            .FirstOrDefaultAsync(t => t.FilePath == key, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            // For reference-only streaming sources, treat stored title/artist/album as a
            // display cache and refresh it on re-add so we never rely on stale content.
            if (IsReferenceOnlySource(result.Source))
                ApplyDisplayCache(existing, result);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return existing.Id;
        }

        var track = new Track
        {
            FilePath = key,
            ExternalUri = result.Uri,
            DurationMs = result.DurationMs ?? 0,
            IsAvailable = true,
            IsCachedOffline = false,
            CueSettings = new CueSettings { FadeOutMs = 2000 }
        };

        if (IsReferenceOnlySource(result.Source))
        {
            // Spotify/YouTube/Apple content: persist the URI as the durable reference
            // only, keep the minimal title/artist/album as a refreshable display cache,
            // and deliberately do NOT store derived audio analysis (BPM/energy/etc.).
            // This honours the "don't cache content beyond immediate use" Developer Terms.
            ApplyDisplayCache(track, result);
        }
        else
        {
            // Catalog-only sources (MusicBrainz/Discogs) are not streaming-service content;
            // retain their richer metadata for offline planning.
            track.Title = result.Title;
            track.Artist = result.Artist;
            track.Album = result.Album;
            track.Genre = result.Genres.Count > 0 ? result.Genres[0] : null;
            track.Bpm = result.Bpm;
            track.Mood = result.Mood;
            track.Energy = result.Energy;
            track.Danceability = result.Danceability;
            track.LastIngestedUtc = DateTimeOffset.UtcNow;
        }

        db.Tracks.Add(track);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return track.Id;
    }

    /// <summary>
    /// Streaming-service sources whose catalog content must not be cached beyond
    /// immediate use. For these we persist only the URI reference plus a refreshable
    /// display cache, never derived audio analysis.
    /// </summary>
    private static bool IsReferenceOnlySource(SearchSource source) => source switch
    {
        SearchSource.Spotify => true,
        SearchSource.YouTube => true,
        SearchSource.Apple => true,
        _ => false
    };

    /// <summary>
    /// Refreshes the minimal display-only metadata for a reference-only track and
    /// stamps <see cref="Track.LastIngestedUtc"/> as the cache time, so callers can
    /// age it out and re-resolve from the source rather than trusting stale content.
    /// </summary>
    private static void ApplyDisplayCache(Track track, SearchResult result)
    {
        track.Title = result.Title;
        track.Artist = result.Artist;
        track.Album = result.Album;
        track.LastIngestedUtc = DateTimeOffset.UtcNow;
    }

    public async Task AddTrackToSectionAsync(int trackId, int sectionId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        bool alreadyThere = await db.PlaylistItems
            .AnyAsync(i => i.TrackId == trackId && i.PlaylistSectionId == sectionId, ct)
            .ConfigureAwait(false);
        if (alreadyThere)
            return;

        int nextPosition = await db.PlaylistItems
            .Where(i => i.PlaylistSectionId == sectionId)
            .Select(i => (int?)i.Position)
            .MaxAsync(ct)
            .ConfigureAwait(false) is { } max ? max + 1 : 0;

        db.PlaylistItems.Add(new PlaylistItem
        {
            TrackId = trackId,
            PlaylistSectionId = sectionId,
            Position = nextPosition
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveTrackFromSectionAsync(int trackId, int sectionId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var item = await db.PlaylistItems
            .FirstOrDefaultAsync(i => i.TrackId == trackId && i.PlaylistSectionId == sectionId, ct)
            .ConfigureAwait(false);
        if (item is null)
            return;

        db.PlaylistItems.Remove(item);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Re-sequence the remaining items so positions stay contiguous.
        var remaining = await db.PlaylistItems
            .Where(i => i.PlaylistSectionId == sectionId)
            .OrderBy(i => i.Position)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int pos = 0;
        foreach (var di in remaining)
            di.Position = pos++;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
