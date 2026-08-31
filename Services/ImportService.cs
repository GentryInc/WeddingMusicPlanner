using Microsoft.EntityFrameworkCore;
using WeddingMusicData;
using WeddingMusicData.Ingestion;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// UI-facing wrapper around <see cref="TrackIngestionService"/> for importing
/// user-selected audio files (MP3/MP4/M4A/WAV/FLAC/AAC). Creates a short-lived
/// <see cref="WeddingMusicContext"/> per import so the operation never shares the
/// UI's read context.
/// </summary>
public interface IImportService
{
    /// <summary>File extensions the importer accepts, without the leading dot.</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    Task<IngestionResult> ImportFilesAsync(
        IEnumerable<string> filePaths,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

public sealed class ImportService : IImportService
{
    private readonly IDbContextFactory<WeddingMusicContext> _factory;

    public ImportService(IDbContextFactory<WeddingMusicContext> factory) => _factory = factory;

    public IReadOnlyList<string> SupportedExtensions { get; } =
        new[] { "mp3", "mp4", "m4a", "aac", "wav", "flac" };

    public async Task<IngestionResult> ImportFilesAsync(
        IEnumerable<string> filePaths,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var ingestion = new TrackIngestionService(db);
        return await ingestion.IngestFilesAsync(filePaths, progress, ct).ConfigureAwait(false);
    }
}
