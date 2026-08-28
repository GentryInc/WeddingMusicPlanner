using NAudio.Wave;
using WeddingAudioEngine;
using Xunit;

namespace WeddingAudioEngine.Tests;

public class DeckStateMachineTests
{
    private static (AudioDeck deck, FakePlayerFactory players, FakeSourceFactory sources) CreateDeck(
        DeckRole role = DeckRole.Master)
    {
        var players = new FakePlayerFactory();
        var sources = new FakeSourceFactory();
        return (new AudioDeck(role, players, sources), players, sources);
    }

    [Fact]
    public void NewDeck_IsIdle()
    {
        var (deck, _, _) = CreateDeck();
        Assert.Equal(DeckState.Idle, deck.State);
    }

    [Fact]
    public void Load_TransitionsToLoaded_AndInitializesPlayer()
    {
        var (deck, players, _) = CreateDeck();
        deck.Load("first-dance.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));

        Assert.Equal(DeckState.Loaded, deck.State);
        Assert.True(players.LastFor(DeckRole.Master).WasInitialized);
        Assert.Equal("first-dance.mp3", deck.CurrentTrackPath);
    }

    [Fact]
    public void Play_FromLoaded_TransitionsToPlaying()
    {
        var (deck, players, _) = CreateDeck();
        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Play();

        Assert.Equal(DeckState.Playing, deck.State);
        Assert.Equal(PlaybackState.Playing, players.LastFor(DeckRole.Master).PlaybackState);
    }

    [Fact]
    public void Play_FromIdle_Throws()
    {
        var (deck, _, _) = CreateDeck();
        Assert.Throws<InvalidOperationException>(() => deck.Play());
    }

    [Fact]
    public void Pause_FromPlaying_ThenResume()
    {
        var (deck, _, _) = CreateDeck();
        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Play();
        deck.Pause();
        Assert.Equal(DeckState.Paused, deck.State);
        deck.Play();
        Assert.Equal(DeckState.Playing, deck.State);
    }

    [Fact]
    public void Pause_WhenNotPlaying_Throws()
    {
        var (deck, _, _) = CreateDeck();
        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        Assert.Throws<InvalidOperationException>(() => deck.Pause());
    }

    [Fact]
    public void Load_WhilePlaying_Throws()
    {
        var (deck, _, _) = CreateDeck();
        deck.Load("a.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Play();
        Assert.Throws<InvalidOperationException>(
            () => deck.Load("b.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Stop_ReleasesPipeline_AndAllowsReload()
    {
        var (deck, players, sources) = CreateDeck();
        deck.Load("a.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Play();
        deck.Stop();

        Assert.Equal(DeckState.Stopped, deck.State);
        Assert.True(players.LastFor(DeckRole.Master).IsDisposed);
        Assert.True(sources.Opened[0].IsDisposed);
        Assert.Null(deck.CurrentTrackPath);

        deck.Load("b.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        Assert.Equal(DeckState.Loaded, deck.State);
    }

    [Fact]
    public async Task EmergencyFade_TransitionsThroughFading_ToStopped_AndDisposesStream()
    {
        var (deck, players, sources) = CreateDeck();
        var states = new List<DeckState>();
        deck.StateChanged += (_, s) => states.Add(s);
        bool completed = false;
        deck.TrackCompleted += (_, _) => completed = true;

        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Play();

        // Use a short fade so the test is fast; logic is duration-agnostic.
        await deck.EmergencyFadeAndAdvanceAsync(TimeSpan.FromMilliseconds(50));

        Assert.Contains(DeckState.Fading, states);
        Assert.Equal(DeckState.Stopped, deck.State);
        Assert.True(players.LastFor(DeckRole.Master).IsDisposed);
        Assert.True(sources.Opened[0].IsDisposed);
        Assert.True(completed);
    }

    [Fact]
    public async Task EmergencyFade_WhenNotPlaying_IsNoOp()
    {
        var (deck, _, _) = CreateDeck();
        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        await deck.EmergencyFadeAndAdvanceAsync(TimeSpan.FromMilliseconds(10));
        Assert.Equal(DeckState.Loaded, deck.State);
    }

    [Fact]
    public void Dispose_TransitionsToDisposed_AndBlocksFurtherUse()
    {
        var (deck, _, _) = CreateDeck();
        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Dispose();

        Assert.Equal(DeckState.Disposed, deck.State);
        Assert.Throws<ObjectDisposedException>(
            () => deck.Load("x.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(1)));
        Assert.Throws<ObjectDisposedException>(() => deck.Play());
    }

    [Fact]
    public void Seek_WhilePlaying_RebuildsPipeline_AndKeepsPlaying()
    {
        var (deck, players, sources) = CreateDeck();
        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Play();

        deck.Seek(TimeSpan.FromSeconds(30));

        Assert.Equal(DeckState.Playing, deck.State);
        Assert.Equal(TimeSpan.FromSeconds(30), deck.Position);
        // Reload opens a fresh source and disposes the previous one.
        Assert.Equal(2, sources.Opened.Count);
        Assert.True(sources.Opened[0].IsDisposed);
        Assert.False(sources.Opened[1].IsDisposed);
        Assert.Equal("t.mp3", deck.CurrentTrackPath);
    }

    [Fact]
    public void Seek_WhilePaused_StaysPaused()
    {
        var (deck, _, _) = CreateDeck();
        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Play();
        deck.Pause();

        deck.Seek(TimeSpan.FromSeconds(10));

        Assert.Equal(DeckState.Loaded, deck.State);
        Assert.Equal(TimeSpan.FromSeconds(10), deck.Position);
    }

    [Fact]
    public void Seek_FromIdle_Throws()
    {
        var (deck, _, _) = CreateDeck();
        Assert.Throws<InvalidOperationException>(() => deck.Seek(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Seek_ClampsPastEnd_IntoPlayableWindow()
    {
        var (deck, _, sources) = CreateDeck();
        sources.Duration = TimeSpan.FromSeconds(20);
        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromSeconds(20));
        deck.Play();

        deck.Seek(TimeSpan.FromMinutes(5)); // way past the end

        Assert.Equal(DeckState.Playing, deck.State);
        Assert.True(deck.Position < TimeSpan.FromSeconds(20));
        Assert.True(deck.Position > TimeSpan.FromSeconds(19));
    }
}

public class DualDeckRoutingTests
{
    [Fact]
    public void MasterAndCue_RouteToCorrectDeckRoles()
    {
        var players = new FakePlayerFactory();
        var sources = new FakeSourceFactory();
        using var engine = new DualDeckAudioEngine(players, sources);

        engine.PlayOnMaster("foh.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        engine.PreviewOnCue("next.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));

        Assert.Equal(DeckState.Playing, engine.Master.State);
        Assert.Equal(DeckState.Playing, engine.Cue.State);
        Assert.Equal(2, players.Created.Count);
        Assert.Single(players.Created, c => c.Role == DeckRole.Master);
        Assert.Single(players.Created, c => c.Role == DeckRole.Cue);
    }

    [Fact]
    public async Task EmergencyFade_OnlyAffectsMasterDeck()
    {
        var players = new FakePlayerFactory();
        var sources = new FakeSourceFactory();
        using var engine = new DualDeckAudioEngine(players, sources);

        engine.PlayOnMaster("foh.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        engine.PreviewOnCue("next.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));

        await engine.Master.EmergencyFadeAndAdvanceAsync(TimeSpan.FromMilliseconds(50));

        Assert.Equal(DeckState.Stopped, engine.Master.State);
        Assert.Equal(DeckState.Playing, engine.Cue.State);
    }
}

public class EnvelopeTests
{
    [Fact]
    public void Read_StopsExactly_AtTOut()
    {
        var source = new FakeAudioSource(TimeSpan.FromSeconds(10));
        var envelope = new ScheduledFadeSampleProvider(
            source, TimeSpan.Zero, TimeSpan.FromSeconds(1),
            TimeSpan.Zero, TimeSpan.FromMilliseconds(200));

        int channels = envelope.WaveFormat.Channels;
        long totalFrames = 0;
        var buffer = new float[4096];
        int read;
        while ((read = envelope.Read(buffer, 0, buffer.Length)) > 0)
            totalFrames += read / channels;

        Assert.Equal(envelope.WaveFormat.SampleRate, totalFrames); // exactly 1 second
    }

    [Fact]
    public void TOut_BeforeTIn_Throws()
    {
        var source = new FakeAudioSource(TimeSpan.FromSeconds(10));
        Assert.Throws<ArgumentException>(() => new ScheduledFadeSampleProvider(
            source, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2),
            TimeSpan.Zero, TimeSpan.Zero));
    }
}

public class FadeShapeTests
{
    [Theory]
    [InlineData(FadeShape.Linear, 0.5, 0.5)]
    [InlineData(FadeShape.Logarithmic, 0.5, 0.25)]      // p^2
    [InlineData(FadeShape.EqualPower, 0.5, 0.70710677)] // sin(pi/4)
    [InlineData(FadeShape.SCurve, 0.5, 0.5)]            // smoothstep midpoint
    [InlineData(FadeShape.SCurve, 0.25, 0.15625)]       // 3p^2 - 2p^3
    public void Gain_FollowsCurve_AtMidpoints(FadeShape shape, double p, double expected)
    {
        Assert.Equal(expected, FadeShapes.Gain(shape, p), precision: 5);
    }

    [Theory]
    [InlineData(FadeShape.Linear)]
    [InlineData(FadeShape.Logarithmic)]
    [InlineData(FadeShape.EqualPower)]
    [InlineData(FadeShape.SCurve)]
    public void Gain_IsClampedAtEndpoints(FadeShape shape)
    {
        Assert.Equal(0f, FadeShapes.Gain(shape, 0.0));
        Assert.Equal(0f, FadeShapes.Gain(shape, -0.5)); // below range clamps to silence
        Assert.Equal(1f, FadeShapes.Gain(shape, 1.0));
        Assert.Equal(1f, FadeShapes.Gain(shape, 1.5));  // above range clamps to full
    }

    [Fact]
    public void NonLinearCurves_DifferFromLinear_MidRamp()
    {
        // Logarithmic (p^2) attenuates more than linear early in a fade-in.
        Assert.True(FadeShapes.Gain(FadeShape.Logarithmic, 0.3) < FadeShapes.Gain(FadeShape.Linear, 0.3));
        // Equal-power sits above linear (constant-power crossfade characteristic).
        Assert.True(FadeShapes.Gain(FadeShape.EqualPower, 0.3) > FadeShapes.Gain(FadeShape.Linear, 0.3));
    }
}
