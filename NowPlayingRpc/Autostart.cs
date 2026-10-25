using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NowPlayingRpc;

/// <summary>Start the program when the user logs in.</summary>
public static class Autostart
{
    private const string Name = "NowPlayingRpc";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

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
                return WindowsEnabled();
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
    private static bool WindowsEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        // The first byte is even (for example 02) when the entry is on, and odd (03) when the user turned it off
        // in Task Manager or Settings.
        return run?.GetValue(Name) != null
            && approved?.GetValue(Name) is byte[] { Length: > 0 } state
            && state[0] % 2 == 0;
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindows(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        // Newer Windows 11 builds skip a Run entry that has no StartupApproved entry,
        // so write both. This is the same entry that Task Manager writes when you enable an app.
        using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey);
        if (enabled)
        {
            run.SetValue(Name, $"\"{ExePath}\"");
            approved.SetValue(Name, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
        else
        {
            run.DeleteValue(Name, false);
            approved.DeleteValue(Name, false);
        }
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
