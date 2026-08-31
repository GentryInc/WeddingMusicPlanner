using Microsoft.Extensions.DependencyInjection;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Trending;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// UI-facing entry point for "what's trending now". Creates a DI scope per call so
/// the typed-HttpClient trending providers resolve correctly, then merges every
/// configured provider's chart into a single ranked list. Mirrors
/// <see cref="SearchGateway"/> so a singleton view model never captures a
/// transient typed client.
/// </summary>
public interface ITrendingGateway
{
    Task<IReadOnlyList<SearchResult>> GetTrendingAsync(TrendingQuery query, CancellationToken ct = default);
}

public sealed class TrendingGateway : ITrendingGateway
{
    private readonly IServiceScopeFactory _scopes;

    public TrendingGateway(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<IReadOnlyList<SearchResult>> GetTrendingAsync(TrendingQuery query, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var providers = scope.ServiceProvider.GetServices<ITrendingService>().ToList();
        if (providers.Count == 0)
            return Array.Empty<SearchResult>();

        // Fan out concurrently; a slow/failing provider must not sink the others.
        var tasks = providers.Select(async p =>
        {
            try
            {
                return await p.GetTrendingAsync(query, ct).ConfigureAwait(false);
            }
            catch
            {
                return (IReadOnlyList<SearchResult>)Array.Empty<SearchResult>();
            }
        });

        var completed = await Task.WhenAll(tasks).ConfigureAwait(false);

        return completed
            .SelectMany(r => r)
            .OrderByDescending(r => r.Relevance ?? 0)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
