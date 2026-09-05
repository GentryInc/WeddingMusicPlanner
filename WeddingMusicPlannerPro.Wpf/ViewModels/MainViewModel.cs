using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WeddingMusicData.Models;
using WeddingMusicPlannerPro.Wpf.Services;
using WeddingMusicPlannerPro.Wpf.Views;
using WeddingMusicSearch.Abstractions;

namespace WeddingMusicPlannerPro.Wpf.ViewModels;

/// <summary>
/// Root ViewModel: owns the event-segment buckets, playback transport, and the
/// safety-critical Lockout Mode. Uses CommunityToolkit.Mvvm source generators for
/// observable properties and relay commands.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ILibraryService _library;
    private readonly IPlaybackService _playback;
    private readonly IImportService _import;
    private readonly IStreamingCacheRefresher _cacheRefresher;
    private readonly ISongRequestService _songRequests;
    private readonly IRequestWebServer _requestServer;
    private readonly IPortForwardingService _portForwarding;
    private readonly ISettingsService _settings;
    private readonly IPlayHistoryService _playHistory;
    private readonly Func<SettingsWindow> _settingsWindowFactory;

    // Tracks what is currently on the master deck so we can auto-advance.
    private EventSegmentViewModel? _currentSegment;
    private TrackViewModel? _currentTrack;

    public MainViewModel(ILibraryService library, IPlaybackService playback, IImportService import, IStreamingCacheRefresher cacheRefresher, ISongRequestService songRequests, IRequestWebServer requestServer, IPortForwardingService portForwarding, ISettingsService settings, IPlayHistoryService playHistory, SearchViewModel search, Func<SettingsWindow> settingsWindowFactory)
    {
        _library = library;
        _playback = playback;
        _import = import;
        _cacheRefresher = cacheRefresher;
        _songRequests = songRequests;
        _requestServer = requestServer;
        _portForwarding = portForwarding;
        _settings = settings;
        _playHistory = playHistory;
        _settingsWindowFactory = settingsWindowFactory;
        Search = search;
        Search.AddRequested += async (_, hit) =>
        {
            if (Application.Current?.Dispatcher is { } d)
                await d.InvokeAsync(async () => await AddSearchHitAsync(hit).ConfigureAwait(true));
        };
        _playback.StateChanged += (_, _) => Application.Current?.Dispatcher.Invoke(OnPlaybackStateChanged);
        _playback.MicLiveChanged += (_, _) => Application.Current?.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(IsMicLive));
            OnPropertyChanged(nameof(MasterVolume));
        });
        _playback.MasterTrackCompleted += (_, reason) =>
            Application.Current?.Dispatcher.Invoke(() => OnMasterTrackCompleted(reason));

        // Live waveform for the lockout screen: roll the newest peak level into a
        // fixed-size buffer and re-render as polyline points (~20 updates/sec).
        _playback.MasterLevelMeasured += (_, level) =>
            Application.Current?.Dispatcher.BeginInvoke(() => PushWaveformLevel(level));

        // Bride's VIP "skip to next song" from the request page: reuse the emergency
        // fade path, which always fades out gracefully and auto-advances.
        _requestServer.SkipRequested += (_, _) =>
            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                StatusMessage = "VIP skip requested \u2014 fading to next song\u2026";
                await _playback.EmergencyFadeAsync().ConfigureAwait(true);
            });

        // Bride's VIP pause from the request page.
        _requestServer.PauseRequested += (_, _) =>
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                _playback.Pause();
                StatusMessage = "VIP pause requested";
            });

        // Bride's VIP play/resume from the request page.
        _requestServer.PlayRequested += (_, _) =>
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                _playback.Resume();
                StatusMessage = "VIP play requested";
            });

        // Playhead poll runs for the app lifetime; position/duration read as zero when
        // idle, so this stays cheap and needs no start/stop coupling to transport state.
        StartPlayheadTimer();
        StartRequestPolling();
        RefreshMicDevices();
    }

    public ObservableCollection<EventSegmentViewModel> Segments { get; } = new();

    /// <summary>The unified (local + third-party) search panel.</summary>
    public SearchViewModel Search { get; }

    [ObservableProperty] private TrackViewModel? _selectedTrack;
    [ObservableProperty] private EventSegmentViewModel? _selectedSegment;
    [ObservableProperty] private string? _nowPlaying;
    [ObservableProperty] private string _statusMessage = "Ready";

    // --- DJ microphone (talkover) --------------------------------------------

    // --- Lockout waveform: scrolling peak-level history rendered as a polyline ---

    private const int WaveformSamples = 120;
    private const double WaveformWidth = 480;
    private const double WaveformHeight = 80;
    private readonly float[] _waveformLevels = new float[WaveformSamples];

    /// <summary>Polyline points for the lockout-screen waveform (newest sample on the right).</summary>
    [ObservableProperty] private System.Windows.Media.PointCollection _waveformPoints = new();

    private void PushWaveformLevel(float level)
    {
        // Only spend cycles building geometry while the lockout overlay shows it.
        Array.Copy(_waveformLevels, 1, _waveformLevels, 0, WaveformSamples - 1);
        _waveformLevels[WaveformSamples - 1] = Math.Clamp(level, 0f, 1f);
        if (!IsLockoutEnabled) return;

        var pts = new System.Windows.Media.PointCollection();
        double midY = WaveformHeight / 2;
        for (int i = 0; i < WaveformSamples; i++)
        {
            double x = i * WaveformWidth / (WaveformSamples - 1);
            // Mirror around the vertical centre for a classic waveform look.
            double amp = _waveformLevels[i] * midY;
            pts.Add(new System.Windows.Point(x, midY - ((i & 1) == 0 ? amp : -amp)));
        }
        pts.Freeze();
        WaveformPoints = pts;
    }

    /// <summary>Available capture devices for the mic picker.</summary>
    public ObservableCollection<MicDeviceViewModel> MicDevices { get; } = new();

    [ObservableProperty] private MicDeviceViewModel? _selectedMicDevice;

    /// <summary>True while the DJ mic is live; music ducks automatically.</summary>
    public bool IsMicLive => _playback.IsMicLive;

    /// <summary>
    /// Toggles the DJ microphone on/off. While live the mic plays through the
    /// speakers and the music is ducked; toggling off restores the volume.
    /// </summary>
    [RelayCommand]
    private void ToggleMic()
    {
        var error = _playback.ToggleMicrophone(SelectedMicDevice?.Id);
        OnPropertyChanged(nameof(IsMicLive));
        OnPropertyChanged(nameof(MasterVolume));
        StatusMessage = error ?? (IsMicLive ? "Mic live \u2014 music ducked" : "Mic off \u2014 music restored");
    }

    /// <summary>Refreshes the list of microphone capture devices.</summary>
    [RelayCommand]
    private void RefreshMicDevices()
    {
        var selectedId = SelectedMicDevice?.Id;
        MicDevices.Clear();
        foreach (var (id, name) in _playback.GetMicrophoneDevices())
            MicDevices.Add(new MicDeviceViewModel(id, name));
        SelectedMicDevice = MicDevices.FirstOrDefault(d => d.Id == selectedId) ?? MicDevices.FirstOrDefault();
    }

    // --- Music controls: volume + read-only playhead -------------------------

    /// <summary>
    /// Master output level, 0..1. Two-way bound to the volume slider; forwards to the
    /// playback service immediately so the operator hears the change live.
    /// </summary>
    public double MasterVolume
    {
        get => _playback.MasterVolume;
        set
        {
            var clamped = Math.Clamp(value, 0d, 1d);
            if (Math.Abs(clamped - _playback.MasterVolume) < 0.0001) return;
            _playback.MasterVolume = (float)clamped;
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private double _durationSeconds;

    /// <summary>Low-shelf EQ (bass) gain in dB, ±12. Two-way bound to a slider.</summary>
    public double EqLowDb
    {
        get => _playback.MasterEqLowDb;
        set { _playback.MasterEqLowDb = (float)value; OnPropertyChanged(); }
    }

    /// <summary>Mid EQ gain in dB, ±12. Two-way bound to a slider.</summary>
    public double EqMidDb
    {
        get => _playback.MasterEqMidDb;
        set { _playback.MasterEqMidDb = (float)value; OnPropertyChanged(); }
    }

    /// <summary>High-shelf EQ (treble) gain in dB, ±12. Two-way bound to a slider.</summary>
    public double EqHighDb
    {
        get => _playback.MasterEqHighDb;
        set { _playback.MasterEqHighDb = (float)value; OnPropertyChanged(); }
    }

    /// <summary>Resets all three EQ bands to flat (0 dB).</summary>
    [RelayCommand]
    private void ResetEq()
    {
        EqLowDb = 0;
        EqMidDb = 0;
        EqHighDb = 0;
    }

    /// <summary>"m:ss / m:ss" transport readout for the playhead.</summary>
    public string PositionDisplay =>
        $"{TimeSpan.FromSeconds(PositionSeconds):m\\:ss} / {TimeSpan.FromSeconds(DurationSeconds):m\\:ss}";

    /// <summary>
    /// Polls the master deck ~4x/second to advance the playhead. The engine does not
    /// push position (it would flood the UI thread), so a light timer is the right fit.
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _playheadTimer =
        new() { Interval = TimeSpan.FromMilliseconds(250) };

    private void StartPlayheadTimer()
    {
        _playheadTimer.Tick -= OnPlayheadTick;
        _playheadTimer.Tick += OnPlayheadTick;
        _playheadTimer.Start();
    }

    private void OnPlayheadTick(object? sender, EventArgs e)
    {
        DurationSeconds = _playback.MasterDuration.TotalSeconds;
        if (!_isScrubbing)
            PositionSeconds = _playback.MasterPosition.TotalSeconds;
        OnPropertyChanged(nameof(PositionDisplay));
    }

    // True while the user is dragging the seek slider, so the poll timer doesn't
    // fight the drag by snapping the thumb back to the engine's position.
    private bool _isScrubbing;

    /// <summary>Called when the user starts dragging the seek slider.</summary>
    public void BeginScrub() => _isScrubbing = true;

    /// <summary>
    /// Commits a scrub: repositions the master deck to the dragged offset and resumes
    /// live position polling. Ignored when nothing is loaded on the master deck.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private void Seek(double seconds)
    {
        try
        {
            _playback.SeekMaster(TimeSpan.FromSeconds(seconds));
            PositionSeconds = seconds;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Seek failed: {ex.Message}";
        }
        finally
        {
            _isScrubbing = false;
        }
    }

    // --- Per-song editor -----------------------------------------------------

    /// <summary>
    /// Builds a modal editor view-model for a single song. The View shows it in a dialog
    /// (opened by double-clicking a track in a playlist); on save it persists the song's
    /// metadata and cue/fade settings, then the caller reloads to surface the changes.
    /// </summary>
    public TrackEditorViewModel CreateTrackEditor(TrackViewModel track) => new(_library, track);

    /// <summary>
    /// Persists a search hit (creating a streaming-backed track for remote hits) and
    /// appends it to the selected segment, defaulting to the first segment.
    /// </summary>
    private async Task AddSearchHitAsync(SearchResult hit)
    {
        var segment = SelectedSegment ?? Segments.FirstOrDefault();
        if (segment is null)
        {
            // Fresh library with no segments yet: create a default one so the
            // Add button always works instead of silently doing nothing.
            try
            {
                await _library.EnsureDefaultSectionAsync().ConfigureAwait(true);
                await LoadAsync().ConfigureAwait(true);
                segment = SelectedSegment ?? Segments.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not create a playlist: {ex.Message}";
                return;
            }

            if (segment is null)
            {
                StatusMessage = "Create a segment before adding tracks.";
                return;
            }
        }

        try
        {
            var trackId = await _library.EnsureTrackAsync(hit).ConfigureAwait(true);
            await _library.AddTrackToSectionAsync(trackId, segment.SectionId).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusMessage = $"Added '{hit.Title}' to {segment.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Add failed: {ex.Message}";
        }
    }

    // --- Playlist builder: remove / reorder within a segment -----------------

    /// <summary>Removes the given (or selected) track from the segment that contains it.</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task RemoveTrackAsync(TrackViewModel? track)
    {
        track ??= SelectedTrack;
        if (track is null) return;

        var segment = FindSegmentContaining(track);
        if (segment is null) return;

        try
        {
            await _library.RemoveTrackFromSectionAsync(track.TrackId, segment.SectionId).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusMessage = $"Removed '{track.Title}' from {segment.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove failed: {ex.Message}";
        }
    }

    /// <summary>Moves the given (or selected) track one position earlier in its segment.</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private Task MoveTrackUpAsync(TrackViewModel? track) => ReorderWithinSegmentAsync(track, -1);

    /// <summary>Moves the given (or selected) track one position later in its segment.</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private Task MoveTrackDownAsync(TrackViewModel? track) => ReorderWithinSegmentAsync(track, +1);

    private async Task ReorderWithinSegmentAsync(TrackViewModel? track, int delta)
    {
        track ??= SelectedTrack;
        if (track is null) return;

        var segment = FindSegmentContaining(track);
        if (segment is null) return;

        int currentIndex = segment.Tracks.IndexOf(track);
        int newIndex = currentIndex + delta;
        if (currentIndex < 0 || newIndex < 0 || newIndex >= segment.Tracks.Count)
            return; // already at an edge; nothing to do.

        try
        {
            // Move within the same section; MoveTrackAsync re-sequences positions.
            await _library.MoveTrackAsync(track.TrackId, segment.SectionId, segment.SectionId, newIndex)
                .ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            SelectedTrack = Segments.FirstOrDefault(s => s.SectionId == segment.SectionId)?
                .Tracks.FirstOrDefault(t => t.TrackId == track.TrackId);
            StatusMessage = $"Moved '{track.Title}' {(delta < 0 ? "up" : "down")}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Move failed: {ex.Message}";
        }
    }

    // --- Playlist management: add / rename / delete / link / loop -------------

    /// <summary>Creates a new (empty) playlist section and selects it.</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task AddSegmentAsync()
    {
        try
        {
            int id = await _library.CreateSectionAsync($"Playlist {Segments.Count + 1}").ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            SelectedSegment = Segments.FirstOrDefault(s => s.SectionId == id);
            StatusMessage = "Added a new playlist";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not add a playlist: {ex.Message}";
        }
    }

    /// <summary>Persists the (already-edited) name of the given segment.</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task RenameSegmentAsync(EventSegmentViewModel? segment)
    {
        segment ??= SelectedSegment;
        if (segment is null) return;

        try
        {
            await _library.RenameSectionAsync(segment.SectionId, segment.Name).ConfigureAwait(true);
            ResolveNextSectionNames();
            StatusMessage = $"Renamed playlist to '{segment.Name}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Rename failed: {ex.Message}";
        }
    }

    /// <summary>Deletes the given (or selected) playlist section.</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task DeleteSegmentAsync(EventSegmentViewModel? segment)
    {
        segment ??= SelectedSegment;
        if (segment is null) return;

        try
        {
            await _library.DeleteSectionAsync(segment.SectionId).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusMessage = $"Deleted playlist '{segment.Name}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
        }
    }

    /// <summary>Toggles whether the given (or selected) playlist loops.</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task ToggleLoopAsync(EventSegmentViewModel? segment)
    {
        segment ??= SelectedSegment;
        if (segment is null) return;

        try
        {
            bool newValue = !segment.IsLooping;
            await _library.SetLoopingAsync(segment.SectionId, newValue).ConfigureAwait(true);
            segment.IsLooping = newValue;
            StatusMessage = $"'{segment.Name}' loop {(newValue ? "on" : "off")}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Loop toggle failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Persists the (already-selected) chain target on the given segment. A null
    /// <see cref="EventSegmentViewModel.NextSectionId"/> clears the link.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task SetNextSegmentAsync(EventSegmentViewModel? segment)
    {
        segment ??= SelectedSegment;
        if (segment is null) return;

        try
        {
            await _library.SetNextSectionAsync(segment.SectionId, segment.NextSectionId).ConfigureAwait(true);
            ResolveNextSectionNames();
            StatusMessage = segment.NextSectionId is null
                ? $"Cleared next-playlist link for '{segment.Name}'"
                : $"'{segment.Name}' now chains into '{segment.NextSectionName}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Link failed: {ex.Message}";
        }
    }

    /// <summary>Fills each segment's NextSectionName from the current segment set.</summary>
    private void ResolveNextSectionNames()
    {
        foreach (var seg in Segments)
            seg.NextSectionName = seg.NextSectionId is { } id
                ? Segments.FirstOrDefault(s => s.SectionId == id)?.Name
                : null;
    }

    /// <summary>
    /// When true, all non-essential commands are disabled to prevent accidental
    /// clicks during a ceremony. Only the Emergency Fade and the unlock toggle remain live.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveTrackCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTrackCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveTrackUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveTrackDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(SeekCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSegmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameSegmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSegmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleLoopCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetNextSegmentCommand))]
    private bool _isLockoutEnabled;

    private bool NotLocked => !IsLockoutEnabled;

    [RelayCommand]
    private async Task LoadAsync()
    {
        // Capture selections before Clear(): clearing the collection makes the bound
        // ComboBox/list push null into RequestTargetSegment/SelectedSegment, which
        // would otherwise overwrite the persisted target with null.
        var selectedSectionId = SelectedSegment?.SectionId;
        _suppressRequestTargetSave = true;
        try
        {
            Segments.Clear();
            var sections = await _library.GetSectionsWithTracksAsync().ConfigureAwait(true);
            foreach (var section in sections)
                Segments.Add(new EventSegmentViewModel(section));
            ResolveNextSectionNames();
            RestoreRequestTargetSelection();
            RestoreSelectedSegment(selectedSectionId);
        }
        finally
        {
            _suppressRequestTargetSave = false;
        }
        StatusMessage = $"Loaded {Segments.Count} segments";

        // Age out reference-only streaming metadata in the background. A slow network
        // must never block the UI, so this is fire-and-forget; if anything is actually
        // refreshed we reload once so the updated titles surface.
        _ = RefreshStreamingCacheAsync();
    }

    private async Task RefreshStreamingCacheAsync()
    {
        try
        {
            int refreshed = await _cacheRefresher.RefreshStaleAsync().ConfigureAwait(true);
            if (refreshed > 0)
            {
                var selectedSectionId = SelectedSegment?.SectionId;
                _suppressRequestTargetSave = true;
                try
                {
                    var sections = await _library.GetSectionsWithTracksAsync().ConfigureAwait(true);
                    Segments.Clear();
                    foreach (var section in sections)
                        Segments.Add(new EventSegmentViewModel(section));
                    ResolveNextSectionNames();
                    RestoreRequestTargetSelection();
                    RestoreSelectedSegment(selectedSectionId);
                }
                finally
                {
                    _suppressRequestTargetSave = false;
                }
                StatusMessage = $"Refreshed {refreshed} streaming track(s)";
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: stale display cache remains usable until the next refresh.
            StatusMessage = $"Metadata refresh skipped: {ex.Message}";
        }
    }

    /// <summary>
    /// Lets the operator upload their own audio (MP3/MP4/M4A/WAV/FLAC/AAC). Files are
    /// tag-scanned and added to the library, then the segments are reloaded so the
    /// new tracks are immediately available for cueing.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task ImportAsync()
    {
        var extensions = string.Join(";", _import.SupportedExtensions.Select(e => "*." + e));
        var dialog = new OpenFileDialog
        {
            Title = "Import music",
            Multiselect = true,
            Filter = $"Audio files ({extensions})|{extensions}|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        StatusMessage = $"Importing {dialog.FileNames.Length} file(s)\u2026";
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            var result = await _import.ImportFilesAsync(dialog.FileNames, progress).ConfigureAwait(true);
            StatusMessage =
                $"Import complete: {result.Added} added, {result.Updated} updated, " +
                $"{result.Skipped} skipped, {result.Errors.Count} error(s)";
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Opens the Settings dialog where users can configure YouTube and Spotify API keys.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private void OpenSettings()
    {
        var settingsWindow = _settingsWindowFactory();
        settingsWindow.Owner = Application.Current.MainWindow;
        settingsWindow.ShowDialog();
    }

    // --- Guest song requests (QR code / LAN page) -----------------------------

    public ObservableCollection<SongRequestViewModel> PendingRequests { get; } = new();

    // TrackId -> priority rank (tip cents; VIP = int.MaxValue) for tracks inserted
    // at the front of the queue this session, so later tips slot in by amount.
    private readonly Dictionary<int, int> _queuedPriorityRank = new();

    /// <summary>
    /// Playlist that approved guest requests are added to by default. Null means
    /// "use the currently selected (or first) playlist". Persisted across restarts.
    /// </summary>
    [ObservableProperty] private EventSegmentViewModel? _requestTargetSegment;

    partial void OnRequestTargetSegmentChanged(EventSegmentViewModel? value)
    {
        if (_suppressRequestTargetSave) return;
        _settings.RequestTargetSectionId = value?.SectionId;
        _settings.Save();
    }

    private bool _suppressRequestTargetSave;

    /// <summary>Re-selects the previously focused playlist after Segments reloads.</summary>
    private void RestoreSelectedSegment(int? sectionId)
    {
        SelectedSegment = sectionId is { } id
            ? Segments.FirstOrDefault(s => s.SectionId == id)
            : null;
    }

    /// <summary>Marks the clicked playlist as the focused target for added songs.</summary>
    [RelayCommand]
    private void SelectSegment(EventSegmentViewModel? segment)
    {
        SelectedSegment = segment;
        if (segment is not null)
            StatusMessage = $"Focused playlist: {segment.Name} \u2014 added songs go here";
    }

    partial void OnSelectedSegmentChanged(EventSegmentViewModel? oldValue, EventSegmentViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    /// <summary>Re-selects the persisted request target after Segments reloads.</summary>
    private void RestoreRequestTargetSelection()
    {
        _suppressRequestTargetSave = true;
        try
        {
            RequestTargetSegment = _settings.RequestTargetSectionId is { } id
                ? Segments.FirstOrDefault(s => s.SectionId == id)
                : null;
        }
        finally
        {
            _suppressRequestTargetSave = false;
        }
    }

    /// <summary>Header for the requests panel, including a live count.</summary>
    public string RequestsHeader => PendingRequests.Count > 0
        ? $"Guest Requests ({PendingRequests.Count})"
        : "Guest Requests";

    private readonly System.Windows.Threading.DispatcherTimer _requestPollTimer =
        new() { Interval = TimeSpan.FromSeconds(5) };

    private void StartRequestPolling()
    {
        _requestPollTimer.Tick += async (_, _) => await RefreshRequestsAsync().ConfigureAwait(true);
        _requestPollTimer.Start();
    }

    private async Task RefreshRequestsAsync()
    {
        try
        {
            var pending = await _songRequests.GetPendingRequestsAsync().ConfigureAwait(true);

            // VIP (bride) and paid-tip requests skip the queue entirely: approve them
            // through the same path the Approve button uses, then drop them from the
            // pending set. Lockout Mode pauses auto-approval too.
            var autoApprove = pending.Where(r => r.IsPriority || r.TipCents > 0).ToList();
            if (autoApprove.Count > 0 && NotLocked)
            {
                foreach (var request in autoApprove)
                    await ApproveRequestAsync(new SongRequestViewModel(request)).ConfigureAwait(true);
                pending = pending.Where(r => !r.IsPriority && r.TipCents == 0).ToList();
            }

            // Only touch the collection when the set actually changed, so approve/reject
            // button clicks aren't disturbed by items shifting under the cursor.
            if (pending.Select(r => r.Id).SequenceEqual(PendingRequests.Select(r => r.RequestId)))
                return;

            PendingRequests.Clear();
            foreach (var request in pending)
                PendingRequests.Add(new SongRequestViewModel(request));
            OnPropertyChanged(nameof(RequestsHeader));
        }
        catch (Exception)
        {
            // Polling must never break the operator UI; retry on the next tick.
        }
    }

    /// <summary>Approves a request: adds the track to the default request playlist
    /// (falling back to the selected or first segment).</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task ApproveRequestAsync(SongRequestViewModel? request)
    {
        if (request is null) return;
        try
        {
            var segment = RequestTargetSegment ?? SelectedSegment ?? Segments.FirstOrDefault();
            if (segment is null)
            {
                await _library.EnsureDefaultSectionAsync().ConfigureAwait(true);
                await LoadAsync().ConfigureAwait(true);
                segment = Segments.FirstOrDefault();
                if (segment is null)
                {
                    StatusMessage = "Create a playlist before approving requests.";
                    return;
                }
            }

            await _library.AddTrackToSectionAsync(request.TrackId, segment.SectionId).ConfigureAwait(true);

            // VIP and tipped requests jump the queue: insert right under the currently
            // playing song. Among tipped tracks, higher tips sit higher — a new tip is
            // placed below any earlier insert that paid the same or more, above lower ones.
            if (request.IsPriority || request.TipCents > 0)
            {
                int position = 0;
                if (_currentSegment is not null && _currentTrack is not null &&
                    _currentSegment.SectionId == segment.SectionId)
                {
                    int idx = _currentSegment.Tracks.IndexOf(_currentTrack);
                    if (idx >= 0) position = idx + 1;
                }

                // Skip past previously inserted priority tracks that outrank this one
                // (VIP counts as infinite; ties keep first-paid-first-played order).
                int myRank = request.IsPriority ? int.MaxValue : request.TipCents;
                while (position < segment.Tracks.Count &&
                       _queuedPriorityRank.TryGetValue(segment.Tracks[position].TrackId, out int rank) &&
                       rank >= myRank)
                {
                    position++;
                }

                _queuedPriorityRank[request.TrackId] = myRank;
                await _library.MoveTrackAsync(request.TrackId, segment.SectionId, segment.SectionId, position)
                    .ConfigureAwait(true);
            }

            await _songRequests.ResolveRequestAsync(request.RequestId, approved: true).ConfigureAwait(true);
            PendingRequests.Remove(request);
            OnPropertyChanged(nameof(RequestsHeader));
            await LoadAsync().ConfigureAwait(true);
            StatusMessage = request.TipCents > 0
                ? $"Tipped request (${request.TipCents / 100m:0.00}) auto-approved: '{request.Title}' queued by tip in {segment.Name}"
                : request.IsPriority
                    ? $"VIP request auto-approved: '{request.Title}' queued next in {segment.Name}"
                    : $"Approved request: '{request.Title}' added to {segment.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Approve failed: {ex.Message}";
        }
    }

    /// <summary>Rejects a request without touching any playlist.</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task RejectRequestAsync(SongRequestViewModel? request)
    {
        if (request is null) return;
        try
        {
            await _songRequests.ResolveRequestAsync(request.RequestId, approved: false).ConfigureAwait(true);
            PendingRequests.Remove(request);
            OnPropertyChanged(nameof(RequestsHeader));
            StatusMessage = $"Rejected request: '{request.Title}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reject failed: {ex.Message}";
        }
    }

    /// <summary>Shows the QR code guests scan to open the request page.</summary>
    [RelayCommand]
    private void ShowRequestQr()
    {
        if (!_requestServer.IsRunning)
        {
            StatusMessage = "Request server is not running (port may be blocked or in use).";
            return;
        }

        // Prefer the internet-reachable URL when UPnP forwarding is active
        // (toggled in Settings); otherwise guests must be on the venue network.
        var usePublic = _portForwarding.IsForwarded && _portForwarding.PublicUrl is not null;
        var url = usePublic ? _portForwarding.PublicUrl! : _requestServer.RequestUrl;
        StatusMessage = _portForwarding.Status;

        var window = new QrCodeWindow(url, usePublic)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task PlayAsync(TrackViewModel? track)
    {
        track ??= SelectedTrack;
        if (track is null) return;
        var segment = FindSegmentContaining(track) ?? _currentSegment;
        if (segment is null) return;
        await PlayInternalAsync(track, segment).ConfigureAwait(true);
    }

    /// <summary>Shared play path used by the command and by auto-advance.</summary>
    private async Task PlayInternalAsync(TrackViewModel track, EventSegmentViewModel segment)
    {
        var cue = await _library.GetCueAsync(track.TrackId).ConfigureAwait(true);
        var error = await _playback.PlayOnMasterAsync(track.Model, cue).ConfigureAwait(true);
        if (error is not null)
        {
            StatusMessage = $"Can't play {track.Title}: {error}";
            return;
        }
        _currentTrack = track;
        _currentSegment = segment;
        NowPlaying = track.Title;
        StatusMessage = $"Playing: {track.Title}";

        // Record the play for the wedding keepsake export. Fire-and-forget so a
        // logging hiccup never interrupts playback; failures are intentionally ignored.
        try
        {
            await _playHistory.RecordPlayAsync(track.TrackId).ConfigureAwait(true);
        }
        catch
        {
            // Non-critical: keepsake history is best-effort.
        }
    }

    private EventSegmentViewModel? FindSegmentContaining(TrackViewModel track) =>
        Segments.FirstOrDefault(s => s.Tracks.Contains(track));

    /// <summary>
    /// Finds the next track after the current one. Within a segment this is simply the
    /// following track. At the end of a segment the search honors, in order: an explicit
    /// chain link (<see cref="EventSegmentViewModel.NextSectionId"/>), a loop back to the
    /// segment's first track, then spilling into the next non-empty segment by position.
    /// </summary>
    private (EventSegmentViewModel Segment, TrackViewModel Track, bool SameSegment)? FindNext()
    {
        if (_currentSegment is null || _currentTrack is null) return null;
        int segIdx = Segments.IndexOf(_currentSegment);
        if (segIdx < 0) return null;

        int trackIdx = _currentSegment.Tracks.IndexOf(_currentTrack);
        if (trackIdx >= 0 && trackIdx + 1 < _currentSegment.Tracks.Count)
            return (_currentSegment, _currentSegment.Tracks[trackIdx + 1], true);

        // End of segment. 1) Explicit chain link wins if the target has tracks.
        if (_currentSegment.NextSectionId is { } nextId)
        {
            var linked = Segments.FirstOrDefault(s => s.SectionId == nextId);
            if (linked is not null && linked.Tracks.Count > 0)
                return (linked, linked.Tracks[0], false);
        }

        // 2) Loop back to this segment's first track.
        if (_currentSegment.IsLooping && _currentSegment.Tracks.Count > 0)
            return (_currentSegment, _currentSegment.Tracks[0], true);

        // 3) Spill into the first track of the next non-empty segment by order.
        for (int s = segIdx + 1; s < Segments.Count; s++)
            if (Segments[s].Tracks.Count > 0)
                return (Segments[s], Segments[s].Tracks[0], false);

        return null;
    }

    /// <summary>
    /// Auto-advance logic. An Emergency Fade always advances ("bride reached the
    /// altar early"). Songs within the same playlist always play through; the
    /// per-segment transition mode only governs whether playback spills into the
    /// *next* segment (e.g. a Processional cue that must wait for the operator).
    /// </summary>
    private async void OnMasterTrackCompleted(TrackEndReason reason)
    {
        NowPlaying = null;
        bool forced = reason == TrackEndReason.EmergencyFade;

        var next = FindNext();
        if (next is null)
        {
            _currentTrack = null;
            StatusMessage = forced ? "Faded out — no next track" : "Segment complete";
            return;
        }

        // Continuing inside the same playlist always advances; a manual Emergency
        // Fade always advances. Crossing into a different segment honours that
        // segment's transition rules (continuous/auto mixes, loops, explicit chains).
        bool auto = forced ||
            next.Value.SameSegment ||
            _currentSegment!.TransitionMode is TransitionMode.ContinuousMix or TransitionMode.AutoAdvance ||
            _currentSegment.IsLooping ||
            _currentSegment.NextSectionId is not null;

        if (!auto)
        {
            _currentTrack = null;
            StatusMessage = "Track complete — waiting for cue";
            return;
        }

        await PlayInternalAsync(next.Value.Track, next.Value.Segment).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task PreviewAsync(TrackViewModel? track)
    {
        track ??= SelectedTrack;
        if (track is null) return;
        var cue = await _library.GetCueAsync(track.TrackId).ConfigureAwait(true);
        _playback.PreviewOnCue(track.Model, cue);
        StatusMessage = $"Cueing on headphones: {track.Title}";
    }

    [RelayCommand(CanExecute = nameof(NotLocked))]
    private void Pause()
    {
        _playback.Pause();
        StatusMessage = "Paused";
    }

    [RelayCommand(CanExecute = nameof(NotLocked))]
    private void Resume()
    {
        _playback.Resume();
        StatusMessage = "Resumed";
    }

    /// <summary>Always enabled — the safety override must work even under lockout.</summary>
    [RelayCommand]
    private async Task EmergencyFadeAsync()
    {
        StatusMessage = "EMERGENCY FADE…";
        // The MasterTrackCompleted handler performs the seamless advance to the next track.
        await _playback.EmergencyFadeAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleLockout()
    {
        IsLockoutEnabled = !IsLockoutEnabled;
        StatusMessage = IsLockoutEnabled ? "LOCKOUT ENGAGED" : "Lockout released";
    }

    /// <summary>Handles a drag-drop move of a track between buckets.</summary>
    [RelayCommand(CanExecute = nameof(NotLocked))]
    private async Task MoveTrackAsync(TrackMoveRequest request)
    {
        await _library.MoveTrackAsync(
            request.Track.TrackId, request.FromSection.SectionId,
            request.ToSection.SectionId, request.NewIndex).ConfigureAwait(true);

        request.FromSection.Tracks.Remove(request.Track);
        int index = Math.Clamp(request.NewIndex, 0, request.ToSection.Tracks.Count);
        request.ToSection.Tracks.Insert(index, request.Track);
        StatusMessage = $"Moved '{request.Track.Title}' to {request.ToSection.Name}";
    }

    private void OnPlaybackStateChanged() =>
        StatusMessage = $"Deck: {_playback.MasterState}";
}

/// <summary>Payload describing a drag-drop track move between segment buckets.</summary>
public sealed record TrackMoveRequest(
    TrackViewModel Track,
    EventSegmentViewModel FromSection,
    EventSegmentViewModel ToSection,
    int NewIndex);
