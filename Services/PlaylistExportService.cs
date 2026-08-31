using System.IO;
using System.Text;
using System.Web;
using WeddingMusicData.Models;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>Streaming service the keepsake playlist is built for.</summary>
public enum ExportProvider
{
    Spotify,
    YouTube
}

/// <summary>Which songs to include in the keepsake export.</summary>
public enum ExportScope
{
    /// <summary>Only songs actually played during the event.</summary>
    Played,
    /// <summary>Every song across all planned playlist sections.</summary>
    All
}

/// <summary>How the keepsake export is delivered to the couple.</summary>
public enum ExportDelivery
{
    /// <summary>An HTML page with clickable "add to provider" links (no login).</summary>
    Links,
    /// <summary>A data file (.csv) with song details and provider links.</summary>
    File
}

/// <summary>
/// Builds a single "wedding keepsake" playlist for Spotify or YouTube from either the
/// played-song history or every planned song, and writes it as a shareable links page
/// or a data file. Uses no OAuth: each song becomes a provider add/search link the
/// couple can click to build the playlist in their own account.
/// </summary>
public interface IPlaylistExportService
{
    /// <summary>
    /// Produces the keepsake export and writes it to <paramref name="outputPath"/>.
    /// Returns the number of songs written (0 means nothing to export).
    /// </summary>
    Task<int> ExportAsync(
        ExportProvider provider,
        ExportScope scope,
        ExportDelivery delivery,
        string outputPath,
        CancellationToken ct = default);

    /// <summary>Suggested file extension for the given delivery mode (e.g. ".html", ".csv").</summary>
    string GetFileExtension(ExportDelivery delivery);
}

public sealed class PlaylistExportService : IPlaylistExportService
{
    private readonly IPlayHistoryService _playHistory;
    private readonly ILibraryService _library;

    public PlaylistExportService(IPlayHistoryService playHistory, ILibraryService library)
    {
        _playHistory = playHistory;
        _library = library;
    }

    public string GetFileExtension(ExportDelivery delivery) =>
        delivery == ExportDelivery.Links ? ".html" : ".csv";

    public async Task<int> ExportAsync(
        ExportProvider provider,
        ExportScope scope,
        ExportDelivery delivery,
        string outputPath,
        CancellationToken ct = default)
    {
        var tracks = await GatherTracksAsync(scope, ct).ConfigureAwait(false);
        if (tracks.Count == 0)
            return 0;

        var songs = tracks
            .Select(t => ToSong(t, provider))
            .ToList();

        var content = delivery == ExportDelivery.Links
            ? BuildLinksHtml(provider, scope, songs)
            : BuildCsv(provider, songs);

        await File.WriteAllTextAsync(outputPath, content, new UTF8Encoding(true), ct).ConfigureAwait(false);
        return songs.Count;
    }

    private async Task<IReadOnlyList<Track>> GatherTracksAsync(ExportScope scope, CancellationToken ct)
    {
        if (scope == ExportScope.Played)
            return await _playHistory.GetPlayedTracksAsync(ct).ConfigureAwait(false);

        // All planned songs across every section, de-duplicated by track, in section order.
        var sections = await _library.GetSectionsWithTracksAsync(ct).ConfigureAwait(false);
        var seen = new HashSet<int>();
        var all = new List<Track>();
        foreach (var section in sections)
            foreach (var item in section.Items)
                if (item.Track is { } track && seen.Add(track.Id))
                    all.Add(track);
        return all;
    }

    private static ExportSong ToSong(Track track, ExportProvider provider)
    {
        var title = string.IsNullOrWhiteSpace(track.Title) ? "(unknown title)" : track.Title;
        var artist = track.Artist ?? string.Empty;
        return new ExportSong(title, artist, BuildLink(track, provider));
    }

    /// <summary>
    /// Builds a provider link for the track. Prefers the track's own streaming URI
    /// (a real Spotify/YouTube link) and otherwise falls back to a provider search URL
    /// built from artist + title, which the couple can use to find and add the song.
    /// </summary>
    private static string BuildLink(Track track, ExportProvider provider)
    {
        var uri = track.ExternalUri;

        if (provider == ExportProvider.Spotify)
        {
            if (!string.IsNullOrWhiteSpace(uri) && LooksLikeSpotify(uri))
                return NormalizeSpotify(uri!);
            var q = HttpUtility.UrlEncode(BuildQuery(track));
            return $"https://open.spotify.com/search/{q}";
        }

        // YouTube
        if (!string.IsNullOrWhiteSpace(uri) && LooksLikeYouTube(uri))
            return uri!;
        var yq = HttpUtility.UrlEncode(BuildQuery(track));
        return $"https://www.youtube.com/results?search_query={yq}";
    }

    private static string BuildQuery(Track track) =>
        string.IsNullOrWhiteSpace(track.Artist) ? track.Title : $"{track.Artist} {track.Title}";

    private static bool LooksLikeSpotify(string uri) =>
        uri.Contains("spotify", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeYouTube(string uri) =>
        uri.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns a spotify:track:ID URI into an openable https link; passes URLs through.</summary>
    private static string NormalizeSpotify(string uri)
    {
        if (uri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.Replace("spotify:", string.Empty, StringComparison.OrdinalIgnoreCase)
                          .Replace(':', '/');
            return $"https://open.spotify.com/{path}";
        }
        return uri;
    }

    private static string BuildCsv(ExportProvider provider, IReadOnlyList<ExportSong> songs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Title,Artist,{provider} Link");
        foreach (var s in songs)
            sb.AppendLine($"{CsvField(s.Title)},{CsvField(s.Artist)},{CsvField(s.Link)}");
        return sb.ToString();
    }

    private static string CsvField(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string BuildLinksHtml(ExportProvider provider, ExportScope scope, IReadOnlyList<ExportSong> songs)
    {
        var providerName = provider == ExportProvider.Spotify ? "Spotify" : "YouTube";
        var accent = provider == ExportProvider.Spotify ? "#1DB954" : "#FF0000";
        var scopeLabel = scope == ExportScope.Played ? "songs played at your wedding" : "all planned songs";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>Wedding Keepsake Playlist - {providerName}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:'Segoe UI',Arial,sans-serif;background:#faf7f2;color:#2b2b2b;margin:0;padding:2rem;}");
        sb.AppendLine(".card{max-width:760px;margin:0 auto;background:#fff;border-radius:16px;box-shadow:0 8px 30px rgba(0,0,0,.08);padding:2rem;}");
        sb.AppendLine("h1{font-size:1.6rem;margin:0 0 .25rem;}");
        sb.AppendLine("p.sub{color:#777;margin:0 0 1.5rem;}");
        sb.AppendLine("ol{list-style:none;counter-reset:s;padding:0;margin:0;}");
        sb.AppendLine("li{counter-increment:s;display:flex;align-items:center;gap:1rem;padding:.65rem .5rem;border-bottom:1px solid #eee;}");
        sb.AppendLine("li::before{content:counter(s);width:2rem;text-align:right;color:#bbb;font-variant-numeric:tabular-nums;}");
        sb.AppendLine(".meta{flex:1;min-width:0;}");
        sb.AppendLine(".title{font-weight:600;}");
        sb.AppendLine(".artist{color:#888;font-size:.9rem;}");
        sb.AppendLine($".btn{{background:{accent};color:#fff;text-decoration:none;padding:.5rem .9rem;border-radius:999px;font-size:.85rem;white-space:nowrap;}}");
        sb.AppendLine("</style></head><body><div class=\"card\">");
        sb.AppendLine($"<h1>&#9829; Your Wedding Keepsake Playlist</h1>");
        sb.AppendLine($"<p class=\"sub\">{songs.Count} {scopeLabel}. Click &quot;Add on {providerName}&quot; for each song to build your playlist.</p>");
        sb.AppendLine("<ol>");
        foreach (var s in songs)
        {
            var title = HttpUtility.HtmlEncode(s.Title);
            var artist = HttpUtility.HtmlEncode(s.Artist);
            var link = HttpUtility.HtmlAttributeEncode(s.Link);
            sb.AppendLine("<li><div class=\"meta\">" +
                          $"<div class=\"title\">{title}</div>" +
                          $"<div class=\"artist\">{artist}</div></div>" +
                          $"<a class=\"btn\" href=\"{link}\" target=\"_blank\" rel=\"noopener\">Add on {providerName}</a></li>");
        }
        sb.AppendLine("</ol></div></body></html>");
        return sb.ToString();
    }

    private readonly record struct ExportSong(string Title, string Artist, string Link);
}
