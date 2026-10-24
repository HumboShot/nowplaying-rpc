using Tmds.DBus.Protocol;

namespace NowPlayingRpc.Sources;

/// <summary>Read Spotify, Plexamp and other players from MPRIS on the D-Bus session bus.</summary>
public sealed class MprisSource : ISource
{
    private const string Prefix = "org.mpris.MediaPlayer2.";
    private const string ObjectPath = "/org/mpris/MediaPlayer2";

    public string Name => "mpris";

    public async Task<IReadOnlyList<Track>> GetTracksAsync()
    {
        var connection = Connection.Session;
        var result = new List<Track>();
        foreach (var busName in await connection.ListServicesAsync())
        {
            if (!busName.StartsWith(Prefix))
                continue;
            var player = SourceFactory.PlayerFromAppId(busName);
            if (player == null)
                continue;

            Dictionary<string, VariantValue> props;
            try
            {
                props = await GetAllAsync(connection, busName);
            }
            catch (DBusException)
            {
                continue;
            }

            var status = props.TryGetValue("PlaybackStatus", out var s) ? Unwrap(s).GetString() : "";
            if (status is not ("Playing" or "Paused"))
                continue;
            var meta = props.TryGetValue("Metadata", out var m)
                ? Unwrap(m).GetDictionary<string, VariantValue>()
                : [];
            var title = Str(meta, "xesam:title");
            if (title.Length == 0)
                continue;

            var length = Long(meta, "mpris:length");
            long? position = props.TryGetValue("Position", out var p) ? Unwrap(p).GetInt64() : null;
            var artist = meta.TryGetValue("xesam:artist", out var a) && Unwrap(a).Type == VariantValueType.Array
                ? string.Join(", ", Unwrap(a).GetArray<string>())
                : "";
            var art = Str(meta, "mpris:artUrl");

            result.Add(new Track(
                player,
                title,
                artist,
                Str(meta, "xesam:album"),
                length > 0 ? length / 1_000_000.0 : null,
                position != null && length > 0 ? position / 1_000_000.0 : null,
                status == "Playing",
                // Local file:// covers cannot reach Discord.
                art.StartsWith("https://") ? art : null,
                meta.TryGetValue("xesam:albumArtist", out var aa) && Unwrap(aa).Type == VariantValueType.Array
                    ? string.Join(", ", Unwrap(aa).GetArray<string>())
                    : ""));
        }
        return result;
    }

    private static Task<Dictionary<string, VariantValue>> GetAllAsync(Connection connection, string busName)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: busName,
            path: ObjectPath,
            @interface: "org.freedesktop.DBus.Properties",
            member: "GetAll",
            signature: "s");
        writer.WriteString("org.mpris.MediaPlayer2.Player");
        return connection.CallMethodAsync(
            writer.CreateMessage(),
            (message, _) => message.GetBodyReader().ReadDictionaryOfStringToVariantValue());
    }

    private static VariantValue Unwrap(VariantValue value) =>
        value.Type == VariantValueType.Variant ? value.GetVariantValue() : value;

    private static string Str(Dictionary<string, VariantValue> meta, string key) =>
        meta.TryGetValue(key, out var v) && Unwrap(v).Type == VariantValueType.String ? Unwrap(v).GetString() : "";

    // Players send the length as int64 or uint64.
    private static long Long(Dictionary<string, VariantValue> meta, string key)
    {
        if (!meta.TryGetValue(key, out var v))
            return 0;
        v = Unwrap(v);
        return v.Type switch
        {
            VariantValueType.Int64 => v.GetInt64(),
            VariantValueType.UInt64 => (long)v.GetUInt64(),
            VariantValueType.Int32 => v.GetInt32(),
            _ => 0,
        };
    }
}
