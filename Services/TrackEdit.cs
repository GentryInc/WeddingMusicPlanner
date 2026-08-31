using WeddingMusicData.Models;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// Immutable snapshot of every per-song editable property, produced by the
/// song-properties editor (opened by double-clicking a track in a playlist) and
/// consumed by <see cref="ILibraryService.SaveTrackEditAsync"/>. Groups the
/// <see cref="Track"/> metadata and the <see cref="CueSettings"/> in one payload
/// so both are saved atomically.
/// </summary>
public sealed record TrackEdit
{
    public required int TrackId { get; init; }

    // --- Metadata ---
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? Genre { get; init; }
    public int? Year { get; init; }
    public int? TrackNumber { get; init; }
    public string? MusicalKey { get; init; }
    public string? Mood { get; init; }
    public double? Energy { get; init; }
    public double? Danceability { get; init; }
    public double? Bpm { get; init; }

    // --- Cue / fade settings ---
    public long StartMs { get; init; }
    public long? StopMs { get; init; }
    public int FadeInMs { get; init; }
    public int FadeOutMs { get; init; } = 2000;
    public FadeCurve FadeInCurve { get; init; } = FadeCurve.EqualPower;
    public FadeCurve FadeOutCurve { get; init; } = FadeCurve.EqualPower;
    public double GainTrimDb { get; init; }
}
