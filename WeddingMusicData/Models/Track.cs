using System.ComponentModel.DataAnnotations;

namespace WeddingMusicData.Models;

/// <summary>
/// A single audio asset with ID3/tag metadata, analysed BPM, on-disk path and
/// optional external URI (e.g. a streaming fallback). Indexed for fast lookup
/// during live events.
/// </summary>
public class Track
{
    public int Id { get; set; }

    // --- Location ---------------------------------------------------------

    /// <summary>Absolute path to the local media file. Unique.</summary>
    [Required, MaxLength(1024)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Optional external/streaming URI used as a resilient fallback.</summary>
    [MaxLength(2048)]
    public string? ExternalUri { get; set; }

    /// <summary>Container/codec, e.g. "mp3", "wav", "flac".</summary>
    [MaxLength(16)]
    public string? Format { get; set; }

    /// <summary>Size in bytes; helps detect file changes for re-ingestion.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Content hash (e.g. SHA-256) used to detect moved/duplicated files.</summary>
    [MaxLength(64)]
    public string? ContentHash { get; set; }

    // --- Offline caching / fail-safe -------------------------------------

    /// <summary>True when the audio is available on local disk (native file or cached external stream).</summary>
    public bool IsCachedOffline { get; set; }

    /// <summary>Local path of the cached copy of <see cref="ExternalUri"/>, when downloaded.</summary>
    [MaxLength(1024)]
    public string? CachedFilePath { get; set; }

    /// <summary>UTC timestamp the external stream was last cached to disk.</summary>
    public DateTimeOffset? CachedUtc { get; set; }

    // --- ID3 / Tag metadata ----------------------------------------------

    [MaxLength(512)] public string Title { get; set; } = string.Empty;
    [MaxLength(512)] public string? Artist { get; set; }
    [MaxLength(512)] public string? Album { get; set; }
    [MaxLength(128)] public string? Genre { get; set; }
    public int? Year { get; set; }
    public int? TrackNumber { get; set; }

    // --- Playback analysis -----------------------------------------------

    /// <summary>Total duration in milliseconds (stored as integer for portable ordering).</summary>
    public long DurationMs { get; set; }

    /// <summary>Analysed tempo in beats-per-minute; nullable until analysed.</summary>
    public double? Bpm { get; set; }

    /// <summary>Musical key (e.g. "Am", "F#"), from the ID3 TKEY frame or analysis.</summary>
    [MaxLength(8)]
    public string? MusicalKey { get; set; }

    /// <summary>Integrated loudness (LUFS) for normalisation, if measured.</summary>
    public double? LoudnessLufs { get; set; }

    // --- Vibe / mood classification (for smart filtering) -----------------

    /// <summary>Curated mood/vibe tag, e.g. "Cocktail Hour", "Romantic", "Party".</summary>
    [MaxLength(64)]
    public string? Mood { get; set; }

    /// <summary>Energy level 0..1 (0 = mellow, 1 = high energy). Nullable until analysed.</summary>
    public double? Energy { get; set; }

    /// <summary>Danceability 0..1 (higher = more danceable). Nullable until analysed.</summary>
    public double? Danceability { get; set; }

    // --- Bookkeeping ------------------------------------------------------

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastIngestedUtc { get; set; }

    /// <summary>Set false if the underlying file is missing at scan time.</summary>
    public bool IsAvailable { get; set; } = true;

    // --- Relationships ----------------------------------------------------

    /// <summary>One-to-one custom cue configuration (start/stop/fade).</summary>
    public CueSettings? CueSettings { get; set; }

    public ICollection<PlaylistItem> PlaylistItems { get; set; } = new List<PlaylistItem>();
}
