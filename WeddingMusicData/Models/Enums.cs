namespace WeddingMusicData.Models;

/// <summary>How playback should behave when a track within a section ends.</summary>
public enum TransitionMode
{
    /// <summary>Beat-matched/crossfaded straight into the next track (DJ style).</summary>
    ContinuousMix = 0,

    /// <summary>Hard stop and wait for the operator to advance (e.g. Processional cue).</summary>
    StopAfterTrack = 1,

    /// <summary>Simple gap-less advance without a crossfade.</summary>
    AutoAdvance = 2
}

/// <summary>Shape of a fade envelope, applied by the audio engine.</summary>
public enum FadeCurve
{
    Linear = 0,
    Logarithmic = 1,
    EqualPower = 2,
    SCurve = 3
}
