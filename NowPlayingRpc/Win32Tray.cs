#if WINDOWS
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace NowPlayingRpc;

/// <summary>
/// The tray icon and its native menu on Windows. This class calls Shell_NotifyIcon and TrackPopupMenuEx
/// directly and runs its own message loop. No UI toolkit is loaded, so the program does not start a
/// GPU renderer, which uses a lot of memory.
/// </summary>
public sealed class Win32Tray : IDisposable
{
    private const uint WM_NULL = 0x0000;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_TRAY = 0x8000 + 1;
    private const uint WM_RUN_QUEUE = 0x8000 + 2;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;

    private readonly TrayController _controller;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly WndProc _wndProc;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private readonly uint _taskbarCreated;
    private string _toolTip = "";

    /// <summary>Show the tray icon and run the message loop until the user selects Close.</summary>
    public static void Run()
    {
        using var tray = new Win32Tray();
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private Win32Tray()
    {
        AllowDarkMenus();

        // Keep the delegate in a field, so the garbage collector does not free it while Windows uses it.
        _wndProc = OnMessage;
        var className = "NowPlayingRpcTray";
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = className,
        };
        RegisterClassExW(ref wc);
        // A hidden top-level window, not a message-only window, because only top-level windows get TaskbarCreated.
        _hwnd = CreateWindowExW(0, className, className, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        using (var png = Resources.Icon())
        using (var buffer = new MemoryStream())
        {
            png.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var size = GetSystemMetrics(49); // SM_CXSMICON
            _icon = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, size, size, 0);
        }

        _controller = new TrayController(Post);
        _toolTip = _controller.ToolTip;
        _controller.Changed += () => SetToolTip(_controller.ToolTip);
        _controller.CloseRequested += () => PostQuitMessage(0);
        AddIcon();
        _controller.Start();
    }

    /// <summary>Run an action on the message loop thread. Other threads can call this.</summary>
    private void Post(Action action)
    {
        _queue.Enqueue(action);
        PostMessageW(_hwnd, WM_RUN_QUEUE, IntPtr.Zero, IntPtr.Zero);
    }

    private void SetToolTip(string toolTip)
    {
        if (toolTip == _toolTip)
            return;
        _toolTip = toolTip;
        var data = NewData(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void AddIcon()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        // This fails when Explorer is not ready yet. Then TaskbarCreated adds the icon later.
        if (!Shell_NotifyIconW(NIM_ADD, ref data))
            Log.Write("Cannot add the tray icon yet");
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    private NOTIFYICONDATAW NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_TRAY,
        hIcon = _icon,
        szTip = _toolTip.Length > 127 ? _toolTip[..127] : _toolTip,
    };

    private IntPtr OnMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_RUN_QUEUE)
        {
            while (_queue.TryDequeue(out var action))
                Safe(action);
            return IntPtr.Zero;
        }
        if (msg == WM_TRAY)
        {
            // With NOTIFYICON_VERSION_4, the low word of lParam holds the mouse event.
            var ev = (uint)(lParam.ToInt64() & 0xFFFF);
            if (ev is WM_CONTEXTMENU or WM_LBUTTONUP)
                ShowMenu();
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated)
        {
            // Explorer restarted and lost all tray icons.
            AddIcon();
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var items = _controller.Items;
        var hMenu = CreatePopupMenu();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not { } item)
            {
                AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
                continue;
            }
            uint flags = 0;
            if (!item.Enabled)
                flags |= MF_GRAYED;
            if (item.IsCheckBox && item.Checked)
                flags |= MF_CHECKED;
            // An "&" marks a keyboard shortcut in a Windows menu, so double it to show it as text.
            AppendMenuW(hMenu, flags, (UIntPtr)(i + 1), item.Text.Replace("&", "&&"));
        }

        GetCursorPos(out var pt);
        // The menu closes correctly on an outside click only when our window is in the foreground.
        SetForegroundWindow(_hwnd);
        var id = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(hMenu);

        if (id > 0 && items[id - 1]?.Click is { } click)
            Safe(click);
    }

    // An exception that leaves a window procedure stops the program, so log it here.
    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Write($"Error: {e}");
        }
    }

    /// <summary>Make native menus follow the dark or light Windows theme.</summary>
    private static void AllowDarkMenus()
    {
        // SetPreferredAppMode (ordinal 135) and FlushMenuThemes (ordinal 136) have no documented name,
        // but Windows 10 1903 and later have them, and many apps (for example Notepad++) use them.
        try
        {
            SetPreferredAppMode(1); // AllowDark
            FlushMenuThemes();
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    public void Dispose()
    {
        var data = NewData(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        DestroyIcon(_icon);
        DestroyWindow(_hwnd);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconFromResourceEx(byte[] data, uint size, bool icon, uint version, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();
}
#endif
