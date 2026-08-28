using WeddingMusicData.Models;

namespace WeddingMusicData.Caching;

/// <summary>
/// Locates an alternative, actually-playable stream URL for a track whose
/// <see cref="Track.ExternalUri"/> is a reference-only page (e.g. a Spotify
/// track page). Implementations typically search YouTube by artist + title.
/// </summary>
public interface IStreamSourceLocator
{
    /// <summary>Returns a playable stream page URL (e.g. a YouTube watch URL), or null when none found.</summary>
    Task<string?> FindPlayableUriAsync(Track track, CancellationToken ct = default);
}
