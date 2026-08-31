using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using WeddingMusicData;
using WeddingMusicData.Caching;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// Background worker that proactively caches the upcoming timeline buckets to
/// local disk before playback, so a network dropout can never interrupt the show.
/// Runs on an interval, always keeping a look-ahead window of sections cached.
/// </summary>
public sealed class TimelineCacheWorker : BackgroundService
{
    private readonly IDbContextFactory<WeddingMusicContext> _dbFactory;
    private readonly IOfflineCacheService _cache;
    private readonly TimeSpan _interval;
    private readonly int _lookAheadSections;

    public TimelineCacheWorker(
        IDbContextFactory<WeddingMusicContext> dbFactory,
        IOfflineCacheService cache,
        TimeSpan? interval = null,
        int lookAheadSections = 2)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _interval = interval ?? TimeSpan.FromSeconds(30);
        _lookAheadSections = lookAheadSections;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CacheUpcomingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Never let a transient error kill the worker; retry next tick.
            }

            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Caches the next <see cref="_lookAheadSections"/> sections that still have un-cached tracks.</summary>
    internal async Task CacheUpcomingAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var upcomingSectionIds = await db.PlaylistSections
            .OrderBy(s => s.Order)
            .Where(s => s.Items.Any(i => i.Track != null && !i.Track.IsCachedOffline))
            .Select(s => s.Id)
            .Take(_lookAheadSections)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (upcomingSectionIds.Count > 0)
            await _cache.CacheSectionsAsync(upcomingSectionIds, ct).ConfigureAwait(false);
    }
}
