using NAudio.Wave;
using WeddingAudioEngine;

namespace WeddingAudioEngine.Tests;

/// <summary>Fake IWavePlayer that records lifecycle calls; no audio hardware required.</summary>
public sealed class FakeWavePlayer : IWavePlayer
{
    public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
    public float Volume { get; set; } = 1f;
    public WaveFormat? OutputWaveFormat { get; private set; }
    public ISampleProvider? InitializedWith { get; private set; }
    public bool WasInitialized { get; private set; }
    public bool IsDisposed { get; private set; }
    public int PlayCount { get; private set; }

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    // NAudio's Init(ISampleProvider) is an extension method that routes here.
    public void Init(IWaveProvider waveProvider)
    {
        OutputWaveFormat = waveProvider.WaveFormat;
        WasInitialized = true;
    }

    public void Init(ISampleProvider sampleProvider)
    {
        InitializedWith = sampleProvider;
        OutputWaveFormat = sampleProvider.WaveFormat;
        WasInitialized = true;
    }

    public void Play() { PlaybackState = PlaybackState.Playing; PlayCount++; }
    public void Pause() => PlaybackState = PlaybackState.Paused;

    public void Stop()
    {
        PlaybackState = PlaybackState.Stopped;
        PlaybackStopped?.Invoke(this, new StoppedEventArgs());
    }

    public void Dispose() => IsDisposed = true;

    /// <summary>Simulates the render thread pulling audio; useful to drive the envelope.</summary>
    public int PumpSamples(int frames)
    {
        if (InitializedWith is null) return 0;
        var buffer = new float[frames * InitializedWith.WaveFormat.Channels];
        return InitializedWith.Read(buffer, 0, buffer.Length);
    }
}

public sealed class FakePlayerFactory : IWavePlayerFactory
{
    public List<(DeckRole Role, FakeWavePlayer Player)> Created { get; } = new();

    public IWavePlayer Create(DeckRole role)
    {
        var player = new FakeWavePlayer();
        Created.Add((role, player));
        return player;
    }

    public FakeWavePlayer LastFor(DeckRole role) => Created.Last(c => c.Role == role).Player;
}

/// <summary>Synthesized silent audio source of a fixed duration; tracks disposal.</summary>
public sealed class FakeAudioSource : IAudioSource, ISampleProvider
{
    private readonly long _totalFrames;
    private long _position;

    public bool IsDisposed { get; private set; }
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    public ISampleProvider SampleProvider => this;
    public TimeSpan TotalTime { get; }

    public FakeAudioSource(TimeSpan duration)
    {
        TotalTime = duration;
        _totalFrames = (long)(duration.TotalSeconds * WaveFormat.SampleRate);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        long framesLeft = _totalFrames - _position;
        int frames = (int)Math.Min(count / WaveFormat.Channels, framesLeft);
        Array.Clear(buffer, offset, frames * WaveFormat.Channels);
        _position += frames;
        return frames * WaveFormat.Channels;
    }

    public void Dispose() => IsDisposed = true;
}

public sealed class FakeSourceFactory : IAudioSourceFactory
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(3);
    public List<FakeAudioSource> Opened { get; } = new();

    public IAudioSource Open(string path)
    {
        var source = new FakeAudioSource(Duration);
        Opened.Add(source);
        return source;
    }
}
