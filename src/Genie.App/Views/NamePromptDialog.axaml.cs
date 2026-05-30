using Avalonia.Controls;

namespace Genie.App.Views;

/// <summary>
/// Small reusable "enter a name" prompt. Returns the entered string
/// on OK or null on Cancel. Used today by the Layout → Save Layout As
/// flow; can be reused for other naming prompts (waypoint names, etc.).
///
/// <para>When <c>existingNames</c> is supplied, the dialog also shows a
/// click-to-reuse list of those names above the text field — picking one
/// fills the field so OK overwrites that entry. Without it the dialog is
/// the compact name-only prompt.</para>
/// </summary>
public partial class NamePromptDialog : Window
{
    public NamePromptDialog()
    {
        InitializeComponent();
    }

    public static async Task<string?> Show(
        Window owner, string promptText, string defaultValue, string title,
        IEnumerable<string>? existingNames = null)
    {
        var dlg = new NamePromptDialog { Title = title };
        dlg.PromptText.Text = promptText;
        dlg.NameInput.Text  = defaultValue;

        var names = existingNames?.ToList() ?? new List<string>();
        if (names.Count > 0)
        {
            dlg.ExistingList.ItemsSource = names;
            dlg.ExistingList.SelectionChanged += (_, _) =>
            {
                if (dlg.ExistingList.SelectedItem is string s)
                {
                    dlg.NameInput.Text = s;
                    dlg.NameInput.SelectAll();
                }
            };
        }
        else
        {
            // Compact prompt: hide the existing-items section and shrink.
            dlg.ExistingHeader.IsVisible = false;
            dlg.ExistingList.IsVisible   = false;
            dlg.Height = 160;
        }

        dlg.NameInput.SelectAll();
        dlg.NameInput.Focus();
        return await dlg.ShowDialog<string?>(owner);
    }

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var name = NameInput.Text?.Trim();
        Close(string.IsNullOrEmpty(name) ? null : name);
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close((string?)null);
    }
}
