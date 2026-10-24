using System.Globalization;

namespace NowPlayingRpc;

public static class Log
{
    private static readonly object Lock = new();

    public static string FilePath => Path.Combine(Config.Directory, "log.txt");

    public static void Start()
    {
        Directory.CreateDirectory(Config.Directory);
        File.WriteAllText(FilePath, "");
    }

    public static void Write(string message)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture) + message;
        Console.WriteLine(line);
        lock (Lock)
        {
            try
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }
    }
}
