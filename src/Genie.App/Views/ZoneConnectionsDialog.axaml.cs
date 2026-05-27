using Avalonia.ReactiveUI;
using Genie.App.ViewModels;
using ReactiveUI;

namespace Genie.App.Views;

public partial class ZoneConnectionsDialog : ReactiveWindow<ZoneConnectionsViewModel>
{
    public ZoneConnectionsDialog()
    {
        InitializeComponent();
        this.WhenActivated(d =>
        {
            // Save doesn't close — the user often saves multiple times
            // while editing. Close button explicitly dismisses the
            // dialog with output=false.
            d(ViewModel!.CloseCommand.Subscribe(_ => Close(false)));
        });
    }
}
