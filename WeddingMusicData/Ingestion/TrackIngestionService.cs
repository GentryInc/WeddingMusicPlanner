using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WeddingMusicData.Models;

namespace WeddingMusicData.Ingestion;

public sealed record IngestionResult(
    int Added,
    int Updated,
    int Skipped,
    IReadOnlyList<IngestionError> Errors);

public sealed record IngestionError(string FilePath, string Message);

/// <summary>
/// Scans directories for MP3/WAV/FLAC files, extracts ID3/tag metadata via
/// TagLib#, and commits <see cref="Track"/> rows to SQLite. Idempotent: existing
/// files are updated in place (keyed by <see cref="Track.FilePath"/>), unchanged
/// files are skipped using size + hash comparison.
/// </summary>
public sealed class TrackIngestionService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".mp4" };

    private readonly WeddingMusicContext _db;

    public TrackIngestionService(WeddingMusicContext db) => _db = db;

    /// <summary>Recursively ingests all supported files under <paramref name="rootDirectory"/>.</summary>
    public async Task<IngestionResult> IngestDirectoryAsync(
        string rootDirectory,
        bool recursive = true,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(rootDirectory))
            throw new DirectoryNotFoundException(rootDirectory);

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(rootDirectory, "*.*", option)
                             .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)));

        int added = 0, updated = 0, skipped = 0;
        var errors = new List<IngestionError>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var outcome = await IngestFileAsync(file, ct).ConfigureAwait(false);
                switch (outcome)
                {
                    case IngestOutcome.Added: added++; break;
                    case IngestOutcome.Updated: updated++; break;
                    case IngestOutcome.Skipped: skipped++; break;
                }
                progress?.Report($"{outcome}: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                errors.Add(new IngestionError(file, ex.Message));
                progress?.Report($"ERROR: {Path.GetFileName(file)} — {ex.Message}");
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IngestionResult(added, updated, skipped, errors);
    }

    /// <summary>
    /// Ingests an explicit set of files (e.g. user-selected uploads from the UI).
    /// Unsupported extensions and missing files are reported as errors rather than
    /// throwing, so one bad selection never aborts the whole batch.
    /// </summary>
    public async Task<IngestionResult> IngestFilesAsync(
        IEnumerable<string> filePaths,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        int added = 0, updated = 0, skipped = 0;
        var errors = new List<IngestionError>();

        foreach (var file in filePaths)
        {
            ct.ThrowIfCancellationRequested();

            if (!SupportedExtensions.Contains(Path.GetExtension(file)))
            {
                errors.Add(new IngestionError(file, "Unsupported file type"));
                progress?.Report($"SKIP (unsupported): {Path.GetFileName(file)}");
                continue;
            }

            if (!System.IO.File.Exists(file))
            {
                errors.Add(new IngestionError(file, "File not found"));
                progress?.Report($"ERROR: {Path.GetFileName(file)} — not found");
                continue;
            }

            try
            {
                var outcome = await IngestFileAsync(file, ct).ConfigureAwait(false);
                switch (outcome)
                {
                    case IngestOutcome.Added: added++; break;
                    case IngestOutcome.Updated: updated++; break;
                    case IngestOutcome.Skipped: skipped++; break;
                }
                progress?.Report($"{outcome}: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                errors.Add(new IngestionError(file, ex.Message));
                progress?.Report($"ERROR: {Path.GetFileName(file)} — {ex.Message}");
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IngestionResult(added, updated, skipped, errors);
    }

    private enum IngestOutcome { Added, Updated, Skipped }

    /// <summary>Ingests a single file, staging changes on the context (no SaveChanges).</summary>
    private async Task<IngestOutcome> IngestFileAsync(string filePath, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        var existing = await _db.Tracks
            .Include(t => t.CueSettings)
            .FirstOrDefaultAsync(t => t.FilePath == filePath, ct)
            .ConfigureAwait(false);

        // Fast path: unchanged file (same size) → skip expensive tag/hash work.
        if (existing is not null && existing.FileSizeBytes == info.Length && existing.ContentHash is not null)
            return IngestOutcome.Skipped;

        string hash = await ComputeHashAsync(filePath, ct).ConfigureAwait(false);
        if (existing is not null && existing.ContentHash == hash)
        {
            existing.FileSizeBytes = info.Length;
            existing.IsAvailable = true;
            existing.LastIngestedUtc = DateTimeOffset.UtcNow;
            return IngestOutcome.Skipped;
        }

        var track = existing ?? new Track { FilePath = filePath };
        PopulateFromTags(track, filePath, info, hash);

        if (existing is null)
        {
            // Seed default cue settings so the deck always has a valid envelope.
            track.CueSettings = new CueSettings { FadeOutMs = 2000 };
            _db.Tracks.Add(track);
            return IngestOutcome.Added;
        }

        return IngestOutcome.Updated;
    }

    private static void PopulateFromTags(Track track, string filePath, FileInfo info, string hash)
    {
        using var file = TagLib.File.Create(filePath);
        var tag = file.Tag;
        var props = file.Properties;

        track.Title = string.IsNullOrWhiteSpace(tag.Title)
            ? Path.GetFileNameWithoutExtension(filePath)
            : tag.Title;
        track.Artist = tag.FirstPerformer ?? tag.JoinedPerformers;
        track.Album = tag.Album;
        track.Genre = tag.FirstGenre;
        track.Year = tag.Year > 0 ? (int)tag.Year : null;
        track.TrackNumber = tag.Track > 0 ? (int)tag.Track : null;

        track.DurationMs = (long)props.Duration.TotalMilliseconds;
        // Some encoders store BPM in the tag; keep it if present, otherwise leave null for analysis.
        if (tag.BeatsPerMinute > 0)
            track.Bpm = tag.BeatsPerMinute;

        // Musical key: ID3v2 TKEY (mapped by TagLib# to InitialKey) when present.
        var key = NormalizeKey(tag.InitialKey);
        if (!string.IsNullOrWhiteSpace(key))
            track.MusicalKey = key;

        track.Format = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        track.FileSizeBytes = info.Length;
        track.ContentHash = hash;
        track.IsAvailable = true;
        track.LastIngestedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Trims and caps the raw tag key to the persisted 8-char limit.</summary>
    private static string? NormalizeKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;
        var trimmed = rawKey.Trim();
        return trimmed.Length > 8 ? trimmed[..8] : trimmed;
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
