using System.Diagnostics;
using System.Globalization;

namespace NowPlayingRpc.Sources;

/// <summary>Read Spotify and Apple Music (Music.app) through AppleScript.</summary>
public sealed class MacOSSource : ISource
{
    // The "is running" check comes first, so the script never starts a closed app.
    private const string SpotifyScript = """
        if application "Spotify" is running then
            tell application "Spotify"
                if player state is stopped then return ""
                set t to current track
                return (player state as string) & linefeed & (name of t) & linefeed & (artist of t) & linefeed & (album of t) & linefeed & ((duration of t) / 1000) & linefeed & player position & linefeed & (artwork url of t)
            end tell
        end if
        return ""
        """;

    private const string MusicScript = """
        if application "Music" is running then
            tell application "Music"
                if player state is stopped then return ""
                set t to current track
                return (player state as string) & linefeed & (name of t) & linefeed & (artist of t) & linefeed & (album of t) & linefeed & (duration of t) & linefeed & player position & linefeed & ""
            end tell
        end if
        return ""
        """;

    public string Name => "macos";

    public async Task<IReadOnlyList<Track>> GetTracksAsync()
    {
        var tracks = await Task.WhenAll(RunAsync("spotify", SpotifyScript), RunAsync("apple_music", MusicScript));
        return tracks.OfType<Track>().ToList();
    }

    private static async Task<Track?> RunAsync(string player, string script)
    {
        var psi = new ProcessStartInfo("osascript", ["-e", script])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi)!;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
            return null;

        var lines = output.TrimEnd('\n').Split('\n');
        if (lines.Length < 7)
            return null;
        var state = lines[0];
        if (state is not ("playing" or "paused") || lines[1].Length == 0)
            return null;
        return new Track(
            player,
            lines[1],
            lines[2],
            lines[3],
            Number(lines[4]),
            Number(lines[5]),
            state == "playing",
            lines[6].Length > 0 ? lines[6] : null);
    }

    // AppleScript uses the system locale, so the decimal mark can be a comma.
    private static double? Number(string value) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
}
