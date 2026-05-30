using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using Genie.App.ViewModels;

namespace Genie.App.Views;

public partial class ManageLayoutsDialog : ReactiveWindow<ManageLayoutsViewModel>
{
    public ManageLayoutsDialog()
    {
        InitializeComponent();
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Selected is not { } sel) return;
        var newName = await NamePromptDialog.Show(
            this, "Rename layout to:", sel.Name, "Rename Layout");
        if (!string.IsNullOrWhiteSpace(newName))
            ViewModel.Rename(newName);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
