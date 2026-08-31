namespace WeddingMusicData.Models;

/// <summary>
/// A record that a track was actually played on the master deck during the event.
/// Powers the "wedding keepsake" export so the couple can keep a playlist of every
/// song that was played. Distinct from <see cref="PlaylistItem"/> (planned songs)
/// and <see cref="SongRequest"/> (guest requests).
/// </summary>
public class PlayHistory
{
    public int Id { get; set; }

    /// <summary>FK to the library track that was played.</summary>
    public int TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>UTC timestamp the track started playing on the master deck.</summary>
    public DateTimeOffset PlayedUtc { get; set; } = DateTimeOffset.UtcNow;
}
