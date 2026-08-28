using WeddingMusicSearch.Abstractions;

namespace WeddingMusicSearch.Streaming;

/// <summary>Freshly-resolved display metadata for a single streaming track.</summary>
public sealed record StreamingMetadata(string Title, string? Artist, string? Album);

/// <summary>
/// Re-resolves display-only metadata for a streaming track by its provider URI.
/// Used to refresh a reference-only cache that has aged past its allowed lifetime,
/// so the app never relies on stale cached streaming-service content.
/// Implementations return null when they can't resolve (wrong source, unconfigured,
/// or the item no longer exists), and short-circuit when their credentials are absent.
/// </summary>
public interface IStreamingMetadataResolver
{
    SearchSource Source { get; }

    /// <summary>
    /// Resolves current metadata for the given provider URI, or null if this resolver
    /// does not handle that URI / cannot resolve it.
    /// </summary>
    Task<StreamingMetadata?> ResolveAsync(string uri, CancellationToken ct = default);
}
