using Avalonia.Controls;

namespace Genie.App.Views;

/// <summary>
/// Small reusable "enter a name" prompt. Returns the entered string
/// on OK or null on Cancel. Used today by the Layout → Save Layout As
/// flow; can be reused for other naming prompts (waypoint names, etc.).
/// </summary>
public partial class NamePromptDialog : Window
{
    public NamePromptDialog()
    {
        InitializeComponent();
    }

    public static async Task<string?> Show(Window owner, string promptText, string defaultValue, string title)
    {
        var dlg = new NamePromptDialog { Title = title };
        dlg.PromptText.Text = promptText;
        dlg.NameInput.Text  = defaultValue;
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
