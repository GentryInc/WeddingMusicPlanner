using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeddingMusicPlannerPro.Wpf.Services;
using WeddingMusicSearch.Abstractions;
using WeddingMusicSearch.Trending;

namespace WeddingMusicPlannerPro.Wpf.ViewModels;

/// <summary>
/// Drives the unified search panel: fans a query across the local library and every
/// configured third-party provider (Spotify/Apple/etc.), surfaces per-source status,
/// and raises <see cref="AddRequested"/> when the operator wants to add a hit.
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly ISearchGateway _gateway;
    private readonly ITrendingGateway _trending;
    private CancellationTokenSource? _inFlight;

    public SearchViewModel(ISearchGateway gateway, ITrendingGateway trending)
    {
        _gateway = gateway;
        _trending = trending;
    }

    /// <summary>Unfiltered/unsorted results as returned by the last search or trending load.</summary>
    private readonly List<SearchResultViewModel> _allResults = new();

    public ObservableCollection<SearchResultViewModel> Results { get; } = new();

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private string _sourceStatus = string.Empty;
    [ObservableProperty] private bool _isSearching;

    // Optional smart filters.
    [ObservableProperty] private string? _mood;
    [ObservableProperty] private double? _minBpm;
    [ObservableProperty] private double? _maxBpm;

    /// <summary>Genre to match (case-insensitive, substring). Empty = any genre.</summary>
    [ObservableProperty] private string? _genre;

    /// <summary>Inclusive release-year range bounds. Null = no constraint.</summary>
    [ObservableProperty] private int? _minYear;
    [ObservableProperty] private int? _maxYear;

    /// <summary>Selected source filter; "All" (the default) applies no source constraint.</summary>
    [ObservableProperty] private string _selectedSource = AllSourcesOption;

    /// <summary>Selected result ordering.</summary>
    [ObservableProperty] private ResultSort _selectedSort = ResultSort.Relevance;

    private const string AllSourcesOption = "All";

    /// <summary>Source options for the filter dropdown: "All" plus every known source.</summary>
    public IReadOnlyList<string> SourceOptions { get; } =
        new[] { AllSourcesOption }
            .Concat(Enum.GetNames(typeof(SearchSource)))
            .ToArray();

    /// <summary>Sort options for the ordering dropdown.</summary>
    public IReadOnlyList<ResultSort> SortOptions { get; } =
        (ResultSort[])Enum.GetValues(typeof(ResultSort));

    // Re-apply the client-side view whenever a filter/sort selection changes so the
    // list updates instantly without re-querying the providers.
    partial void OnSelectedSourceChanged(string value) => ApplyView();
    partial void OnSelectedSortChanged(ResultSort value) => ApplyView();
    partial void OnGenreChanged(string? value) => ApplyView();
    partial void OnMinYearChanged(int? value) => ApplyView();
    partial void OnMaxYearChanged(int? value) => ApplyView();

    /// <summary>Raised when the operator asks to add a search hit to the playlist.</summary>
    public event EventHandler<SearchResult>? AddRequested;

    [RelayCommand]
    private async Task SearchAsync()
    {
        var text = Query?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            SourceStatus = "Enter a song or artist to search.";
            return;
        }

        // Cancel any previous in-flight search so results never interleave.
        _inFlight?.Cancel();
        _inFlight = new CancellationTokenSource();
        var ct = _inFlight.Token;

        IsSearching = true;
        SourceStatus = "Searching\u2026";
        try
        {
            var query = new SearchQuery
            {
                Text = text,
                Mood = string.IsNullOrWhiteSpace(Mood) ? null : Mood,
                Genres = string.IsNullOrWhiteSpace(Genre)
                    ? Array.Empty<string>()
                    : new[] { Genre.Trim() },
                MinBpm = MinBpm,
                MaxBpm = MaxBpm,
                MinYear = MinYear,
                MaxYear = MaxYear,
                Limit = 30
            };

            var response = await _gateway.SearchAsync(query, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
                return;

            _allResults.Clear();
            foreach (var hit in response.Results)
                _allResults.Add(new SearchResultViewModel(hit));
            ApplyView();

            SourceStatus = FormatStatuses(response);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search; ignore.
        }
        catch (Exception ex)
        {
            SourceStatus = $"Search error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void Add(SearchResultViewModel? result)
    {
        if (result is not null)
            AddRequested?.Invoke(this, result.Model);
    }

    /// <summary>
    /// Replaces the results list with currently-trending tracks from every configured
    /// provider, so the operator can browse and add popular songs using the same
    /// Add flow as search. Cancels any in-flight search first.
    /// </summary>
    [RelayCommand]
    private async Task LoadTrendingAsync()
    {
        _inFlight?.Cancel();
        _inFlight = new CancellationTokenSource();
        var ct = _inFlight.Token;

        IsSearching = true;
        SourceStatus = "Loading trending\u2026";
        try
        {
            var results = await _trending.GetTrendingAsync(new TrendingQuery(), ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
                return;

            _allResults.Clear();
            foreach (var hit in results)
                _allResults.Add(new SearchResultViewModel(hit));
            ApplyView();

            SourceStatus = results.Count == 0
                ? "No trending results. Configure a provider (e.g. YouTube:ApiKey) to enable trending."
                : $"{results.Count} trending track(s)";
        }
        catch (OperationCanceledException)
        {
            // Superseded; ignore.
        }
        catch (Exception ex)
        {
            SourceStatus = $"Trending error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    /// Rebuilds the visible <see cref="Results"/> from <see cref="_allResults"/> by
    /// applying the current source/genre filters and the selected sort order. This is
    /// purely client-side so toggling a filter or sort is instant and never re-queries
    /// the providers.
    /// </summary>
    private void ApplyView()
    {
        IEnumerable<SearchResultViewModel> view = _allResults;

        // Source filter ("All" imposes no constraint).
        if (!string.Equals(SelectedSource, AllSourcesOption, StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<SearchSource>(SelectedSource, out var source))
        {
            view = view.Where(r => r.Model.Source == source);
        }

        // Genre filter: substring match on any of the result's genres.
        if (!string.IsNullOrWhiteSpace(Genre))
        {
            var g = Genre.Trim();
            view = view.Where(r => r.Model.Genres.Any(
                x => x.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        // Year filter: only constrains results whose year is known.
        if (MinYear is { } minYear)
            view = view.Where(r => r.Model.Year is not { } y || y >= minYear);
        if (MaxYear is { } maxYear)
            view = view.Where(r => r.Model.Year is not { } y || y <= maxYear);

        view = SelectedSort switch
        {
            ResultSort.TitleAsc => view.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
            ResultSort.TitleDesc => view.OrderByDescending(r => r.Title, StringComparer.OrdinalIgnoreCase),
            ResultSort.ArtistAsc => view.OrderBy(r => r.Artist ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                                        .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
            ResultSort.BpmAsc => view.OrderBy(r => r.Model.Bpm ?? double.MaxValue),
            ResultSort.DurationAsc => view.OrderBy(r => r.Model.DurationMs ?? long.MaxValue),
            ResultSort.YearAsc => view.OrderBy(r => r.Model.Year ?? int.MaxValue),
            ResultSort.Source => view.OrderBy(r => r.SourceDisplay, StringComparer.OrdinalIgnoreCase)
                                     .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
            // Relevance: highest provider score first, unknown scores last.
            _ => view.OrderByDescending(r => r.Model.Relevance ?? double.MinValue),
        };

        Results.Clear();
        foreach (var r in view)
            Results.Add(r);
    }

    private static string FormatStatuses(AggregateSearchResponse response)
    {
        var parts = response.SourceStatuses
            .OrderBy(kvp => kvp.Key.ToString())
            .Select(kvp =>
            {
                var s = kvp.Value;
                return s.Succeeded
                    ? $"{kvp.Key}: {s.Count}"
                    : $"{kvp.Key}: {(s.Error ?? "failed")}";
            });
        return $"{response.Results.Count} result(s)  —  " + string.Join("  |  ", parts);
    }
}

/// <summary>Ordering options for the unified search results list.</summary>
public enum ResultSort
{
    /// <summary>Provider relevance score, highest first (default).</summary>
    Relevance,
    /// <summary>Title A→Z.</summary>
    TitleAsc,
    /// <summary>Title Z→A.</summary>
    TitleDesc,
    /// <summary>Artist A→Z, then title.</summary>
    ArtistAsc,
    /// <summary>Tempo, slowest first.</summary>
    BpmAsc,
    /// <summary>Duration, shortest first.</summary>
    DurationAsc,
    /// <summary>Release year, oldest first.</summary>
    YearAsc,
    /// <summary>Group by source.</summary>
    Source
}
