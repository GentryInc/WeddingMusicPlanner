using NAudio.Wave;
using WeddingAudioEngine;
using Xunit;

namespace WeddingAudioEngine.Tests;

public class EqualizerTests
{
    /// <summary>A constant-amplitude mono/stereo tone generator for DSP tests.</summary>
    private sealed class ConstantSignal : ISampleProvider
    {
        private readonly float _value;
        public WaveFormat WaveFormat { get; }

        public ConstantSignal(float value, int channels = 2, int sampleRate = 44100)
        {
            _value = value;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++) buffer[offset + i] = _value;
            return count;
        }
    }

    [Fact]
    public void FlatEq_IsTransparent()
    {
        var eq = new EqualizerSampleProvider(new ConstantSignal(0.5f));
        var buffer = new float[512];

        int read = eq.Read(buffer, 0, buffer.Length);

        Assert.Equal(512, read);
        Assert.False(eq.IsActive);
        Assert.All(buffer, s => Assert.Equal(0.5f, s, 5));
    }

    [Fact]
    public void SettingAnyBand_MarksActive_AndClampsToTwelveDb()
    {
        var eq = new EqualizerSampleProvider(new ConstantSignal(0.25f))
        {
            LowGainDb = 30f  // beyond the ±12 dB range
        };

        Assert.True(eq.IsActive);
        Assert.Equal(12f, eq.LowGainDb);
    }

    [Fact]
    public void BassBoost_IncreasesEnergy_OnLowFrequencyContent()
    {
        // A DC / very-low-frequency signal is boosted by a positive low shelf.
        var flat = new EqualizerSampleProvider(new ConstantSignal(0.3f));
        var boosted = new EqualizerSampleProvider(new ConstantSignal(0.3f)) { LowGainDb = 12f };

        var a = new float[8192];
        var b = new float[8192];

        // Prime the filters past their transient, then measure steady state.
        flat.Read(a, 0, a.Length);
        boosted.Read(b, 0, b.Length);
        flat.Read(a, 0, a.Length);
        boosted.Read(b, 0, b.Length);

        float flatAvg = Average(a);
        float boostedAvg = Average(b);

        Assert.True(boostedAvg > flatAvg,
            $"Expected low-shelf boost to raise low-frequency level (flat={flatAvg}, boosted={boostedAvg}).");
    }

    private static float Average(float[] samples)
    {
        double sum = 0;
        foreach (var s in samples) sum += Math.Abs(s);
        return (float)(sum / samples.Length);
    }
}
