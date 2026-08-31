using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using WeddingMusicPlannerPro.Wpf.ViewModels;

namespace WeddingMusicPlannerPro.Wpf.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Load existing Spotify secret into PasswordBox
        if (viewModel.SpotifyClientSecret is { Length: > 0 } secret)
        {
            SpotifySecretBox.Password = secret;
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.SpotifyClientSecret = SpotifySecretBox.Password;
        }
    }

    private void OnHyperlinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // Persist any edits so typed values (e.g. the VIP password) aren't
        // silently lost when the window is closed without pressing Save.
        if (DataContext is SettingsViewModel vm)
        {
            vm.SaveCommand.Execute(null);
        }
        Close();
    }
}
