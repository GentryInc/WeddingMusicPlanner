namespace WeddingAudioEngine;

/// <summary>
/// Dual-deck routing engine:
///   Deck A (Master) → primary/default WASAPI render device (front of house),
///   Deck B (Cue)    → secondary WASAPI device (headphone PFL monitoring).
/// </summary>
public sealed class DualDeckAudioEngine : IDisposable
{
    private bool _disposed;

    public AudioDeck Master { get; }
    public AudioDeck Cue { get; }

    /// <summary>Raised when the master deck finishes a track (natural end or emergency fade).</summary>
    public event EventHandler? MasterTrackCompleted;

    /// <summary>
    /// Raised ~20x/second while the master deck renders audio, with the peak
    /// sample level (0..1) of the latest block. Fired on the audio render thread.
    /// </summary>
    public event EventHandler<float>? MasterLevelMeasured;

    public DualDeckAudioEngine(IWavePlayerFactory playerFactory, IAudioSourceFactory sourceFactory)
    {
        Master = new AudioDeck(DeckRole.Master, playerFactory, sourceFactory);
        Cue = new AudioDeck(DeckRole.Cue, playerFactory, sourceFactory);
        Master.TrackCompleted += (s, e) => MasterTrackCompleted?.Invoke(this, e);
        Master.LevelMeasured += (s, level) => MasterLevelMeasured?.Invoke(this, level);
    }

    /// <summary>Convenience factory using real WASAPI endpoints.</summary>
    public static DualDeckAudioEngine CreateWasapi(string? cueDeviceId = null) =>
        new(new WasapiPlayerFactory(cueDeviceId), new AudioFileSourceFactory());

    /// <summary>
    /// PFL workflow: audition a track on headphones (Deck B), then promote it to
    /// the master deck once verified.
    /// </summary>
    public void PreviewOnCue(string path, TimeSpan tIn, TimeSpan tOut)
    {
        Cue.Stop();
        Cue.Load(path, tIn, tOut);
        Cue.Play();
    }

    public void PlayOnMaster(string path, TimeSpan tIn, TimeSpan tOut,
                             TimeSpan? fadeIn = null, TimeSpan? fadeOut = null,
                             FadeShape fadeInShape = FadeShape.Linear,
                             FadeShape fadeOutShape = FadeShape.Linear)
    {
        Master.Stop();
        Master.Load(path, tIn, tOut, fadeIn, fadeOut, fadeInShape, fadeOutShape);
        Master.Play();
    }

    /// <summary>3-second emergency fade on the master deck, then advances via <see cref="MasterTrackCompleted"/>.</summary>
    public Task EmergencyFadeAndAdvanceAsync(CancellationToken ct = default) =>
        Master.EmergencyFadeAndAdvanceAsync(TimeSpan.FromSeconds(3), ct);

    /// <summary>Master output level, 0.0 .. 1.0. Applied on top of scheduled fades.</summary>
    public float MasterVolume
    {
        get => Master.Volume;
        set => Master.Volume = value;
    }

    /// <summary>Read-only master playhead position.</summary>
    public TimeSpan MasterPosition => Master.Position;

    /// <summary>Total duration of the track loaded on the master deck.</summary>
    public TimeSpan MasterDuration => Master.Duration;

    /// <summary>Repositions the master deck playhead to an absolute offset from the track start.</summary>
    public void SeekMaster(TimeSpan position) => Master.Seek(position);

    /// <summary>Low-shelf EQ gain (dB) on the master deck.</summary>
    public float MasterEqLowDb { get => Master.EqLowDb; set => Master.EqLowDb = value; }

    /// <summary>Mid peaking EQ gain (dB) on the master deck.</summary>
    public float MasterEqMidDb { get => Master.EqMidDb; set => Master.EqMidDb = value; }

    /// <summary>High-shelf EQ gain (dB) on the master deck.</summary>
    public float MasterEqHighDb { get => Master.EqHighDb; set => Master.EqHighDb = value; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Master.Dispose();
        Cue.Dispose();
    }
}
