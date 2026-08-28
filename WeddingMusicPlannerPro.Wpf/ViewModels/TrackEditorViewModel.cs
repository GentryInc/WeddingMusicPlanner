using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeddingMusicData.Models;
using WeddingMusicPlannerPro.Wpf.Services;

namespace WeddingMusicPlannerPro.Wpf.ViewModels;

/// <summary>
/// View-model for the per-song properties dialog opened by double-clicking a track
/// in a playlist. Surfaces the full <see cref="Track"/> metadata and
/// <see cref="CueSettings"/> (including the fade-out mechanic that replaced the old
/// toolbar control) and persists them atomically via
/// <see cref="ILibraryService.SaveTrackEditAsync"/>.
///
/// All bindable members are hand-written <c>SetProperty</c> properties rather than
/// <c>[ObservableProperty]</c>: the source generator has proven unreliable in this
/// project (it silently skipped members, causing CS0103), so we don't depend on it.
/// </summary>
public partial class TrackEditorViewModel : ObservableObject
{
    private readonly ILibraryService _library;
    private readonly TrackViewModel _track;

    public TrackEditorViewModel(ILibraryService library, TrackViewModel track)
    {
        _library = library;
        _track = track;

        var m = track.Model;
        _title = m.Title;
        _artist = m.Artist ?? string.Empty;
        _album = m.Album ?? string.Empty;
        _genre = m.Genre ?? string.Empty;
        _year = m.Year;
        _trackNumber = m.TrackNumber;
        _musicalKey = m.MusicalKey ?? string.Empty;
        _mood = m.Mood ?? string.Empty;
        _energy = m.Energy;
        _danceability = m.Danceability;
        _bpm = m.Bpm;
    }

    /// <summary>Dialog window title.</summary>
    public string HeaderText => $"Edit \u2013 {_title}";

    /// <summary>The song's total length, shown read-only as guidance for cue times.</summary>
    public string DurationDisplay => _track.DurationDisplay;

    /// <summary>Available fade envelope shapes for the curve pickers.</summary>
    public IReadOnlyList<FadeCurve> FadeCurves { get; } =
        new[] { FadeCurve.Linear, FadeCurve.Logarithmic, FadeCurve.EqualPower, FadeCurve.SCurve };

    /// <summary>True once the user saved, so the caller knows to reload the playlist.</summary>
    public bool Saved { get; private set; }

    // --- General ---
    private string? _errorMessage;
    /// <summary>Set when the last save attempt failed validation or persistence.</summary>
    public string? ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    // --- Metadata ---
    private string _title;
    public string Title
    {
        get => _title;
        set { if (SetProperty(ref _title, value)) OnPropertyChanged(nameof(HeaderText)); }
    }

    private string _artist;
    public string Artist { get => _artist; set => SetProperty(ref _artist, value); }

    private string _album;
    public string Album { get => _album; set => SetProperty(ref _album, value); }

    private string _genre;
    public string Genre { get => _genre; set => SetProperty(ref _genre, value); }

    private int? _year;
    public int? Year { get => _year; set => SetProperty(ref _year, value); }

    private int? _trackNumber;
    public int? TrackNumber { get => _trackNumber; set => SetProperty(ref _trackNumber, value); }

    private string _musicalKey;
    public string MusicalKey { get => _musicalKey; set => SetProperty(ref _musicalKey, value); }

    private string _mood;
    public string Mood { get => _mood; set => SetProperty(ref _mood, value); }

    private double? _energy;
    public double? Energy { get => _energy; set => SetProperty(ref _energy, value); }

    private double? _danceability;
    public double? Danceability { get => _danceability; set => SetProperty(ref _danceability, value); }

    private double? _bpm;
    public double? Bpm { get => _bpm; set => SetProperty(ref _bpm, value); }

    // --- Cue / fade (times exposed as friendly m:ss text) ---
    private string _startAt = string.Empty;
    public string StartAt { get => _startAt; set => SetProperty(ref _startAt, value); }

    private string _fadeOutAt = string.Empty;
    public string FadeOutAt { get => _fadeOutAt; set => SetProperty(ref _fadeOutAt, value); }

    private double _fadeInSeconds;
    public double FadeInSeconds { get => _fadeInSeconds; set => SetProperty(ref _fadeInSeconds, value); }

    private double _fadeOutSeconds = 2;
    public double FadeOutSeconds { get => _fadeOutSeconds; set => SetProperty(ref _fadeOutSeconds, value); }

    private FadeCurve _fadeInCurve = FadeCurve.EqualPower;
    public FadeCurve FadeInCurve { get => _fadeInCurve; set => SetProperty(ref _fadeInCurve, value); }

    private FadeCurve _fadeOutCurve = FadeCurve.EqualPower;
    public FadeCurve FadeOutCurve { get => _fadeOutCurve; set => SetProperty(ref _fadeOutCurve, value); }

    private double _gainTrimDb;
    public double GainTrimDb { get => _gainTrimDb; set => SetProperty(ref _gainTrimDb, value); }

    /// <summary>Loads the saved cue so the dialog reflects existing fade/mix settings.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var cue = await _library.GetCueAsync(_track.TrackId).ConfigureAwait(true);
            if (cue is null)
                return;

            StartAt = cue.StartMs > 0 ? FormatMs(cue.StartMs) : string.Empty;
            FadeOutAt = cue.StopMs is { } stop ? FormatMs(stop) : string.Empty;
            FadeInSeconds = Math.Max(0, cue.FadeInMs / 1000.0);
            FadeOutSeconds = Math.Max(0, cue.FadeOutMs / 1000.0);
            FadeInCurve = cue.FadeInCurve;
            FadeOutCurve = cue.FadeOutCurve;
            GainTrimDb = cue.GainTrimDb;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load cue: {ex.Message}";
        }
    }

    /// <summary>Validates, persists the metadata + cue, and flags <see cref="Saved"/> on success.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Title can't be empty.";
            return;
        }

        if (!TryParseTime(StartAt, out var startMs))
        {
            ErrorMessage = "Start time must be m:ss (e.g. 0:08) or blank.";
            return;
        }

        if (!TryParseTime(FadeOutAt, out var stopMs))
        {
            ErrorMessage = "Fade-out time must be m:ss (e.g. 3:10) or blank to play to the end.";
            return;
        }

        var lengthMs = _track.DurationMs;
        if (stopMs is { } stop && lengthMs > 0 && stop > lengthMs)
        {
            ErrorMessage = $"Fade-out {FadeOutAt} is past the song length ({DurationDisplay}).";
            return;
        }

        if (startMs is { } start && stopMs is { } outro && start >= outro)
        {
            ErrorMessage = "Start time must come before the fade-out time.";
            return;
        }

        var edit = new TrackEdit
        {
            TrackId = _track.TrackId,
            Title = Title,
            Artist = NullIfBlank(Artist),
            Album = NullIfBlank(Album),
            Genre = NullIfBlank(Genre),
            Year = Year,
            TrackNumber = TrackNumber,
            MusicalKey = NullIfBlank(MusicalKey),
            Mood = NullIfBlank(Mood),
            Energy = Energy,
            Danceability = Danceability,
            Bpm = Bpm,
            StartMs = startMs ?? 0,
            StopMs = stopMs,
            FadeInMs = (int)Math.Round(Math.Max(0, FadeInSeconds) * 1000),
            FadeOutMs = (int)Math.Round(Math.Max(0, FadeOutSeconds) * 1000),
            FadeInCurve = FadeInCurve,
            FadeOutCurve = FadeOutCurve,
            GainTrimDb = GainTrimDb,
        };

        try
        {
            await _library.SaveTrackEditAsync(edit).ConfigureAwait(true);

            // Reflect the metadata edits back into the live playlist row.
            _track.Title = edit.Title!;
            _track.Artist = edit.Artist ?? "Unknown Artist";
            _track.Bpm = edit.Bpm;
            _track.Model.Album = edit.Album;
            _track.Model.Genre = edit.Genre;
            _track.Model.Year = edit.Year;
            _track.Model.TrackNumber = edit.TrackNumber;
            _track.Model.MusicalKey = edit.MusicalKey;
            _track.Model.Mood = edit.Mood;
            _track.Model.Energy = edit.Energy;
            _track.Model.Danceability = edit.Danceability;

            Saved = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't save: {ex.Message}";
        }
    }

    /// <summary>Closes the dialog without saving.</summary>
    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised when the dialog should close (after Save or Cancel).</summary>
    public event EventHandler? RequestClose;

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Parses "m:ss" / "mm:ss" (or plain seconds) to milliseconds. Blank => null.</summary>
    private static bool TryParseTime(string? text, out long? ms)
    {
        ms = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        text = text.Trim();
        var parts = text.Split(':');

        if (parts.Length == 2
            && int.TryParse(parts[0], out var mm)
            && double.TryParse(parts[1], out var ss)
            && mm >= 0 && ss >= 0 && ss < 60)
        {
            ms = (long)Math.Round((mm * 60 + ss) * 1000);
            return true;
        }

        if (parts.Length == 1 && double.TryParse(text, out var secs) && secs >= 0)
        {
            ms = (long)Math.Round(secs * 1000);
            return true;
        }

        return false;
    }

    private static string FormatMs(long ms) => TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss");
}
