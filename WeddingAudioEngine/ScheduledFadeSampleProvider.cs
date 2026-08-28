using NAudio.Wave;

namespace WeddingAudioEngine;

/// <summary>
/// Applies a curve-shaped gain envelope to a source and adds sample-accurate
/// scheduling: skips to T-in (offset applied upstream), fades in at T-in, and
/// automatically begins the outro fade so it completes exactly at T-out.
///
/// Unlike NAudio's built-in <c>FadeInOutSampleProvider</c> (which is linear-only),
/// this computes gain per frame from a configurable <see cref="FadeShape"/>, so
/// the operator's per-track Linear / Logarithmic / Equal-Power / S-Curve choice
/// is actually honoured.
/// </summary>
public sealed class ScheduledFadeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly FadeShape _fadeInShape;
    private readonly FadeShape _fadeOutShape;

    private readonly long _fadeInFrames;      // frames over which the intro fade-in runs
    private readonly long _fadeStartSample;   // frame index where the scheduled outro fade begins
    private readonly long _fadeOutFrames;     // duration (frames) of the scheduled outro fade
    private readonly long _endSample;         // hard stop at T-out (per-channel frames)

    private long _samplesRead;                // frames delivered so far

    // Manual (emergency) fade-out state, set atomically via BeginManualFadeOut.
    private long _manualStartFrame = -1;
    private long _manualFadeFrames;
    private volatile bool _manualFadeActive;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Raised (on the audio thread) when playback passes T-out or the source ends.</summary>
    public event EventHandler? PlaybackReachedEnd;

    /// <param name="source">Source already positioned at T-in (offset applied upstream).</param>
    /// <param name="tIn">Intro skip point (used only to compute remaining duration).</param>
    /// <param name="tOut">Absolute outro timestamp; playback fades out to silence at this point.</param>
    /// <param name="fadeInDuration">Envelope fade-in applied at T-in.</param>
    /// <param name="fadeOutDuration">Envelope fade-out that completes at T-out.</param>
    /// <param name="fadeInShape">Curve applied to the intro fade-in.</param>
    /// <param name="fadeOutShape">Curve applied to the scheduled and manual fade-outs.</param>
    public ScheduledFadeSampleProvider(
        ISampleProvider source,
        TimeSpan tIn,
        TimeSpan tOut,
        TimeSpan fadeInDuration,
        TimeSpan fadeOutDuration,
        FadeShape fadeInShape = FadeShape.Linear,
        FadeShape fadeOutShape = FadeShape.Linear)
    {
        if (tOut <= tIn) throw new ArgumentException("T-out must be after T-in.", nameof(tOut));

        _source = source;
        _fadeInShape = fadeInShape;
        _fadeOutShape = fadeOutShape;

        int sampleRate = source.WaveFormat.SampleRate;
        long playableFrames = (long)((tOut - tIn).TotalSeconds * sampleRate);
        _fadeInFrames = Math.Max(0, (long)(fadeInDuration.TotalSeconds * sampleRate));
        _fadeOutFrames = Math.Max(0, (long)(fadeOutDuration.TotalSeconds * sampleRate));

        _endSample = playableFrames;
        _fadeStartSample = Math.Max(0, playableFrames - _fadeOutFrames);
        FadeOutDuration = fadeOutDuration;
    }

    public TimeSpan FadeOutDuration { get; }

    /// <summary>
    /// Audio delivered so far as a wall-clock duration (per-channel frames / sample rate).
    /// Read-only playhead used by the UI for a progress bar; safe to poll from any thread.
    /// </summary>
    public TimeSpan Elapsed =>
        TimeSpan.FromSeconds((double)Interlocked.Read(ref _samplesRead) / WaveFormat.SampleRate);

    /// <summary>Begins an immediate manual fade-out (e.g. the 3-second emergency fade).</summary>
    public void BeginManualFadeOut(TimeSpan duration)
    {
        int sampleRate = WaveFormat.SampleRate;
        Interlocked.Exchange(ref _manualFadeFrames, Math.Max(1, (long)(duration.TotalSeconds * sampleRate)));
        Interlocked.Exchange(ref _manualStartFrame, Interlocked.Read(ref _samplesRead));
        _manualFadeActive = true;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int channels = WaveFormat.Channels;
        long framesSoFar = Interlocked.Read(ref _samplesRead);

        // Clamp the read so we never emit audio past T-out.
        long framesRemaining = _endSample - framesSoFar;
        if (framesRemaining <= 0)
        {
            PlaybackReachedEnd?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        int maxFrames = (int)Math.Min(count / channels, framesRemaining);
        int read = _source.Read(buffer, offset, maxFrames * channels);
        int framesDelivered = read / channels;

        // Apply the curve-shaped gain envelope frame-by-frame.
        for (int f = 0; f < framesDelivered; f++)
        {
            long globalFrame = framesSoFar + f;
            float gain = ComputeGain(globalFrame);
            int baseIdx = offset + f * channels;
            for (int ch = 0; ch < channels; ch++)
                buffer[baseIdx + ch] *= gain;
        }

        Interlocked.Add(ref _samplesRead, framesDelivered);

        if (read == 0)
            PlaybackReachedEnd?.Invoke(this, EventArgs.Empty);

        return read;
    }

    /// <summary>Computes the envelope gain for a given per-channel frame index.</summary>
    private float ComputeGain(long frame)
    {
        float gain = 1f;

        // Intro fade-in (T-in).
        if (_fadeInFrames > 0 && frame < _fadeInFrames)
            gain *= FadeShapes.Gain(_fadeInShape, (double)frame / _fadeInFrames);

        // A manual/emergency fade-out overrides the scheduled outro fade.
        if (_manualFadeActive)
        {
            long start = Interlocked.Read(ref _manualStartFrame);
            long frames = Interlocked.Read(ref _manualFadeFrames);
            if (start >= 0 && frames > 0)
            {
                double progress = (double)(frame - start) / frames; // 0 -> 1 over the fade
                gain *= FadeShapes.Gain(_fadeOutShape, 1.0 - progress);
                return gain;
            }
        }

        // Scheduled outro fade completing exactly at T-out.
        if (_fadeOutFrames > 0 && frame >= _fadeStartSample)
        {
            double progress = (double)(frame - _fadeStartSample) / _fadeOutFrames; // 0 -> 1
            gain *= FadeShapes.Gain(_fadeOutShape, 1.0 - progress);
        }

        return gain;
    }
}
