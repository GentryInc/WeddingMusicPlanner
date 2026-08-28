using CommunityToolkit.Mvvm.ComponentModel;
using WeddingMusicSearch.Abstractions;

namespace WeddingMusicPlannerPro.Wpf.ViewModels;

/// <summary>Presentation wrapper around a unified <see cref="SearchResult"/>.</summary>
public sealed class SearchResultViewModel : ObservableObject
{
    public SearchResultViewModel(SearchResult model) => Model = model;

    public SearchResult Model { get; }

    public string Title => Model.Title;
    public string? Artist => Model.Artist;
    public string? Album => Model.Album;
    public string SourceDisplay => Model.Source.ToString();
    public bool IsLocal => Model.Source == SearchSource.Local;

    public string SubtitleDisplay
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Artist)) parts.Add(Artist!);
            if (Model.Year is { } year) parts.Add(year.ToString());
            if (Model.Bpm is { } bpm) parts.Add($"{bpm:0} BPM");
            if (Model.DurationMs is { } ms) parts.Add(TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss"));
            return string.Join("  •  ", parts);
        }
    }

    public string AccessibleName =>
        $"{Title} by {Artist ?? "unknown artist"}, from {SourceDisplay}";
}
