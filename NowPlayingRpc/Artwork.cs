using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NowPlayingRpc;

/// <summary>
/// Find a public cover URL through the iTunes Search API, then MusicBrainz and the Cover Art Archive.
/// Discord shows only public URLs, and a Plex thumb URL would expose the Plex token.
/// </summary>
public static partial class Artwork
{
    private static readonly HttpClient Http = CreateClient(autoRedirect: true);
    // The Cover Art Archive answers with a redirect to the image. Read the target, and use a 404 to see that no cover exists.
    private static readonly HttpClient NoRedirect = CreateClient(autoRedirect: false);
    private static readonly ConcurrentDictionary<string, string?> Cache = new();

    private static HttpClient CreateClient(bool autoRedirect)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = autoRedirect })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        // MusicBrainz blocks requests without a descriptive User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NowPlayingRpc/0.2 (https://github.com/HumboShot/nowplaying-rpc)");
        return client;
    }

    public static async Task<string?> LookupAsync(Track track)
    {
        var artist = track.AlbumArtist.Length > 0 ? track.AlbumArtist : track.Artist;
        var key = $"{artist}|{track.Album}|{(track.Album.Length > 0 ? "" : track.Title)}".ToLowerInvariant();
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        string? url;
        try
        {
            url = track.Album.Length > 0
                ? await ITunesAlbumAsync(artist, track.Album) ?? await MusicBrainzAsync(artist, track.Album)
                : await ITunesSongAsync(track.Artist, track.Title);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Do not cache a network error, so the next check tries again.
            return null;
        }
        Cache[key] = url;
        return url;
    }

    private static async Task<string?> ITunesAlbumAsync(string artist, string album)
    {
        var terms = IsVarious(artist) || artist.Length == 0 ? [album] : new[] { $"{artist} {album}", album };
        foreach (var term in terms)
        {
            foreach (var result in await ITunesSearchAsync("album", term))
            {
                if (Matches(Str(result, "collectionName"), album) && Str(result, "artworkUrl100") is { Length: > 0 } art)
                    return art.Replace("100x100bb", "512x512bb");
            }
        }
        return null;
    }

    private static async Task<string?> ITunesSongAsync(string artist, string title)
    {
        foreach (var result in await ITunesSearchAsync("song", $"{artist} {title}"))
        {
            if (Matches(Str(result, "trackName"), title) && Str(result, "artworkUrl100") is { Length: > 0 } art)
                return art.Replace("100x100bb", "512x512bb");
        }
        return null;
    }

    private static async Task<List<JsonElement>> ITunesSearchAsync(string entity, string term)
    {
        var uri = $"https://itunes.apple.com/search?entity={entity}&limit=10&term={Uri.EscapeDataString(term)}";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(uri));
        return doc.RootElement.GetProperty("results").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static async Task<string?> MusicBrainzAsync(string artist, string album)
    {
        var query = $"release:{Quote(album)}";
        if (artist.Length > 0)
            query += $" AND artist:{Quote(artist)}";
        var uri = $"https://musicbrainz.org/ws/2/release/?fmt=json&limit=5&query={Uri.EscapeDataString(query)}";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(uri));
        foreach (var release in doc.RootElement.GetProperty("releases").EnumerateArray())
        {
            if (!Matches(Str(release, "title"), album) || Str(release, "id") is not { Length: > 0 } id)
                continue;
            using var response = await NoRedirect.GetAsync(
                $"https://coverartarchive.org/release/{id}/front-500", HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.Redirect
                && response.Headers.Location is { } location)
                return location.ToString();
        }
        return null;
    }

    private static bool IsVarious(string artist) => Normalize(artist) is "variousartists" or "various";

    /// <summary>Compare two names, and ignore case, punctuation and extras such as "(Deluxe Edition)" or "- Single".</summary>
    private static bool Matches(string a, string b)
    {
        if (Normalize(a) == Normalize(b))
            return true;
        var strippedA = Normalize(StripExtras(a));
        return strippedA.Length > 0 && strippedA == Normalize(StripExtras(b));
    }

    private static string StripExtras(string name) => Extras().Replace(name, "");

    private static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    [GeneratedRegex(@"\s*(\([^)]*\)|\[[^\]]*\])|\s+-\s+(Single|EP)$", RegexOptions.IgnoreCase)]
    private static partial Regex Extras();
}
