using System.IO;
using System.Text.Json;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// Persists user settings (API keys) to a JSON file in LocalApplicationData.
/// Settings are loaded on startup and saved when the user updates them via the UI.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public SettingsService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WeddingMusicPlannerPro");
        Directory.CreateDirectory(appData);
        _settingsPath = Path.Combine(appData, "settings.json");
    }

    public string? YouTubeApiKey
    {
        get => _settings.YouTubeApiKey;
        set => _settings.YouTubeApiKey = value;
    }

    public string? SpotifyClientId
    {
        get => _settings.SpotifyClientId;
        set => _settings.SpotifyClientId = value;
    }

    public string? SpotifyClientSecret
    {
        get => _settings.SpotifyClientSecret;
        set => _settings.SpotifyClientSecret = value;
    }

    public bool EnablePublicRequests
    {
        get => _settings.EnablePublicRequests;
        set => _settings.EnablePublicRequests = value;
    }

    public string? BridePassword
    {
        get => _settings.BridePassword;
        set => _settings.BridePassword = value;
    }

    public int? RequestTargetSectionId
    {
        get => _settings.RequestTargetSectionId;
        set => _settings.RequestTargetSectionId = value;
    }

    public string? StripeSecretKey
    {
        get => _settings.StripeSecretKey;
        set => _settings.StripeSecretKey = value;
    }

    public bool EnableTipping
    {
        get => _settings.EnableTipping;
        set => _settings.EnableTipping = value;
    }

    public string? PayPalClientId
    {
        get => _settings.PayPalClientId;
        set => _settings.PayPalClientId = value;
    }

    public string? PayPalClientSecret
    {
        get => _settings.PayPalClientSecret;
        set => _settings.PayPalClientSecret = value;
    }

    public bool PayPalSandbox
    {
        get => _settings.PayPalSandbox;
        set => _settings.PayPalSandbox = value;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_settingsPath, json);
    }

    public void Load()
    {
        if (!File.Exists(_settingsPath))
            return;

        try
        {
            var json = File.ReadAllText(_settingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch
        {
            // Corrupted or invalid JSON; start fresh
            _settings = new();
        }
    }

    private class AppSettings
    {
        public string? YouTubeApiKey { get; set; }
        public string? SpotifyClientId { get; set; }
        public string? SpotifyClientSecret { get; set; }
        public bool EnablePublicRequests { get; set; }
        public string? BridePassword { get; set; }
        public int? RequestTargetSectionId { get; set; }
        public string? StripeSecretKey { get; set; }
        public bool EnableTipping { get; set; } = true;
        public string? PayPalClientId { get; set; }
        public string? PayPalClientSecret { get; set; }
        public bool PayPalSandbox { get; set; }
    }
}
