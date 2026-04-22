namespace Bifrost.Core;

public static class Logger
{
    private static Action<string>? _handler;
    private static StreamWriter? _fileWriter;

    public static void SetHandler(Action<string> handler) => _handler = handler;
    public static void UseConsole() => _handler = Console.WriteLine;

    /// <summary>
    /// Write all log lines to a file in real time in addition to the normal handler.
    /// Call at startup with a path like bifrost-latest.log.
    /// </summary>
    public static void UseFile(string path)
    {
        _fileWriter?.Dispose();
        _fileWriter = new StreamWriter(path, append: false, System.Text.Encoding.UTF8)
        {
            AutoFlush = true,
        };
    }

    public static void StopFile()
    {
        _fileWriter?.Dispose();
        _fileWriter = null;
    }

    public static void Log(string message)
    {
        _handler?.Invoke(message);
        _fileWriter?.WriteLine(message);
    }

    public static void Log(string format, params object[] args)
        => Log(string.Format(format, args));
}