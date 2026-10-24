using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NowPlayingRpc;

public sealed class App : Application
{
    private Tracker? _tracker;
#if WINDOWS
    private Win32Tray? _tray;
#else
    private TrayIcon? _tray;
#endif
    private NativeMenuItem? _trackStatus;
    private NativeMenuItem? _discordStatus;

    public override void OnFrameworkInitializationCompleted()
    {
        Log.Start();
        var config = Config.TryLoad();
        _tracker = new Tracker(config ?? new Config());
        _tracker.StatusChanged += (track, discord) => Dispatcher.UIThread.Post(() => ShowStatus(track, discord));

        _trackStatus = new NativeMenuItem(config == null ? "Config error – see log" : "Starting…") { IsEnabled = false };
        _discordStatus = new NativeMenuItem("Discord: not connected") { IsEnabled = false };

        // Use our own state, because some platforms toggle a check box item before the command runs.
        var pause = new NativeMenuItem("Pause presence") { ToggleType = NativeMenuItemToggleType.CheckBox };
        pause.Command = new MenuCommand(() =>
        {
            _tracker.Paused = !_tracker.Paused;
            pause.IsChecked = _tracker.Paused;
        });

        var autostart = new NativeMenuItem("Start with system")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = Autostart.IsEnabled,
        };
        autostart.Command = new MenuCommand(() =>
        {
            Autostart.Set(!Autostart.IsEnabled);
            autostart.IsChecked = Autostart.IsEnabled;
        });

        var openConfig = new NativeMenuItem("Open config file") { Command = new MenuCommand(() => OpenFile(Config.FilePath)) };

        var reload = new NativeMenuItem("Reload config")
        {
            Command = new MenuCommand(() =>
            {
                if (Config.TryLoad() is { } newConfig)
                    _tracker.Reload(newConfig);
                else
                    ShowStatus("Config error – see log", _discordStatus.Header ?? "");
            }),
        };

        var openLog = new NativeMenuItem("Open log") { Command = new MenuCommand(() => OpenFile(Log.FilePath)) };

        var close = new NativeMenuItem("Close") { Command = new MenuCommand(() => _ = CloseAsync()) };

        var menu = new NativeMenu
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
        };

#if WINDOWS
        using (var png = AssetLoader.Open(new Uri("avares://NowPlayingRpc/Assets/icon.png")))
        using (var buffer = new MemoryStream())
        {
            png.CopyTo(buffer);
            _tray = new Win32Tray(menu, buffer.ToArray()) { ToolTip = "NowPlaying RPC" };
        }
#else
        // On macOS and Linux, Avalonia shows the menu with the native system menu.
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://NowPlayingRpc/Assets/icon.png"))),
            ToolTipText = "NowPlaying RPC",
            Menu = menu,
            IsVisible = true,
        };
        TrayIcon.SetIcons(this, [_tray]);
#endif

        _tracker.Start();
        base.OnFrameworkInitializationCompleted();
    }

    private async Task CloseAsync()
    {
        await _tracker!.StopAsync();
        _tray?.Dispose();
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void ShowStatus(string track, string discord)
    {
        if (_trackStatus != null)
            _trackStatus.Header = track;
        if (_discordStatus != null)
            _discordStatus.Header = discord;

        // Windows limits the tooltip to 127 characters, so shorten the track line to fit.
        var max = 126 - discord.Length - 1;
        if (track.Length > max)
            track = track[..(max - 1)] + "…";
        var toolTip = $"{track}\n{discord}";
#if WINDOWS
        if (_tray != null)
            _tray.ToolTip = toolTip;
#else
        if (_tray != null)
            _tray.ToolTipText = toolTip;
#endif
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

    private sealed class MenuCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
