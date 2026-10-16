namespace UbisoftAutoLogin.Core;

/// <summary>
/// Tiny local file logger. Callers must never pass secrets; every line is additionally run
/// through <see cref="Redact.Sanitize"/> so e-mail addresses and token-like strings are redacted.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1_000_000;

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UbisoftAutoLogin");

    public static string FilePath => Path.Combine(Directory, "autologin.log");

    public static event Action<string>? LineWritten;

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Warn(string msg) => Write("WARN ", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {Redact.Sanitize(msg, 400)}";
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch { /* logging must never break the app */ }
        }
        LineWritten?.Invoke(line);
    }
}
