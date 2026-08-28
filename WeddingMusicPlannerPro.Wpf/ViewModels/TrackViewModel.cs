using CommunityToolkit.Mvvm.ComponentModel;
using WeddingMusicData.Models;

namespace WeddingMusicPlannerPro.Wpf.ViewModels;

/// <summary>Lightweight, bindable wrapper over a <see cref="Track"/>.</summary>
public partial class TrackViewModel : ObservableObject
{
    public TrackViewModel(Track track)
    {
        Model = track;
        _title = track.Title;
        _artist = track.Artist ?? "Unknown Artist";
        _bpm = track.Bpm;
        _durationMs = track.DurationMs;
    }

    public Track Model { get; }
    public int TrackId => Model.Id;

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _artist;
    [ObservableProperty] private double? _bpm;
    [ObservableProperty] private long _durationMs;
    [ObservableProperty] private bool _isPlaying;

    public string DurationDisplay => TimeSpan.FromMilliseconds(DurationMs).ToString(@"m\:ss");
    public string BpmDisplay => Bpm is { } b ? $"{b:0} BPM" : "—";

    /// <summary>Accessible label read by screen readers for the whole row.</summary>
    public string AccessibleName => $"{Title} by {Artist}, {DurationDisplay}{(Bpm is { } b ? $", {b:0} beats per minute" : "")}";
}
