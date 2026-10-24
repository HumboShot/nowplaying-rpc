#if WINDOWS
using Windows.Media.Control;

namespace NowPlayingRpc.Sources;

/// <summary>Read Spotify, Apple Music, Plexamp and foobar2000 from Windows media controls (GSMTC).</summary>
public sealed class WindowsMediaSource : ISource
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public string Name => "windows_media";

    public async Task<IReadOnlyList<Track>> GetTracksAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var result = new List<Track>();
        foreach (var session in _manager.GetSessions())
        {
            var player = SourceFactory.PlayerFromAppId(session.SourceAppUserModelId);
            if (player == null)
                continue;
            var status = session.GetPlaybackInfo().PlaybackStatus;
            if (status is not (GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused))
                continue;
            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null || string.IsNullOrEmpty(props.Title))
                continue;

            var playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var timeline = session.GetTimelineProperties();
            var duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
            var position = timeline.Position.TotalSeconds;
            // The app reports position only now and then, so add the time since its last report.
            if (playing && timeline.LastUpdatedTime.Year > 1601)
                position += (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds;
            if (duration > 0)
                position = Math.Min(position, duration);

            result.Add(new Track(
                player,
                props.Title,
                string.IsNullOrEmpty(props.Artist) ? props.AlbumArtist : props.Artist,
                props.AlbumTitle,
                duration > 0 ? duration : null,
                duration > 0 ? position : null,
                playing));
        }
        return result;
    }
}
#endif
