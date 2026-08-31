using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WeddingMusicPlannerPro.Wpf.ViewModels;

namespace WeddingMusicPlannerPro.Wpf.Behaviors;

/// <summary>
/// Attached behavior enabling drag-and-drop of <see cref="TrackViewModel"/> rows
/// between <see cref="EventSegmentViewModel"/> buckets (ItemsControls bound to
/// each segment's Tracks). Raises <see cref="MainViewModel.MoveTrackCommand"/> on drop.
/// Honours Lockout Mode via the command's CanExecute.
/// </summary>
public static class TrackDragDrop
{
    private static Point _startPoint;
    private static TrackViewModel? _dragged;
    private static EventSegmentViewModel? _sourceSegment;

    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached(
            "Enable", typeof(bool), typeof(TrackDragDrop),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject o, bool value) => o.SetValue(EnableProperty, value);
    public static bool GetEnable(DependencyObject o) => (bool)o.GetValue(EnableProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list) return;
        if ((bool)e.NewValue)
        {
            list.PreviewMouseLeftButtonDown += OnMouseDown;
            list.PreviewMouseMove += OnMouseMove;
            list.AllowDrop = true;
            list.Drop += OnDrop;
        }
        else
        {
            list.PreviewMouseLeftButtonDown -= OnMouseDown;
            list.PreviewMouseMove -= OnMouseMove;
            list.Drop -= OnDrop;
        }
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(null);
        _dragged = (e.OriginalSource as FrameworkElement)?.DataContext as TrackViewModel;
        _sourceSegment = (sender as FrameworkElement)?.DataContext as EventSegmentViewModel;
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragged is null) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _startPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop((DependencyObject)sender, _dragged, DragDropEffects.Move);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not ItemsControl target) return;
        if (target.DataContext is not EventSegmentViewModel toSegment) return;
        if (_dragged is null || _sourceSegment is null) return;
        if (Window.GetWindow(target)?.DataContext is not MainViewModel main) return;

        int index = ResolveDropIndex(target, e.GetPosition(target));
        var request = new TrackMoveRequest(_dragged, _sourceSegment, toSegment, index);

        if (main.MoveTrackCommand.CanExecute(request))
            main.MoveTrackCommand.Execute(request);

        _dragged = null;
        _sourceSegment = null;
    }

    private static int ResolveDropIndex(ItemsControl list, Point drop)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe)
            {
                var topLeft = fe.TranslatePoint(new Point(0, 0), list);
                if (drop.Y < topLeft.Y + fe.ActualHeight / 2)
                    return i;
            }
        }
        return list.Items.Count;
    }
}
