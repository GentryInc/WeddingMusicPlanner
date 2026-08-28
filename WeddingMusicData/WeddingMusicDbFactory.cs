using Microsoft.EntityFrameworkCore;

namespace WeddingMusicData;

/// <summary>
/// Factory that builds a resilient, live-event-tuned SQLite context.
/// Applies WAL journaling and a busy timeout so reads never block the operator
/// mid-song, and ensures the schema/migrations are present.
/// </summary>
public static class WeddingMusicDbFactory
{
    /// <summary>Builds options for a SQLite file at <paramref name="databasePath"/>.</summary>
    public static DbContextOptions<WeddingMusicContext> BuildOptions(string databasePath)
    {
        var builder = new DbContextOptionsBuilder<WeddingMusicContext>();
        builder.UseSqlite($"Data Source={databasePath};Cache=Shared");
        return builder.Options;
    }

    /// <summary>
    /// Creates a context, ensures the database exists, and applies live-event
    /// PRAGMAs (WAL journal, NORMAL sync, busy timeout) for resilience.
    /// </summary>
    public static WeddingMusicContext CreateInitialized(string databasePath)
    {
        var ctx = new WeddingMusicContext(BuildOptions(databasePath));

        // Apply migrations (and adopt any legacy EnsureCreated database) so the
        // schema always matches the current model.
        DatabaseInitializer.InitializeAsync(ctx).GetAwaiter().GetResult();

        // WAL lets a reader (playback UI) and writer (ingestion) coexist without stalls.
        ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        ctx.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        ctx.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
        ctx.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
        return ctx;
    }
}
