namespace WeddingAudioEngine;

/// <summary>
/// Shape of a fade envelope applied by <see cref="ScheduledFadeSampleProvider"/>.
/// Mirrors the persisted <c>WeddingMusicData.Models.FadeCurve</c> so the UI's
/// per-track curve choice is actually honoured by the audio path.
/// </summary>
public enum FadeShape
{
    /// <summary>Constant-slope amplitude ramp.</summary>
    Linear = 0,

    /// <summary>Exponential/logarithmic ramp (slow start, fast finish on fade-in).</summary>
    Logarithmic = 1,

    /// <summary>Equal-power ramp (sin/cos); keeps perceived loudness steady in crossfades.</summary>
    EqualPower = 2,

    /// <summary>Smoothstep S-curve (gentle at both ends).</summary>
    SCurve = 3
}

/// <summary>
/// Gain-shaping helpers for <see cref="FadeShape"/>. Public so playback logic and
/// tests can evaluate the exact envelope curve without driving the audio device.
/// </summary>
public static class FadeShapes
{
    /// <summary>
    /// Maps a fade-in progress <paramref name="p"/> in [0,1] to a gain multiplier
    /// in [0,1] for the given <paramref name="shape"/>. Fade-outs pass <c>1 - progress</c>.
    /// </summary>
    public static float Gain(FadeShape shape, double p)
    {
        if (p <= 0) return 0f;
        if (p >= 1) return 1f;

        return shape switch
        {
            FadeShape.Linear => (float)p,
            // Squared amplitude ramp: slow onset, characteristic of an exponential/log fader.
            FadeShape.Logarithmic => (float)(p * p),
            // Equal-power: sin(p * pi/2). Constant acoustic power through the ramp.
            FadeShape.EqualPower => (float)Math.Sin(p * Math.PI / 2.0),
            // Smoothstep: 3p^2 - 2p^3. Eased at both ends.
            FadeShape.SCurve => (float)(p * p * (3.0 - 2.0 * p)),
            _ => (float)p
        };
    }
}
