using System.Runtime.CompilerServices;
using WeddingAudioEngine;
using Xunit;

namespace WeddingAudioEngine.Tests;

/// <summary>
/// Verifies deterministic teardown so no large audio buffers linger and cause
/// Gen2/LOH collection stutter mid-reception.
/// </summary>
public class MemoryCleanupTests
{
    [Fact]
    public void Stop_DisposesPlayerAndSource_Deterministically()
    {
        var players = new FakePlayerFactory();
        var sources = new FakeSourceFactory();
        using var deck = new AudioDeck(DeckRole.Master, players, sources);

        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Play();
        deck.Stop();

        Assert.True(players.LastFor(DeckRole.Master).IsDisposed);
        Assert.All(sources.Opened, s => Assert.True(s.IsDisposed));
    }

    [Fact]
    public void Reload_DisposesPreviousPipeline_NoLeakedSources()
    {
        var players = new FakePlayerFactory();
        var sources = new FakeSourceFactory();
        using var deck = new AudioDeck(DeckRole.Master, players, sources);

        for (int i = 0; i < 5; i++)
        {
            deck.Load($"track{i}.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
            deck.Stop();
        }

        Assert.Equal(5, sources.Opened.Count);
        Assert.All(sources.Opened, s => Assert.True(s.IsDisposed));
        Assert.All(players.Created, c => Assert.True(c.Player.IsDisposed));
    }

    [Fact]
    public void DisposedDeck_IsGarbageCollected_NoRootedReferences()
    {
        WeakReference weakDeck = AllocateAndDisposeDeck();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weakDeck.IsAlive, "AudioDeck is still rooted after Dispose — event handlers or player references are leaking.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AllocateAndDisposeDeck()
    {
        var deck = new AudioDeck(DeckRole.Master, new FakePlayerFactory(), new FakeSourceFactory());
        deck.Load("t.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        deck.Play();
        deck.Dispose();
        return new WeakReference(deck);
    }

    [Fact]
    public void EngineDispose_DisposesBothDecks()
    {
        var players = new FakePlayerFactory();
        var sources = new FakeSourceFactory();
        var engine = new DualDeckAudioEngine(players, sources);

        engine.PlayOnMaster("a.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        engine.PreviewOnCue("b.mp3", TimeSpan.Zero, TimeSpan.FromMinutes(2));
        engine.Dispose();

        Assert.Equal(DeckState.Disposed, engine.Master.State);
        Assert.Equal(DeckState.Disposed, engine.Cue.State);
        Assert.All(players.Created, c => Assert.True(c.Player.IsDisposed));
        Assert.All(sources.Opened, s => Assert.True(s.IsDisposed));
    }
}
