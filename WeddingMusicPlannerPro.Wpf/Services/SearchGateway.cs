using Microsoft.Extensions.DependencyInjection;
using WeddingMusicSearch;
using WeddingMusicSearch.Abstractions;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// UI-facing entry point for unified search. Creates a DI scope per query so the
/// scoped <c>LocalSearchService</c> (and its <c>WeddingMusicContext</c>) resolve
/// correctly alongside the singleton/typed-client remote providers.
/// </summary>
public interface ISearchGateway
{
    Task<AggregateSearchResponse> SearchAsync(SearchQuery query, CancellationToken ct = default);
}

public sealed class SearchGateway : ISearchGateway
{
    private readonly IServiceScopeFactory _scopes;

    public SearchGateway(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<AggregateSearchResponse> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var aggregate = scope.ServiceProvider.GetRequiredService<AggregateSearchService>();
        return await aggregate.SearchDetailedAsync(query, ct).ConfigureAwait(false);
    }
}
