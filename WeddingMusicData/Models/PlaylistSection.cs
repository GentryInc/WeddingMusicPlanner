using System.ComponentModel.DataAnnotations;

namespace WeddingMusicData.Models;

/// <summary>
/// An ordered segment of the event (e.g. Prelude, Processional, Recessional,
/// Cocktail Hour) with a transition mode governing how tracks flow.
/// </summary>
public class PlaylistSection
{
    public int Id { get; set; }

    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Display/running order of the section within the event.</summary>
    public int Order { get; set; }

    public TransitionMode TransitionMode { get; set; } = TransitionMode.StopAfterTrack;

    /// <summary>Default crossfade duration (ms) for continuous-mix sections.</summary>
    public int DefaultCrossfadeMs { get; set; } = 4000;

    [MaxLength(512)]
    public string? Notes { get; set; }

    /// <summary>
    /// When true, playback restarts this section's first track after its last track
    /// ends under auto-advance, instead of moving on to the following section.
    /// </summary>
    public bool IsLooping { get; set; }

    /// <summary>
    /// Optional explicit chain target. When set, auto-advance jumps to this section
    /// after the last track instead of spilling into the next section by <see cref="Order"/>.
    /// </summary>
    public int? NextSectionId { get; set; }

    /// <summary>Navigation for the explicit chain target.</summary>
    public PlaylistSection? NextSection { get; set; }

    public ICollection<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();
}

/// <summary>
/// Join entity ordering a <see cref="Track"/> within a <see cref="PlaylistSection"/>.
/// A track may appear in multiple sections; ordering is explicit for live cueing.
/// </summary>
public class PlaylistItem
{
    public int Id { get; set; }

    public int PlaylistSectionId { get; set; }
    public PlaylistSection? Section { get; set; }

    public int TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>Position of this track inside the section.</summary>
    public int Position { get; set; }

    /// <summary>Optional per-placement override of the section transition mode.</summary>
    public TransitionMode? TransitionModeOverride { get; set; }
}
