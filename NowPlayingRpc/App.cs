#if !WINDOWS
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace NowPlayingRpc;

/// <summary>The tray icon on macOS and Linux. Avalonia shows the menu with the native system menu there.</summary>
public sealed class App : Application
{
    private TrayController? _controller;
    private TrayIcon? _tray;

    public override void OnFrameworkInitializationCompleted()
    {
        _controller = new TrayController(action => Dispatcher.UIThread.Post(action));
        var menu = new NativeMenu();
        var pairs = new List<(TrayMenuItem Item, NativeMenuItem Native)>();
        foreach (var item in _controller.Items)
        {
            if (item == null)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }
            var native = new NativeMenuItem(item.Text)
            {
                IsEnabled = item.Enabled,
                ToggleType = item.IsCheckBox ? NativeMenuItemToggleType.CheckBox : NativeMenuItemToggleType.None,
                IsChecked = item.Checked,
                Command = item.Click != null ? new MenuCommand(item.Click) : null,
            };
            menu.Items.Add(native);
            pairs.Add((item, native));
        }

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(NowPlayingRpc.Resources.Icon()),
            ToolTipText = _controller.ToolTip,
            Menu = menu,
            IsVisible = true,
        };
        TrayIcon.SetIcons(this, [_tray]);

        _controller.Changed += () =>
        {
            foreach (var (item, native) in pairs)
            {
                native.Header = item.Text;
                native.IsChecked = item.Checked;
            }
            _tray.ToolTipText = _controller.ToolTip;
        };
        _controller.CloseRequested += () =>
        {
            _tray.Dispose();
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };

        _controller.Start();
        base.OnFrameworkInitializationCompleted();
    }

    private sealed class MenuCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
#endif
