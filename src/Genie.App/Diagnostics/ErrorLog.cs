using System.IO;

namespace Genie.App.Diagnostics;

/// <summary>
/// Lightweight file logger for unhandled exceptions. Writes to
/// <c>Config/genie_error.log</c> alongside the other config files.
/// Existing log lines are preserved so a sequence of failures can be reviewed.
/// </summary>
public static class ErrorLog
{
    public static string? Path { get; private set; }

    public static void Initialize(string configDir)
    {
        Path = System.IO.Path.Combine(configDir, "genie_error.log");
    }

    public static void Log(string source, Exception ex)
    {
        if (Path is null) return;
        try
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] " +
                        $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n";
            File.AppendAllText(Path, entry);
        }
        catch
        {
            // Best-effort — never let the logger itself crash the app.
        }
    }
}
