using NAudio.Dsp;
using NAudio.Wave;

namespace WeddingAudioEngine;

/// <summary>
/// A 3-band equalizer (low shelf / mid peak / high shelf) applied per channel using
/// NAudio biquad filters. Gains are expressed in decibels and default to 0 dB (flat),
/// so inserting this node into the pipeline is transparent until the operator adjusts it.
///
/// Filter state is per channel to avoid cross-channel bleed. Coefficient recomputation is
/// done off the audio thread (on the setter) and swapped in atomically via a volatile
/// reference, so the render loop never allocates or locks.
/// </summary>
public sealed class EqualizerSampleProvider : ISampleProvider
{
    // Fixed crossover/centre points chosen for typical reception PA voicing.
    private const float LowShelfHz = 120f;
    private const float MidCentreHz = 1000f;
    private const float MidQ = 0.9f;
    private const float HighShelfHz = 8000f;

    private readonly ISampleProvider _source;
    private readonly int _channels;

    // One filter trio per channel. Volatile so the audio thread always sees the
    // most recently published coefficient set after a band gain change.
    private volatile BiQuadFilter[] _low;
    private volatile BiQuadFilter[] _mid;
    private volatile BiQuadFilter[] _high;

    private float _lowGainDb;
    private float _midGainDb;
    private float _highGainDb;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Whether any band is currently non-flat (used to allow a cheap bypass).</summary>
    public bool IsActive => _lowGainDb != 0f || _midGainDb != 0f || _highGainDb != 0f;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _low = BuildBand(Band.LowShelf, 0f);
        _mid = BuildBand(Band.MidPeak, 0f);
        _high = BuildBand(Band.HighShelf, 0f);
    }

    /// <summary>Low-shelf gain in dB (approx. below 120 Hz). Clamped to ±12 dB.</summary>
    public float LowGainDb
    {
        get => _lowGainDb;
        set { _lowGainDb = Clamp(value); _low = BuildBand(Band.LowShelf, _lowGainDb); }
    }

    /// <summary>Mid peaking gain in dB (centred at 1 kHz). Clamped to ±12 dB.</summary>
    public float MidGainDb
    {
        get => _midGainDb;
        set { _midGainDb = Clamp(value); _mid = BuildBand(Band.MidPeak, _midGainDb); }
    }

    /// <summary>High-shelf gain in dB (approx. above 8 kHz). Clamped to ±12 dB.</summary>
    public float HighGainDb
    {
        get => _highGainDb;
        set { _highGainDb = Clamp(value); _high = BuildBand(Band.HighShelf, _highGainDb); }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read == 0 || !IsActive) return read;

        // Snapshot the current filter references so a concurrent gain change can't
        // swap the array mid-frame.
        var low = _low;
        var mid = _mid;
        var high = _high;

        for (int n = 0; n < read; n++)
        {
            int ch = n % _channels;
            float sample = buffer[offset + n];
            sample = low[ch].Transform(sample);
            sample = mid[ch].Transform(sample);
            sample = high[ch].Transform(sample);
            buffer[offset + n] = sample;
        }

        return read;
    }

    private enum Band { LowShelf, MidPeak, HighShelf }

    private BiQuadFilter[] BuildBand(Band band, float gainDb)
    {
        int sampleRate = WaveFormat.SampleRate;
        var filters = new BiQuadFilter[_channels];
        for (int ch = 0; ch < _channels; ch++)
        {
            filters[ch] = band switch
            {
                Band.LowShelf => BiQuadFilter.LowShelf(sampleRate, LowShelfHz, 1f, gainDb),
                Band.MidPeak => BiQuadFilter.PeakingEQ(sampleRate, MidCentreHz, MidQ, gainDb),
                Band.HighShelf => BiQuadFilter.HighShelf(sampleRate, HighShelfHz, 1f, gainDb),
                _ => BiQuadFilter.PeakingEQ(sampleRate, MidCentreHz, MidQ, gainDb)
            };
        }
        return filters;
    }

    private static float Clamp(float db) => Math.Clamp(db, -12f, 12f);
}
