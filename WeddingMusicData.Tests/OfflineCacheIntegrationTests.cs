using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WeddingMusicData;
using WeddingMusicData.Caching;
using WeddingMusicData.Models;
using Xunit;

namespace WeddingMusicData.Tests;

/// <summary>
/// Integration tests for the offline caching + fail-safe layer. Uses a real
/// temp-file SQLite database and a faultable HTTP handler to simulate a sudden
/// network dropout, verifying playback resolution falls back to the cached buffer.
/// </summary>
public sealed class OfflineCacheIntegrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"wmp_{Guid.NewGuid():N}.db");
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"wmp_cache_{Guid.NewGuid():N}");
    private readonly IDbContextFactory<WeddingMusicContext> _factory;

    public OfflineCacheIntegrationTests()
    {
        Directory.CreateDirectory(_cacheDir);
        _factory = new PooledDbContextFactory<WeddingMusicContext>(
            new DbContextOptionsBuilder<WeddingMusicContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options);

        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    private (OfflineCacheService cache, FaultableHttpHandler handler) CreateCache(byte[] payload)
    {
        var handler = new FaultableHttpHandler(payload);
        var http = new HttpClient(handler);
        return (new OfflineCacheService(_factory, http, _cacheDir), handler);
    }

    private async Task<int> SeedRemoteTrackAsync()
    {
        await using var db = _factory.CreateDbContext();
        var track = new Track
        {
            FilePath = $"remote-only-{Guid.NewGuid():N}", // no local file on disk
            ExternalUri = "https://cdn.example.com/first-dance.mp3",
            Title = "First Dance",
            DurationMs = 180000
        };
        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        return track.Id;
    }

    [Fact]
    public async Task PreCache_DownloadsStream_AndFlagsTrackOffline()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var (cache, _) = CreateCache(payload);
        var trackId = await SeedRemoteTrackAsync();

        var localPath = await cache.EnsureCachedAsync(trackId);

        Assert.NotNull(localPath);
        Assert.True(File.Exists(localPath!));
        Assert.Equal(payload, await File.ReadAllBytesAsync(localPath!));

        await using var db = _factory.CreateDbContext();
        var track = await db.Tracks.FindAsync(trackId);
        Assert.True(track!.IsCachedOffline);
        Assert.Equal(localPath, track.CachedFilePath);
    }

    [Fact]
    public async Task NetworkDropout_AfterCaching_ResolvesToLocalBuffer()
    {
        var payload = new byte[] { 10, 20, 30 };
        var (cache, handler) = CreateCache(payload);
        var trackId = await SeedRemoteTrackAsync();

        // 1. Pre-cache while the network is up (what the background worker does).
        var cachedPath = await cache.EnsureCachedAsync(trackId);
        Assert.NotNull(cachedPath);

        // 2. The network drops out mid-event.
        handler.NetworkDown = true;

        // 3. Resolving the source must still return the local cached buffer — no network hit.
        await using var db = _factory.CreateDbContext();
        var track = await db.Tracks.FindAsync(trackId);
        var resolved = cache.ResolveLocalPath(track!);

        Assert.Equal(cachedPath, resolved);
        Assert.True(File.Exists(resolved!));
    }

    [Fact]
    public async Task NetworkDown_WithoutPriorCache_FailsGracefully_NoFlag()
    {
        var (cache, handler) = CreateCache(new byte[] { 9 });
        var trackId = await SeedRemoteTrackAsync();
        handler.NetworkDown = true;

        var result = await cache.EnsureCachedAsync(trackId);

        Assert.Null(result); // could not cache
        await using var db = _factory.CreateDbContext();
        var track = await db.Tracks.FindAsync(trackId);
        Assert.False(track!.IsCachedOffline);
        Assert.Null(cache.ResolveLocalPath(track));
    }

    [Fact]
    public async Task EnsureCached_IsIdempotent_DoesNotReDownload()
    {
        var (cache, handler) = CreateCache(new byte[] { 7, 7 });
        var trackId = await SeedRemoteTrackAsync();

        await cache.EnsureCachedAsync(trackId);
        int afterFirst = handler.RequestCount;

        await cache.EnsureCachedAsync(trackId); // already cached → no new request

        Assert.Equal(afterFirst, handler.RequestCount);
    }

    [Fact]
    public async Task CacheSections_CachesAllUncachedTracksInBucket()
    {
        var (cache, _) = CreateCache(new byte[] { 42 });

        int sectionId;
        await using (var db = _factory.CreateDbContext())
        {
            var section = new PlaylistSection { Name = "Prelude", Order = 0 };
            db.PlaylistSections.Add(section);
            await db.SaveChangesAsync();
            sectionId = section.Id;

            for (int i = 0; i < 3; i++)
            {
                var track = new Track
                {
                    FilePath = $"remote-{i}-{Guid.NewGuid():N}",
                    ExternalUri = $"https://cdn.example.com/track{i}.mp3",
                    Title = $"Track {i}"
                };
                db.Tracks.Add(track);
                await db.SaveChangesAsync();
                db.PlaylistItems.Add(new PlaylistItem { PlaylistSectionId = sectionId, TrackId = track.Id, Position = i });
            }
            await db.SaveChangesAsync();
        }

        int cached = await cache.CacheSectionsAsync(new[] { sectionId });

        Assert.Equal(3, cached);
        await using var verify = _factory.CreateDbContext();
        Assert.True(await verify.Tracks.Where(t => t.ExternalUri != null).AllAsync(t => t.IsCachedOffline));
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, true); } catch { }
    }
}
