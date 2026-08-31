using Microsoft.Extensions.DependencyInjection;
using WeddingMusicData.Caching;
using WeddingMusicData.Models;
using WeddingMusicSearch.Abstractions;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// Finds a playable YouTube watch URL for a reference-only streaming track
/// (e.g. one added from Spotify search) by searching YouTube for its
/// artist + title. Used by the offline cache as a stream-source fallback.
/// </summary>
public sealed class YouTubeStreamLocator : IStreamSourceLocator
{
    private readonly IServiceScopeFactory _scopes;

    public YouTubeStreamLocator(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<string?> FindPlayableUriAsync(Track track, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(track.Title))
            return null;

        using var scope = _scopes.CreateScope();
        var youtube = scope.ServiceProvider
            .GetServices<ISearchService>()
            .FirstOrDefault(s => s.Source == SearchSource.YouTube);
        if (youtube is null)
            return null;

        var text = string.IsNullOrWhiteSpace(track.Artist)
            ? track.Title
            : $"{track.Artist} {track.Title}";

        try
        {
            var results = await youtube
                .SearchAsync(new SearchQuery { Text = text, Limit = 1 }, ct)
                .ConfigureAwait(false);
            return results.FirstOrDefault()?.Uri;
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // Search failure (no API key, quota, network) just means no fallback.
            return null;
        }
    }
}
