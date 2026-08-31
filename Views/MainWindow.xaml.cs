using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WeddingMusicPlannerPro.Wpf.ViewModels;

namespace WeddingMusicPlannerPro.Wpf.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>Suspend live position polling while the user drags the seek thumb.</summary>
    private void OnSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.BeginScrub();
    }

    /// <summary>Commit the dragged position to the playback engine when the drag ends.</summary>
    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Slider slider
            && vm.SeekCommand.CanExecute(slider.Value))
        {
            vm.SeekCommand.Execute(slider.Value);
        }
    }

    /// <summary>
    /// Opens the per-song properties editor when a playlist track is double-clicked.
    /// Resolves the clicked <see cref="TrackViewModel"/> from the event source so a click
    /// on empty list space (or a header) is ignored, then shows the modal editor.
    /// </summary>
    private void OnTrackDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if ((e.OriginalSource as DependencyObject)?.FindTrack() is not TrackViewModel track)
            return;

        var editor = new TrackEditorWindow(vm.CreateTrackEditor(track)) { Owner = this };
        editor.ShowDialog();
        e.Handled = true;
    }
}

/// <summary>Visual-tree helpers for the playlist track list.</summary>
internal static class TrackDoubleClickExtensions
{
    /// <summary>
    /// Walks up from a hit-tested element to the <see cref="TrackViewModel"/> bound to the
    /// containing <see cref="ListBoxItem"/>. Returns null when the click wasn't on a row.
    /// </summary>
    public static TrackViewModel? FindTrack(this DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);

        return (source as ListBoxItem)?.DataContext as TrackViewModel;
    }
}
