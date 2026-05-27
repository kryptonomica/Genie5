using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.ReactiveUI;

namespace Genie.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // ── Install bottom-most exception handlers BEFORE anything else can
        // throw. These catch failures that escape every other handler — the
        // ones responsible for "app ends without notice" exits.
        InstallCrashLogger();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrash("Main.UnhandledTopLevel", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .LogToTrace();

    // ── Last-resort crash logging ────────────────────────────────────────────

    private static string CrashLogPath { get; } = ComputeCrashLogPath();

    private static string ComputeCrashLogPath()
    {
        // Resolve a writable user-data dir without depending on any of our
        // own classes (those might be the thing that's failing).
        try
        {
            string root;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Genie5");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    "Library", "Application Support", "Genie5");
            else
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    ".local", "share", "Genie5");

            Directory.CreateDirectory(Path.Combine(root, "Config"));
            return Path.Combine(root, "Config", "genie_crash.log");
        }
        catch
        {
            return Path.Combine(AppContext.BaseDirectory, "genie_crash.log");
        }
    }

    private static void InstallCrashLogger()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrash("AppDomain.UnhandledException", ex);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrash("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        // Marker so we can confirm the logger is wired even on a clean run.
        WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Startup] Process started; crash logger installed at {CrashLogPath}");
    }

    private static void WriteCrash(string source, Exception ex)
    {
        WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.GetType().FullName}: {ex.Message}\n{ex}\n");
    }

    private static void WriteLine(string text)
    {
        try { File.AppendAllText(CrashLogPath, text + Environment.NewLine); } catch { }
    }
}
