namespace NowPlayingRpc;

public static class Resources
{
    public static Stream Icon() =>
        typeof(Resources).Assembly.GetManifestResourceStream("icon.png")
        ?? throw new InvalidOperationException("The icon resource is missing.");
}
