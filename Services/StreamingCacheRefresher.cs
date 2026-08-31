using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WeddingMusicData;
using WeddingMusicData.Models;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Streaming;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// Ages out reference-only streaming metadata: any streaming-backed track whose
/// display cache is older than <see cref="MaxCacheAge"/> is re-resolved from its
/// source and refreshed, so the app never relies on stale cached streaming content
/// (Spotify Developer Terms: "don't cache beyond immediate use").
/// </summary>
public interface IStreamingCacheRefresher
{
    Task<int> RefreshStaleAsync(CancellationToken ct = default);
}

public sealed class StreamingCacheRefresher : IStreamingCacheRefresher
{
    /// <summary>Streaming display caches older than this are refreshed on load.</summary>
    public static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopes;
    private readonly IDbContextFactory<WeddingMusicContext> _factory;

    public StreamingCacheRefresher(IServiceScopeFactory scopes, IDbContextFactory<WeddingMusicContext> factory)
    {
        _scopes = scopes;
        _factory = factory;
    }

    public async Task<int> RefreshStaleAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - MaxCacheAge;

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Streaming-backed tracks are those with an ExternalUri and no local file cached.
        // Refresh only those whose cache is missing or older than the cutoff.
        // Note: SQLite cannot translate DateTimeOffset comparisons, so the cutoff
        // check runs client-side on the (small) streaming-backed subset.
        var candidates = await db.Tracks
            .Where(t => t.ExternalUri != null && !t.IsCachedOffline)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var stale = candidates
            .Where(t => t.LastIngestedUtc == null || t.LastIngestedUtc < cutoff)
            .ToList();

        if (stale.Count == 0)
            return 0;

        // Resolvers are typed HttpClient consumers -> resolve them inside a scope.
        using var scope = _scopes.CreateScope();
        var resolvers = scope.ServiceProvider.GetServices<IStreamingMetadataResolver>().ToList();
        if (resolvers.Count == 0)
            return 0;

        int refreshed = 0;
        foreach (var track in stale)
        {
            ct.ThrowIfCancellationRequested();
            var uri = track.ExternalUri!;

            foreach (var resolver in resolvers)
            {
                StreamingMetadata? meta;
                try
                {
                    meta = await resolver.ResolveAsync(uri, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    continue; // try the next resolver; a failing provider must not block others.
                }

                if (meta is null)
                    continue; // this resolver doesn't handle that URI.

                track.Title = meta.Title;
                track.Artist = meta.Artist;
                track.Album = meta.Album;
                track.LastIngestedUtc = DateTimeOffset.UtcNow;
                refreshed++;
                break; // first resolver that handled it wins.
            }
        }

        if (refreshed > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return refreshed;
    }
}
