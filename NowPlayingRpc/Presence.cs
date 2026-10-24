using System.Text;
using DiscordRPC;

namespace NowPlayingRpc;

public sealed class Presence : IDisposable
{
    // Plex reports the play position only about every 10 seconds, so a smaller drift is not a seek.
    private const int SeekTolerance = 10;

    private DiscordConfig _config;
    private DiscordRpcClient? _client;
    private RichPresence? _current;
    private (string, string, string, string, bool)? _shownKey;
    private string? _shownArt;
    private DateTime? _shownStart;

    public bool Connected { get; private set; }

    public Presence(DiscordConfig config) => _config = config;

    public void Configure(DiscordConfig config)
    {
        _config = config;
        CloseClient();
    }

    private void EnsureClient(string clientId)
    {
        if (_client != null && _client.ApplicationID == clientId)
            return;
        CloseClient();
        // The client connects again by itself when Discord restarts.
        _client = new DiscordRpcClient(clientId) { SkipIdenticalPresence = false };
        _client.OnReady += (_, e) =>
        {
            Connected = true;
            Log.Write($"Connected to Discord as {e.User.Username}");
            if (_current != null)
                _client.SetPresence(_current);
        };
        _client.OnConnectionFailed += (_, _) => Connected = false;
        _client.OnClose += (_, _) => Connected = false;
        _client.Initialize();
    }

    private void CloseClient()
    {
        _client?.Dispose();
        _client = null;
        _current = null;
        _shownKey = null;
        Connected = false;
    }

    public void Show(Track track, string? artUrl)
    {
        DateTime? start = null, end = null;
        if (track.Playing && track.Position is { } position)
        {
            start = DateTime.UtcNow.AddSeconds(-position);
            if (track.Duration is { } duration)
                end = start.Value.AddSeconds(duration);
        }

        var seeked = start != null && _shownStart != null
            && Math.Abs((start.Value - _shownStart.Value).TotalSeconds) > SeekTolerance;
        if (track.Key == _shownKey && artUrl == _shownArt && !seeked)
            return;

        var clientId = _config.ClientIds.GetValueOrDefault(track.Player) is { Length: > 0 } id ? id : _config.ClientId;
        EnsureClient(clientId);

        var smallImage = _config.ShowPlayerIcon ? track.Player : null;
        var largeImage = artUrl ?? (_config.LargeImageFallback.Length > 0 ? _config.LargeImageFallback : null);
        _current = new RichPresence
        {
            Type = ActivityType.Listening,
            StatusDisplay = _config.StatusDisplay switch
            {
                "state" => StatusDisplayType.State,
                "details" => StatusDisplayType.Details,
                _ => StatusDisplayType.Name,
            },
            Details = Text(track.Title),
            State = Text(track.Playing ? track.Artist : $"Paused · {track.Artist}"),
            Timestamps = start != null ? new Timestamps { Start = start, End = end } : null,
            Assets = new Assets
            {
                LargeImageKey = largeImage,
                LargeImageText = Text(track.Album),
                SmallImageKey = smallImage,
                SmallImageText = smallImage != null ? track.PlayerName : null,
            },
        };

        try
        {
            _client!.SetPresence(_current);
        }
        catch (Exception e)
        {
            Log.Write($"Discord update failed: {e.Message}");
            return;
        }
        if (track.Key != _shownKey)
            Log.Write($"Now showing: {track.Artist} - {track.Title} ({track.PlayerName})");
        _shownKey = track.Key;
        _shownArt = artUrl;
        _shownStart = start;
    }

    public void Clear()
    {
        if (_client == null || _shownKey == null)
            return;
        _client.ClearPresence();
        _current = null;
        _shownKey = null;
        _shownStart = null;
        Log.Write("Cleared presence");
    }

    public void Dispose() => CloseClient();

    /// <summary>Discord accepts 2 to 128 bytes of UTF-8 for each text field.</summary>
    private static string? Text(string value)
    {
        if (value.Length == 0)
            return null;
        while (Encoding.UTF8.GetByteCount(value) > 128)
            value = value[..^1];
        if (value.Length > 0 && char.IsHighSurrogate(value[^1]))
            value = value[..^1];
        return value.PadRight(2, '​');
    }
}
