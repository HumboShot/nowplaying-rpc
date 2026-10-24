namespace NowPlayingRpc.Sources;

public interface ISource
{
    string Name { get; }
    Task<IReadOnlyList<Track>> GetTracksAsync();
}

public static class SourceFactory
{
    /// <summary>Map a media session app ID or MPRIS bus name to a supported player.</summary>
    public static string? PlayerFromAppId(string appId)
    {
        appId = appId.ToLowerInvariant();
        if (appId.Contains("plexamp")) return "plexamp";
        if (appId.Contains("spotify")) return "spotify";
        if (appId.Contains("applemusic") || appId.Contains("itunes")) return "apple_music";
        if (appId.Contains("foobar2000")) return "foobar2000";
        return null;
    }

    public static IReadOnlyList<ISource> Build(Config config)
    {
        var sources = new List<ISource>();
        if (config.MediaSession.Enabled)
        {
#if WINDOWS
            sources.Add(new WindowsMediaSource());
#endif
            if (OperatingSystem.IsMacOS())
                sources.Add(new MacOSSource());
            else if (OperatingSystem.IsLinux())
                sources.Add(new MprisSource());
        }
        if (config.Plex.Enabled)
            sources.Add(new PlexSource(config.Plex));
        if (config.Foobar2000.Enabled)
            sources.Add(new BeefwebSource(config.Foobar2000));
        return sources;
    }
}
