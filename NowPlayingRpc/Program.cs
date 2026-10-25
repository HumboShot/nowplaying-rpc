#if !WINDOWS
using Avalonia;
#endif

namespace NowPlayingRpc;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--once"))
        {
            PrintOnce().GetAwaiter().GetResult();
            return;
        }

#if WINDOWS
        Win32Tray.Run();
#else
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
#endif
    }

    private static async Task PrintOnce()
    {
        var config = Config.Load();
        foreach (var track in await Tracker.Collect(Sources.SourceFactory.Build(config)))
        {
            Console.WriteLine(track);
            Console.WriteLine($"  Cover: {track.ArtUrl ?? await Artwork.LookupAsync(track) ?? "(none found)"}");
        }
    }
}
