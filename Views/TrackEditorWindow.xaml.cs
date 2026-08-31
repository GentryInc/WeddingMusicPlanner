using System.Windows;
using WeddingMusicPlannerPro.Wpf.ViewModels;

namespace WeddingMusicPlannerPro.Wpf.Views;

/// <summary>
/// Modal per-song properties editor opened by double-clicking a track in a playlist.
/// The <see cref="TrackEditorViewModel"/> owns validation and persistence; this window
/// just hosts it, loads the cue on open, and closes on Save/Cancel.
/// </summary>
public partial class TrackEditorWindow : Window
{
    private readonly TrackEditorViewModel _vm;

    public TrackEditorWindow(TrackEditorViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _vm.RequestClose += OnRequestClose;
        Loaded += async (_, _) => await _vm.InitializeAsync();
    }

    /// <summary>True when the user saved changes, so the caller can reload the playlist.</summary>
    public bool Saved => _vm.Saved;

    private void OnRequestClose(object? sender, EventArgs e)
    {
        _vm.RequestClose -= OnRequestClose;
        DialogResult = _vm.Saved;
        Close();
    }
}
