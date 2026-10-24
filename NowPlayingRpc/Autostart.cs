using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NowPlayingRpc;

/// <summary>Start the program when the user logs in.</summary>
public static class Autostart
{
    private const string Name = "NowPlayingRpc";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string ExePath => Environment.ProcessPath ?? "";

    private static string MacPlist => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.nowplayingrpc.plist");

    private static string LinuxDesktopFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "nowplayingrpc.desktop");

    public static bool IsEnabled
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return WindowsValue() != null;
            return File.Exists(OperatingSystem.IsMacOS() ? MacPlist : LinuxDesktopFile);
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                SetWindows(enabled);
            else if (OperatingSystem.IsMacOS())
                SetFile(MacPlist, enabled, $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                    <plist version="1.0">
                    <dict>
                        <key>Label</key><string>com.nowplayingrpc</string>
                        <key>ProgramArguments</key><array><string>{ExePath}</string></array>
                        <key>RunAtLoad</key><true/>
                    </dict>
                    </plist>
                    """);
            else
                SetFile(LinuxDesktopFile, enabled, $"""
                    [Desktop Entry]
                    Type=Application
                    Name=NowPlaying RPC
                    Exec="{ExePath}"
                    X-GNOME-Autostart-enabled=true
                    """);
        }
        catch (Exception e)
        {
            Log.Write($"Cannot change autostart: {e.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static object? WindowsValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name);
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(Name, $"\"{ExePath}\"");
        else
            key.DeleteValue(Name, false);
    }

    private static void SetFile(string path, bool enabled, string content)
    {
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        else
        {
            File.Delete(path);
        }
    }
}
