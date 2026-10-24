using System.Text.Json;
using System.Text.Json.Serialization;

namespace NowPlayingRpc;

public sealed class DiscordConfig
{
    public string ClientId { get; set; } = "";
    public Dictionary<string, string> ClientIds { get; set; } = new();
    /// <summary>"name" (player), "details" (song title) or "state" (artist).</summary>
    public string StatusDisplay { get; set; } = "state";
    public string LargeImageFallback { get; set; } = "";
    public bool ShowPlayerIcon { get; set; }
}

public sealed class GeneralConfig
{
    public double PollInterval { get; set; } = 2.0;
    public bool ClearOnPause { get; set; } = true;
    public List<string> Priority { get; set; } = ["plexamp", "spotify", "apple_music", "foobar2000"];
    public bool LookupArtwork { get; set; } = true;
}

public sealed class MediaSessionConfig
{
    public bool Enabled { get; set; } = true;
}

public sealed class PlexampConfig
{
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = "http://127.0.0.1:32500";
}

public sealed class PlexConfig
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = "http://127.0.0.1:32400";
    public string Token { get; set; } = "";
    public string Username { get; set; } = "";
    public bool VerifyTls { get; set; } = true;
}

public sealed class FoobarConfig
{
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = "http://127.0.0.1:8880";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class Config
{
    public DiscordConfig Discord { get; set; } = new();
    public GeneralConfig General { get; set; } = new();
    public MediaSessionConfig MediaSession { get; set; } = new();
    public PlexampConfig Plexamp { get; set; } = new();
    public PlexConfig Plex { get; set; } = new();
    public FoobarConfig Foobar2000 { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static string Directory
    {
        get
        {
            string baseDir;
            if (OperatingSystem.IsWindows())
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            else if (OperatingSystem.IsMacOS())
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
            else
                baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                    ? xdg
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(baseDir, "NowPlayingRpc");
        }
    }

    public static string FilePath => Path.Combine(Directory, "config.json");

    /// <summary>Read the config file. Return null and write the error to the log if the file is not valid.</summary>
    public static Config? TryLoad()
    {
        try
        {
            return Load();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            Log.Write($"Config error: {e.Message}");
            return null;
        }
    }

    /// <summary>Read the config file. On the first run, write a file with the default values.</summary>
    public static Config Load()
    {
        if (!File.Exists(FilePath))
        {
            var config = new Config();
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, JsonOptions));
            return config;
        }
        return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), JsonOptions) ?? new Config();
    }
}
