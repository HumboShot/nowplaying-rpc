using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace NowPlayingRpc;

public sealed class App : Application
{
    private Tracker? _tracker;
    private TrayIcon? _tray;
    private NativeMenuItem? _trackStatus;
    private NativeMenuItem? _discordStatus;

    // On Windows, Avalonia draws the tray menu as its own popup, which has no look without a theme.
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        Log.Start();
        var config = Config.TryLoad();
        _tracker = new Tracker(config ?? new Config());
        _tracker.StatusChanged += (track, discord) => Dispatcher.UIThread.Post(() => ShowStatus(track, discord));

        _trackStatus = new NativeMenuItem(config == null ? "Config error – see log" : "Starting…") { IsEnabled = false };
        _discordStatus = new NativeMenuItem("Discord: not connected") { IsEnabled = false };

        // Use our own state, because some platforms toggle a check box item before Click.
        var pause = new NativeMenuItem("Pause presence") { ToggleType = NativeMenuItemToggleType.CheckBox };
        pause.Click += (_, _) =>
        {
            _tracker.Paused = !_tracker.Paused;
            pause.IsChecked = _tracker.Paused;
        };

        var autostart = new NativeMenuItem("Start with system")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = Autostart.IsEnabled,
        };
        autostart.Click += (_, _) =>
        {
            Autostart.Set(!Autostart.IsEnabled);
            autostart.IsChecked = Autostart.IsEnabled;
        };

        var openConfig = new NativeMenuItem("Open config file");
        openConfig.Click += (_, _) => OpenFile(Config.FilePath);

        var reload = new NativeMenuItem("Reload config");
        reload.Click += (_, _) =>
        {
            if (Config.TryLoad() is { } newConfig)
                _tracker.Reload(newConfig);
            else
                ShowStatus("Config error – see log", _discordStatus.Header ?? "");
        };

        var openLog = new NativeMenuItem("Open log");
        openLog.Click += (_, _) => OpenFile(Log.FilePath);

        var close = new NativeMenuItem("Close");
        close.Click += async (_, _) =>
        {
            await _tracker.StopAsync();
            _tray?.Dispose();
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://NowPlayingRpc/Assets/icon.png"))),
            ToolTipText = "NowPlaying RPC",
            Menu = new NativeMenu
            {
                Items =
                {
                    _trackStatus,
                    _discordStatus,
                    new NativeMenuItemSeparator(),
                    pause,
                    autostart,
                    new NativeMenuItemSeparator(),
                    openConfig,
                    reload,
                    openLog,
                    new NativeMenuItemSeparator(),
                    close,
                },
            },
            IsVisible = true,
        };
        TrayIcon.SetIcons(this, [_tray]);

        _tracker.Start();
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowStatus(string track, string discord)
    {
        if (_trackStatus != null)
            _trackStatus.Header = track;
        if (_discordStatus != null)
            _discordStatus.Header = discord;
        if (_tray != null)
        {
            // Windows limits the tooltip to 127 characters, so shorten the track line to fit.
            var max = 126 - discord.Length - 1;
            if (track.Length > max)
                track = track[..(max - 1)] + "…";
            _tray.ToolTipText = $"{track}\n{discord}";
        }
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
