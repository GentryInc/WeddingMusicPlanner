using NAudio.Wave;

namespace WeddingAudioEngine;

/// <summary>
/// A lightweight real-time safety limiter. It applies a fixed makeup <see cref="GainDb"/>
/// (used for per-track normalisation) and then guarantees the signal never exceeds
/// <see cref="CeilingLinear"/> using a fast-attack / slow-release peak envelope, so the
/// front-of-house output is protected from clipping regardless of source loudness.
/// </summary>
public sealed class LimiterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    // Smoothed gain-reduction envelope (1.0 = no reduction).
    private float _envelope = 1f;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;

    public LimiterSampleProvider(ISampleProvider source)
    {
        _source = source;

        // Time constants tuned for a transparent safety limiter (fast catch, gentle release).
        int sr = Math.Max(1, source.WaveFormat.SampleRate);
        _attackCoeff = (float)Math.Exp(-1.0 / (0.002 * sr));   // ~2 ms attack
        _releaseCoeff = (float)Math.Exp(-1.0 / (0.150 * sr));  // ~150 ms release
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Makeup/normalisation gain in dB applied before limiting. 0 = unity.</summary>
    public float GainDb { get; set; }

    /// <summary>True to bypass makeup gain and limiting entirely (pure pass-through).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Absolute output ceiling (linear, 0..1). Peaks are held below this.</summary>
    public float CeilingLinear { get; set; } = 0.99f;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (!Enabled)
            return read;

        float makeup = GainDb == 0f ? 1f : (float)Math.Pow(10.0, GainDb / 20.0);
        float ceiling = Math.Clamp(CeilingLinear, 0.01f, 1f);

        for (int n = 0; n < read; n++)
        {
            float sample = buffer[offset + n] * makeup;

            // Required instantaneous gain reduction to stay under the ceiling.
            float mag = Math.Abs(sample);
            float target = mag > ceiling ? ceiling / mag : 1f;

            // Fast attack when we must pull down, slow release when recovering.
            float coeff = target < _envelope ? _attackCoeff : _releaseCoeff;
            _envelope = (coeff * _envelope) + ((1f - coeff) * target);

            float outSample = sample * _envelope;

            // Hard clamp as a final guarantee against overshoot.
            if (outSample > ceiling) outSample = ceiling;
            else if (outSample < -ceiling) outSample = -ceiling;

            buffer[offset + n] = outSample;
        }

        return read;
    }
}
