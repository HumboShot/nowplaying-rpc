using System.Diagnostics;

namespace NowPlayingRpc;

public sealed class TrayMenuItem(string text)
{
    public string Text { get; set; } = text;
    public bool Enabled { get; init; } = true;
    public bool IsCheckBox { get; init; }
    public bool Checked { get; set; }
    public Action? Click { get; init; }
}

/// <summary>
/// The tray menu and tooltip, without a UI toolkit. Windows shows it with Win32Tray,
/// and macOS and Linux show it with an Avalonia tray icon.
/// </summary>
public sealed class TrayController
{
    private readonly Action<Action> _post;
    private readonly Tracker _tracker;
    private readonly TrayMenuItem _trackStatus;
    private readonly TrayMenuItem _discordStatus;

    /// <summary>The menu items. A null item is a separator.</summary>
    public IReadOnlyList<TrayMenuItem?> Items { get; }

    public string ToolTip { get; private set; } = "NowPlaying RPC";

    /// <summary>Raised on the UI thread when a text, a check mark or the tooltip changes.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread when the program must stop.</summary>
    public event Action? CloseRequested;

    /// <param name="post">Runs an action on the UI thread.</param>
    public TrayController(Action<Action> post)
    {
        _post = post;
        Log.Start();
        var config = Config.TryLoad();
        _tracker = new Tracker(config ?? new Config());
        _tracker.StatusChanged += (track, discord) => _post(() => ShowStatus(track, discord));

        _trackStatus = new TrayMenuItem(config == null ? "Config error – see log" : "Starting…") { Enabled = false };
        _discordStatus = new TrayMenuItem("Discord: not connected") { Enabled = false };

        TrayMenuItem pause = null!;
        pause = new TrayMenuItem("Pause presence")
        {
            IsCheckBox = true,
            Click = () =>
            {
                _tracker.Paused = !_tracker.Paused;
                pause.Checked = _tracker.Paused;
                Changed?.Invoke();
            },
        };

        TrayMenuItem autostart = null!;
        autostart = new TrayMenuItem("Start with system")
        {
            IsCheckBox = true,
            Checked = Autostart.IsEnabled,
            Click = () =>
            {
                Autostart.Set(!Autostart.IsEnabled);
                autostart.Checked = Autostart.IsEnabled;
                Changed?.Invoke();
            },
        };

        Items =
        [
            _trackStatus,
            _discordStatus,
            null,
            pause,
            autostart,
            null,
            new TrayMenuItem("Open config file") { Click = () => OpenFile(Config.FilePath) },
            new TrayMenuItem("Reload config") { Click = Reload },
            new TrayMenuItem("Open log") { Click = () => OpenFile(Log.FilePath) },
            null,
            new TrayMenuItem("Close") { Click = () => _ = CloseAsync() },
        ];
    }

    public void Start() => _tracker.Start();

    private void Reload()
    {
        if (Config.TryLoad() is { } config)
            _tracker.Reload(config);
        else
            ShowStatus("Config error – see log", _discordStatus.Text);
    }

    private async Task CloseAsync()
    {
        await _tracker.StopAsync();
        _post(() => CloseRequested?.Invoke());
    }

    private void ShowStatus(string track, string discord)
    {
        _trackStatus.Text = track;
        _discordStatus.Text = discord;
        // Windows limits the tooltip to 127 characters, so shorten the track line to fit.
        var max = 126 - discord.Length - 1;
        if (track.Length > max)
            track = track[..(max - 1)] + "…";
        ToolTip = $"{track}\n{discord}";
        Changed?.Invoke();
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [path]);
        }
        catch (Exception e)
        {
            Log.Write($"Cannot open {path}: {e.Message}");
        }
    }
}
