using System.IO;
using System.Text;

namespace WinSetup.Services;

/// <summary>Thread-safe logger: writes to %LocalAppData%\WinSetup\logs and raises an event for the UI.</summary>
public static class Logger
{
    private static readonly object Gate = new();

    public static string FilePath { get; }
    public static event Action<string>? LineLogged;

    static Logger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinSetup", "logs");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {level} {message}";
        lock (Gate)
        {
            try { File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8); }
            catch { /* never let logging crash the app */ }
        }
        LineLogged?.Invoke(line);
    }
}
