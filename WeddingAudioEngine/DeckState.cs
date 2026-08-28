namespace WeddingAudioEngine;

/// <summary>States for the deck routing state machine.</summary>
public enum DeckState
{
    Idle,
    Loaded,
    Playing,
    Paused,
    Fading,
    Stopped,
    Disposed
}

/// <summary>Logical routing role of a deck.</summary>
public enum DeckRole
{
    /// <summary>Deck A — routed to the primary (front-of-house) output device.</summary>
    Master,

    /// <summary>Deck B — routed to the secondary (headphone/PFL cue) device.</summary>
    Cue
}
