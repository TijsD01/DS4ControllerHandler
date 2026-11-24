using System.Text;

namespace DS4Handler.App.Infrastructure;

/// <summary>
/// Tiny append-only log in %APPDATA%\DS4ControllerHandler\log.txt (rotated at 1 MB).
/// Never used in the per-report hot path.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DS4ControllerHandler");

    public static string FilePath { get; } = Path.Combine(DataDirectory, "log.txt");

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').Append(level).Append(' ')
            .Append(message);
        if (ex != null) line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        System.Diagnostics.Debug.WriteLine(line.ToString());
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DataDirectory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                {
                    File.Copy(FilePath, FilePath + ".old", overwrite: true);
                    File.Delete(FilePath);
                }
                File.AppendAllText(FilePath, line.Append(Environment.NewLine).ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
