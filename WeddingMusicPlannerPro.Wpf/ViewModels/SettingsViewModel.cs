using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WeddingMusicPlannerPro.Wpf.Services;

namespace WeddingMusicPlannerPro.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Settings dialog where users configure API keys for YouTube and Spotify.
/// Changes are saved immediately to disk when the Save button is clicked.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IPlaylistExportService _exportService;

    public SettingsViewModel(ISettingsService settings, IPlaylistExportService exportService)
    {
        _settings = settings;
        _exportService = exportService;

        // Load current values from settings service
        _youTubeApiKey = _settings.YouTubeApiKey ?? string.Empty;
        _spotifyClientId = _settings.SpotifyClientId ?? string.Empty;
        _spotifyClientSecret = _settings.SpotifyClientSecret ?? string.Empty;
        _enablePublicRequests = _settings.EnablePublicRequests;
        _bridePassword = _settings.BridePassword ?? string.Empty;
        _stripeSecretKey = _settings.StripeSecretKey ?? string.Empty;
        _enableTipping = _settings.EnableTipping;
        _payPalClientId = _settings.PayPalClientId ?? string.Empty;
        _payPalClientSecret = _settings.PayPalClientSecret ?? string.Empty;
        _payPalSandbox = _settings.PayPalSandbox;
        _tunnelSubdomain = _settings.TunnelSubdomain ?? string.Empty;
    }

    [ObservableProperty]
    private string _youTubeApiKey = string.Empty;

    [ObservableProperty]
    private string _spotifyClientId = string.Empty;

    [ObservableProperty]
    private string _spotifyClientSecret = string.Empty;

    [ObservableProperty]
    private bool _enablePublicRequests;

    [ObservableProperty]
    private string _bridePassword = string.Empty;

    [ObservableProperty]
    private string _stripeSecretKey = string.Empty;

    [ObservableProperty]
    private bool _enableTipping = true;

    [ObservableProperty]
    private string _payPalClientId = string.Empty;

    [ObservableProperty]
    private string _payPalClientSecret = string.Empty;

    [ObservableProperty]
    private bool _payPalSandbox;

    [ObservableProperty]
    private string _tunnelSubdomain = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // --- Wedding keepsake export -----------------------------------------

    /// <summary>Export target provider: 0 = Spotify, 1 = YouTube.</summary>
    [ObservableProperty]
    private int _exportProviderIndex;

    /// <summary>Export song scope: 0 = played only, 1 = all planned songs.</summary>
    [ObservableProperty]
    private int _exportScopeIndex;

    /// <summary>Export delivery: 0 = clickable links page, 1 = data file.</summary>
    [ObservableProperty]
    private int _exportDeliveryIndex;

    [ObservableProperty]
    private string _exportStatusMessage = string.Empty;

    [RelayCommand]
    private void Save()
    {
        _settings.YouTubeApiKey = string.IsNullOrWhiteSpace(YouTubeApiKey) ? null : YouTubeApiKey.Trim();
        _settings.SpotifyClientId = string.IsNullOrWhiteSpace(SpotifyClientId) ? null : SpotifyClientId.Trim();
        _settings.SpotifyClientSecret = string.IsNullOrWhiteSpace(SpotifyClientSecret) ? null : SpotifyClientSecret.Trim();
        _settings.EnablePublicRequests = EnablePublicRequests;
        _settings.BridePassword = string.IsNullOrWhiteSpace(BridePassword) ? null : BridePassword.Trim();
        _settings.StripeSecretKey = string.IsNullOrWhiteSpace(StripeSecretKey) ? null : StripeSecretKey.Trim();
        _settings.EnableTipping = EnableTipping;
        _settings.PayPalClientId = string.IsNullOrWhiteSpace(PayPalClientId) ? null : PayPalClientId.Trim();
        _settings.PayPalClientSecret = string.IsNullOrWhiteSpace(PayPalClientSecret) ? null : PayPalClientSecret.Trim();
        _settings.PayPalSandbox = PayPalSandbox;
        _settings.TunnelSubdomain = string.IsNullOrWhiteSpace(TunnelSubdomain) ? null : TunnelSubdomain.Trim().ToLowerInvariant();

        _settings.Save();

        StatusMessage = "Settings saved successfully! Restart the application for changes to take effect.";
    }

    [RelayCommand]
    private void Clear()
    {
        YouTubeApiKey = string.Empty;
        SpotifyClientId = string.Empty;
        SpotifyClientSecret = string.Empty;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// Builds the wedding keepsake playlist for the selected provider/scope and writes
    /// it to a file the couple chooses. Never throws to the UI; failures are reported
    /// through <see cref="ExportStatusMessage"/>.
    /// </summary>
    [RelayCommand]
    private async Task ExportKeepsakeAsync()
    {
        var provider = ExportProviderIndex == 1 ? ExportProvider.YouTube : ExportProvider.Spotify;
        var scope = ExportScopeIndex == 1 ? ExportScope.All : ExportScope.Played;
        var delivery = ExportDeliveryIndex == 1 ? ExportDelivery.File : ExportDelivery.Links;

        var extension = _exportService.GetFileExtension(delivery);
        var filter = delivery == ExportDelivery.Links
            ? "Web page (*.html)|*.html"
            : "CSV file (*.csv)|*.csv";

        var dialog = new SaveFileDialog
        {
            Title = "Save Wedding Keepsake Playlist",
            FileName = $"Wedding Keepsake Playlist ({provider})",
            DefaultExt = extension,
            Filter = filter,
            AddExtension = true
        };

        if (dialog.ShowDialog() != true)
        {
            ExportStatusMessage = "Export cancelled.";
            return;
        }

        try
        {
            ExportStatusMessage = "Building your keepsake playlist…";
            int count = await _exportService.ExportAsync(provider, scope, delivery, dialog.FileName)
                .ConfigureAwait(true);

            ExportStatusMessage = count == 0
                ? "No songs found to export yet. Play some songs (or add them to a playlist) first."
                : $"Exported {count} song{(count == 1 ? string.Empty : "s")} to \"{dialog.FileName}\".";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Export failed: {ex.Message}";
        }
    }
}
