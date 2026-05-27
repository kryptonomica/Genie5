using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using Genie.App.ViewModels;
using ReactiveUI;

namespace Genie.App.Views;

public partial class Genie4ImportDialog : ReactiveWindow<Genie4ImportViewModel>
{
    public Genie4ImportDialog()
    {
        InitializeComponent();
        this.WhenActivated(d =>
        {
            // Close fires from CancelCommand (the "Close" button at the bottom)
            // OR from a successful Import — but Import doesn't auto-close,
            // it just shows the result so the user can read it. They click
            // Close when ready.
            d(ViewModel!.CancelCommand.Subscribe(_ => Close()));

            // Browse → OS folder picker → push the result back into the VM,
            // which fires the throttled Probe. Subscribed-not-disposed here;
            // the event handler dies with the VM when the dialog closes.
            ViewModel!.BrowseRequested += async () => await PickFolder();
        });
    }

    private async Task PickFolder()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var startLocation = !string.IsNullOrWhiteSpace(ViewModel?.SourcePath)
                             && Directory.Exists(ViewModel.SourcePath)
            ? await top.StorageProvider.TryGetFolderFromPathAsync(ViewModel.SourcePath)
            : null;

        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title                  = "Select Genie 4 Config folder",
            SuggestedStartLocation = startLocation,
            AllowMultiple          = false,
        });

        var folder = picked.FirstOrDefault();
        if (folder is null) return;

        ViewModel!.SetSourcePathFromBrowse(folder.Path.LocalPath);
    }
}
