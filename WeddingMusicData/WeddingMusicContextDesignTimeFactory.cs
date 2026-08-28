using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WeddingMusicData;

/// <summary>
/// Enables the EF Core tools (e.g. <c>dotnet ef migrations add</c>) to construct
/// the context at design time, since <see cref="WeddingMusicContext"/> has no
/// parameterless constructor and the runtime wires it up via the host container.
/// </summary>
public sealed class WeddingMusicContextDesignTimeFactory
    : IDesignTimeDbContextFactory<WeddingMusicContext>
{
    public WeddingMusicContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<WeddingMusicContext>();
        // A throwaway design-time data source; no data is read/written during scaffolding.
        builder.UseSqlite("Data Source=design_time.db");
        return new WeddingMusicContext(builder.Options);
    }
}
