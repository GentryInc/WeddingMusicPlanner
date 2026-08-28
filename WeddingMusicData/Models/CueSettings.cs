namespace WeddingMusicData.Models;

/// <summary>
/// Per-track cue configuration: intro skip (T-in), outro point (T-out) and the
/// fade curves applied by the audio engine. All timestamps stored in milliseconds.
/// </summary>
public class CueSettings
{
    public int Id { get; set; }

    /// <summary>FK to the owning track (one-to-one).</summary>
    public int TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>Intro skip point — playback starts here (T-in). Default 0.</summary>
    public long StartMs { get; set; }

    /// <summary>Outro point — fade completes/stops here (T-out). Null = play to natural end.</summary>
    public long? StopMs { get; set; }

    /// <summary>Fade-in duration applied at StartMs.</summary>
    public int FadeInMs { get; set; }

    /// <summary>Fade-out duration completing at StopMs.</summary>
    public int FadeOutMs { get; set; } = 2000;

    public FadeCurve FadeInCurve { get; set; } = FadeCurve.EqualPower;
    public FadeCurve FadeOutCurve { get; set; } = FadeCurve.EqualPower;

    /// <summary>Optional cue point (ms) for beat-matched mix-in in continuous sections.</summary>
    public long? MixInPointMs { get; set; }

    /// <summary>Optional cue point (ms) where the next track should begin mixing.</summary>
    public long? MixOutPointMs { get; set; }

    /// <summary>Per-track gain trim in dB applied on top of normalisation.</summary>
    public double GainTrimDb { get; set; }
}
