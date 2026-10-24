using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NowPlayingRpc.Sources;

/// <summary>Read foobar2000 through the beefweb component's HTTP API.</summary>
public sealed class BeefwebSource : ISource
{
    private readonly string _url;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public BeefwebSource(FoobarConfig config)
    {
        _url = config.Url.TrimEnd('/');
        if (config.Username.Length > 0)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}")));
    }

    public string Name => "beefweb";

    public async Task<IReadOnlyList<Track>> GetTracksAsync()
    {
        string json;
        try
        {
            json = await _http.GetStringAsync($"{_url}/api/player?columns=%25title%25,%25artist%25,%25album%25");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("player", out var player)
            || !player.TryGetProperty("activeItem", out var item)
            || !item.TryGetProperty("columns", out var columnsElement))
            return [];
        var state = player.TryGetProperty("playbackState", out var s) ? s.GetString() : "";
        var columns = columnsElement.EnumerateArray().Select(c => c.GetString() ?? "").ToArray();
        if (state is not ("playing" or "paused") || columns.Length < 3 || columns[0].Length == 0)
            return [];

        double? duration = item.TryGetProperty("duration", out var d) && d.GetDouble() > 0 ? d.GetDouble() : null;
        double? position = duration != null && item.TryGetProperty("position", out var p) ? p.GetDouble() : null;
        return
        [
            new Track(
                "foobar2000",
                columns[0],
                columns[1] == "?" ? "" : columns[1],
                columns[2] == "?" ? "" : columns[2],
                duration,
                position,
                state == "playing"),
        ];
    }
}
