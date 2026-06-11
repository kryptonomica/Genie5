using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Genie.App.Views;

/// <summary>
/// The Help → About dialog. Self-contained (no view-model): shows the app
/// version (read from <see cref="Genie.Core.GenieCore.HostVersionString"/> — the
/// single source of truth, the csproj <c>InformationalVersion</c>), runtime,
/// quick links, license, and credits.
/// </summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = Genie.Core.GenieCore.HostVersionString;
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* best effort — a missing browser shouldn't crash the dialog */ }
    }

    private void OnGitHub  (object? sender, RoutedEventArgs e) => Open("https://github.com/GenieClient/Genie5");
    private void OnWiki    (object? sender, RoutedEventArgs e) => Open("https://github.com/GenieClient/Genie5/wiki");
    private void OnDiscord (object? sender, RoutedEventArgs e) => Open("https://discord.gg/MtmzE2w");
    private void OnReleases(object? sender, RoutedEventArgs e) => Open("https://github.com/GenieClient/Genie5/releases/latest");
    private void OnClose   (object? sender, RoutedEventArgs e) => Close();
}
