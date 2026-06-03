using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Genie.App.Views;
using Genie.App.ViewModels;

namespace Genie.App;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Parse launch flags (--host/--port/--profile/--mode) so an external
            // launcher (e.g. Lich Launcher) can start Genie pre-connected.
            var startup = StartupOptions.Parse(desktop.Args);
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(startup)
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
