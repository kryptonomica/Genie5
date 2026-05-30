using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie.App.ViewModels;

namespace Genie.App.Views;

/// <summary>
/// Save Layout As prompt with a scope selector (This profile / Global) and a
/// click-to-overwrite list of the existing layouts in the selected scope.
/// Returns a <see cref="LayoutSaveResult"/> (name + scope) on OK, null on
/// Cancel. When no profile is connected, the scope is forced to Global.
/// </summary>
public partial class SaveLayoutDialog : Window
{
    private IReadOnlyList<string> _profileNames = Array.Empty<string>();
    private IReadOnlyList<string> _globalNames  = Array.Empty<string>();

    public SaveLayoutDialog()
    {
        InitializeComponent();
    }

    public static async Task<LayoutSaveResult?> Show(Window owner, LayoutSavePrompt prompt)
    {
        var dlg = new SaveLayoutDialog
        {
            _profileNames = prompt.ProfileNames,
            _globalNames  = prompt.GlobalNames,
        };
        dlg.NameInput.Text = prompt.DefaultName;

        if (prompt.ProfileAvailable)
        {
            dlg.ProfileRadio.IsChecked = true;   // default to the profile when connected
        }
        else
        {
            dlg.ProfileRadio.IsEnabled = false;  // no profile — Global only
            dlg.GlobalRadio.IsChecked  = true;
        }

        dlg.ProfileRadio.IsCheckedChanged += (_, _) => dlg.RefreshList();
        dlg.GlobalRadio.IsCheckedChanged  += (_, _) => dlg.RefreshList();
        dlg.ExistingList.SelectionChanged += (_, _) =>
        {
            if (dlg.ExistingList.SelectedItem is string s)
            {
                dlg.NameInput.Text = s;
                dlg.NameInput.SelectAll();
            }
        };
        dlg.RefreshList();

        dlg.NameInput.SelectAll();
        dlg.NameInput.Focus();
        return await dlg.ShowDialog<LayoutSaveResult?>(owner);
    }

    private void RefreshList()
    {
        ExistingList.ItemsSource = GlobalRadio.IsChecked == true ? _globalNames : _profileNames;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var name = NameInput.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { Close((LayoutSaveResult?)null); return; }
        var scope = GlobalRadio.IsChecked == true ? LayoutScope.Global : LayoutScope.Profile;
        Close(new LayoutSaveResult(name, scope));
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((LayoutSaveResult?)null);
}
