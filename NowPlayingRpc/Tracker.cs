using NowPlayingRpc.Sources;

namespace NowPlayingRpc;

/// <summary>Check all sources at an interval and send the best track to Discord.</summary>
public sealed class Tracker
{
    private Config _config;
    private IReadOnlyList<ISource> _sources;
    private readonly Presence _presence;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private (string, string) _lastStatus;
    private volatile Config? _pending;
    private volatile bool _paused;

    /// <summary>Raised with the track line and the Discord connection line.</summary>
    public event Action<string, string>? StatusChanged;

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public Tracker(Config config)
    {
        _config = config;
        _sources = SourceFactory.Build(config);
        _presence = new Presence(config.Discord);
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_stop.Token));

    /// <summary>The poll loop applies the new config at its next check, so no lock is necessary.</summary>
    public void Reload(Config config) => _pending = config;

    public async Task StopAsync()
    {
        _stop.Cancel();
        if (_loop != null)
            await _loop;
        _presence.Dispose();
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LastErrors = new();

    public static async Task<List<Track>> Collect(IReadOnlyList<ISource> sources)
    {
        var tasks = sources.Select(async s =>
        {
            try
            {
                var tracks = await s.GetTracksAsync();
                LastErrors.TryRemove(s.Name, out _);
                return tracks;
            }
            catch (Exception e)
            {
                // Log a repeated error only once, so the log does not grow at each check.
                if (!LastErrors.TryGetValue(s.Name, out var last) || last != e.Message)
                    Log.Write($"Source {s.Name} failed: {e.Message}");
                LastErrors[s.Name] = e.Message;
                return [];
            }
        });
        return (await Task.WhenAll(tasks)).SelectMany(t => t).ToList();
    }

    public static Track? Pick(IEnumerable<Track> tracks, IList<string> priority) =>
        tracks
            .OrderBy(t => !t.Playing)
            .ThenBy(t => priority.IndexOf(t.Player) is var i && i >= 0 ? i : priority.Count)
            .FirstOrDefault();

    private async Task RunAsync(CancellationToken token)
    {
        Log.Write($"Config: {Config.FilePath}");
        Log.Write($"Sources: {string.Join(", ", _sources.Select(s => s.Name))}");
        while (!token.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception e)
            {
                Log.Write($"Error: {e}");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _config.General.PollInterval)), token);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }

    private async Task TickAsync()
    {
        if (_pending is { } pending)
        {
            _pending = null;
            _config = pending;
            _sources = SourceFactory.Build(pending);
            _presence.Configure(pending.Discord);
            Log.Write("Config reloaded");
        }

        if (_config.Discord.ClientId.Length == 0)
        {
            SetStatus("Set discord.client_id in the config file");
            return;
        }

        var track = Pick(await Collect(_sources), _config.General.Priority);
        if (Paused || track == null || (!track.Playing && _config.General.ClearOnPause))
        {
            _presence.Clear();
            SetStatus(Paused ? "Presence paused" : track == null ? "Nothing playing" : $"{track.PlayerName}: paused");
            return;
        }

        var art = track.ArtUrl;
        if (art == null && _config.General.LookupArtwork)
            art = await Artwork.LookupAsync(track);
        _presence.Show(track, art);

        var prefix = track.Playing ? "" : "Paused · ";
        SetStatus($"{prefix}{track.PlayerName}: {track.Artist} – {track.Title}");
    }

    private void SetStatus(string track)
    {
        var discord = _config.Discord.ClientId.Length == 0 ? "Discord: not set up"
            : _presence.Connected ? "Discord: connected"
            : "Discord: not connected";
        if ((track, discord) == _lastStatus)
            return;
        _lastStatus = (track, discord);
        StatusChanged?.Invoke(track, discord);
    }
}
