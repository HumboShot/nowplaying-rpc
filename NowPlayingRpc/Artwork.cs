using System.Collections.Concurrent;
using System.Text.Json;

namespace NowPlayingRpc;

/// <summary>
/// Find a public cover URL through the iTunes Search API.
/// Discord shows only public URLs, and a Plex thumb URL would expose the Plex token.
/// </summary>
public static class Artwork
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly ConcurrentDictionary<string, string?> Cache = new();

    public static async Task<string?> LookupAsync(Track track)
    {
        var (entity, term) = track.Album.Length > 0
            ? ("album", $"{track.Artist} {track.Album}")
            : ("song", $"{track.Artist} {track.Title}");
        var key = $"{entity}|{term.ToLowerInvariant()}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        string? url = null;
        try
        {
            var uri = $"https://itunes.apple.com/search?entity={entity}&limit=1&term={Uri.EscapeDataString(term)}";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(uri));
            var results = doc.RootElement.GetProperty("results");
            if (results.GetArrayLength() > 0 && results[0].TryGetProperty("artworkUrl100", out var art))
                url = art.GetString()?.Replace("100x100bb", "512x512bb");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
        Cache[key] = url;
        return url;
    }
}
