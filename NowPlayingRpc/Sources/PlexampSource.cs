using System.Xml.Linq;

namespace NowPlayingRpc.Sources;

/// <summary>Read Plexamp through its local player API. Plexamp desktop serves it on every OS, with no token.</summary>
public sealed class PlexampSource : ISource
{
    private readonly string _url;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private int _commandId;

    public PlexampSource(PlexampConfig config)
    {
        _url = config.Url.TrimEnd('/');
        _http.DefaultRequestHeaders.Add("X-Plex-Client-Identifier", "nowplaying-rpc");
        _http.DefaultRequestHeaders.Add("X-Plex-Device-Name", "NowPlaying RPC");
    }

    public string Name => "plexamp";

    public async Task<IReadOnlyList<Track>> GetTracksAsync()
    {
        string xml;
        try
        {
            var id = Interlocked.Increment(ref _commandId);
            xml = await _http.GetStringAsync(
                $"{_url}/player/timeline/poll?wait=0&includeMetadata=1&type=music&commandID={id}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return [];
        }

        var timeline = XDocument.Parse(xml).Root?
            .Elements("Timeline")
            .FirstOrDefault(t => (string?)t.Attribute("type") == "music");
        var state = (string?)timeline?.Attribute("state");
        var track = timeline?.Element("Track");
        if (track == null || state is not ("playing" or "paused"))
            return [];

        var title = (string?)track.Attribute("title") ?? "";
        if (title.Length == 0)
            return [];
        var artist = (string?)track.Attribute("originalTitle");
        double? duration = (double?)timeline!.Attribute("duration") / 1000;
        double? position = (double?)timeline.Attribute("time") / 1000;
        return
        [
            new Track(
                "plexamp",
                title,
                string.IsNullOrEmpty(artist) ? (string?)track.Attribute("grandparentTitle") ?? "" : artist,
                (string?)track.Attribute("parentTitle") ?? "",
                duration > 0 ? duration : null,
                duration > 0 ? position : null,
                state == "playing",
                AlbumArtist: (string?)track.Attribute("grandparentTitle") ?? ""),
        ];
    }
}
