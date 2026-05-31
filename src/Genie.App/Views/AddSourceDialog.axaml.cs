using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Genie.Core.Update;

namespace Genie.App.Views;

/// <summary>
/// Modal for adding a new Maps or Plugins update source. Shows the
/// third-party-code acknowledgment when launched in plugin mode (the
/// Plugins tab) and hides it for Maps (XML data — not executable). The
/// caller awaits <see cref="ShowAsync"/> which returns the parsed
/// <see cref="FeedEntry"/> on success or null on cancel/invalid input.
///
/// Parsing is delegated to <see cref="PluginSourceParser"/>; the dialog
/// just validates and surfaces the parser's error message inline.
/// </summary>
public partial class AddSourceDialog : Window
{
    private FeedEntry? _result;
    private bool       _requireAcknowledgment;

    public AddSourceDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the dialog modally. When <paramref name="forPlugin"/> is true,
    /// the third-party-code panel appears and the user must check the
    /// acknowledgment box before Add is enabled. Maps mode skips that step.
    /// </summary>
    public static async Task<FeedEntry?> ShowAsync(Window owner, bool forPlugin)
    {
        var dlg = new AddSourceDialog();
        dlg._requireAcknowledgment = forPlugin;

        var header  = dlg.FindControl<TextBlock>("HeaderText")!;
        var warn    = dlg.FindControl<Border>("WarningPanel")!;
        var ackBox  = dlg.FindControl<CheckBox>("AcknowledgeBox")!;
        var addBtn  = dlg.FindControl<Button>("AddButton")!;

        if (forPlugin)
        {
            dlg.Title = "Add Plugin Source";
            header.Text = "Paste a GitHub repo URL or owner/repo shorthand for a plugin you want to install.";
            warn.IsVisible = true;
            addBtn.IsEnabled = false;
            ackBox.IsCheckedChanged += (_, _) => addBtn.IsEnabled = ackBox.IsChecked == true;
        }
        else
        {
            dlg.Title = "Add Maps Source";
            header.Text = "Paste a GitHub repo URL or owner/repo shorthand for a Maps repository.";
            warn.IsVisible = false;
            addBtn.IsEnabled = true;
        }

        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var input  = this.FindControl<TextBox>("UrlInput")!.Text ?? "";
        var error  = this.FindControl<TextBlock>("ErrorText")!;
        var ackBox = this.FindControl<CheckBox>("AcknowledgeBox")!;

        if (_requireAcknowledgment && ackBox.IsChecked != true)
        {
            error.Text      = "Please acknowledge the third-party-code notice to continue.";
            error.IsVisible = true;
            return;
        }

        if (!PluginSourceParser.TryParse(input, out var entry, out var err))
        {
            error.Text      = err ?? "Invalid source URL.";
            error.IsVisible = true;
            return;
        }

        _result = entry;
        Close();
    }
}
