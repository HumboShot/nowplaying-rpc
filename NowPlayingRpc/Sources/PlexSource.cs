using System.Text.Json;

namespace NowPlayingRpc.Sources;

/// <summary>Read Plexamp sessions from the Plex Media Server API. This works on every OS.</summary>
public sealed class PlexSource : ISource
{
    private readonly PlexConfig _config;
    private readonly HttpClient _http;

    public PlexSource(PlexConfig config)
    {
        _config = config;
        var handler = new HttpClientHandler();
        if (!config.VerifyTls)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        _http.DefaultRequestHeaders.Add("X-Plex-Token", config.Token);
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public string Name => "plex";

    public async Task<IReadOnlyList<Track>> GetTracksAsync()
    {
        string json;
        try
        {
            json = await _http.GetStringAsync($"{_config.ServerUrl.TrimEnd('/')}/status/sessions");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        var result = new List<Track>();
        if (!doc.RootElement.TryGetProperty("MediaContainer", out var container)
            || !container.TryGetProperty("Metadata", out var items))
            return result;

        foreach (var item in items.EnumerateArray())
        {
            if (Str(item, "type") != "track" || !item.TryGetProperty("Player", out var player))
                continue;
            if (!Str(player, "product").Contains("plexamp", StringComparison.OrdinalIgnoreCase))
                continue;
            if (_config.Username.Length > 0
                && !(item.TryGetProperty("User", out var user)
                     && Str(user, "title").Equals(_config.Username, StringComparison.OrdinalIgnoreCase)))
                continue;
            var state = Str(player, "state");
            if (state is not ("playing" or "paused"))
                continue;

            double? duration = item.TryGetProperty("duration", out var d) ? d.GetDouble() / 1000 : null;
            double? offset = item.TryGetProperty("viewOffset", out var o) ? o.GetDouble() / 1000 : null;
            var artist = Str(item, "originalTitle");
            result.Add(new Track(
                "plexamp",
                Str(item, "title"),
                artist.Length > 0 ? artist : Str(item, "grandparentTitle"),
                Str(item, "parentTitle"),
                duration,
                duration != null ? offset : null,
                state == "playing"));
        }
        return result;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
}
