namespace NowPlayingRpc;

public sealed record Track(
    string Player,
    string Title,
    string Artist = "",
    string Album = "",
    double? Duration = null,
    double? Position = null,
    bool Playing = true,
    string? ArtUrl = null,
    string AlbumArtist = "")
{
    public static readonly IReadOnlyDictionary<string, string> PlayerNames = new Dictionary<string, string>
    {
        ["plexamp"] = "Plexamp",
        ["spotify"] = "Spotify",
        ["apple_music"] = "Apple Music",
        ["foobar2000"] = "foobar2000",
    };

    public string PlayerName => PlayerNames.GetValueOrDefault(Player, Player);

    public (string, string, string, string, bool) Key => (Player, Title, Artist, Album, Playing);
}
