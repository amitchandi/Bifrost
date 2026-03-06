namespace Bifrost.Core;

public static class Logger
{
    private static Action<string>? _handler;

    public static void SetHandler(Action<string> handler) => _handler = handler;
    public static void UseConsole() => _handler = Console.WriteLine;

    public static void Log(string message)
    {
        _handler?.Invoke(message);
    }

    public static void Log(string format, params object[] args)
        => Log(string.Format(format, args));
}