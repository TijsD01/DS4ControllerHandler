// Stand-ins for the two app types that AppSettings.cs and HidHideUninstall.cs use, so those files compile into
// this (non-Windows) test project unchanged. Only the members they use.
using System.Collections.Concurrent;

namespace DS4Handler.App.Engine
{
    public enum OutputMode { Xbox360, DualShock4 }
}

namespace DS4Handler.App.Infrastructure
{
    public static class Log
    {
        /// <summary>A per-run temp folder instead of %APPDATA%\DS4ControllerHandler.</summary>
        public static string DataDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "DS4HandlerTests-" + Environment.ProcessId, "DS4ControllerHandler");

        public static readonly ConcurrentQueue<string> Lines = new();

        public static void Info(string message) => Lines.Enqueue("INFO  " + message);
        public static void Warn(string message, Exception? ex = null) => Lines.Enqueue("WARN  " + message + (ex == null ? "" : " | " + ex.GetType().Name));
        public static void Error(string message, Exception? ex = null) => Lines.Enqueue("ERROR " + message);
    }
}
