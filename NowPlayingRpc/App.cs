using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NowPlayingRpc;

public sealed class App : Application
{
    private Tracker? _tracker;
    private TrayIcon? _tray;
    private NativeMenuItem? _status;

    public override void OnFrameworkInitializationCompleted()
    {
        Log.Start();
        var config = Config.TryLoad();
        _tracker = new Tracker(config ?? new Config());
        _tracker.StatusChanged += text => Dispatcher.UIThread.Post(() => ShowStatus(text));

        _status = new NativeMenuItem(config == null ? "Config error – see log" : "Starting…") { IsEnabled = false };

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
                ShowStatus("Config error – see log");
        };

        var openLog = new NativeMenuItem("Open log");
        openLog.Click += (_, _) => OpenFile(Log.FilePath);

        var quit = new NativeMenuItem("Quit");
        quit.Click += async (_, _) =>
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
                    _status,
                    new NativeMenuItemSeparator(),
                    pause,
                    autostart,
                    new NativeMenuItemSeparator(),
                    openConfig,
                    reload,
                    openLog,
                    new NativeMenuItemSeparator(),
                    quit,
                },
            },
            IsVisible = true,
        };
        TrayIcon.SetIcons(this, [_tray]);

        _tracker.Start();
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowStatus(string text)
    {
        if (_status != null)
            _status.Header = text;
        if (_tray != null)
            // Windows limits the tooltip to 127 characters.
            _tray.ToolTipText = text.Length > 120 ? text[..120] + "…" : text;
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
