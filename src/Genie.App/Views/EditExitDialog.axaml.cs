using Avalonia.ReactiveUI;
using Genie.App.ViewModels;
using ReactiveUI;

namespace Genie.App.Views;

public partial class EditExitDialog : ReactiveWindow<EditExitViewModel>
{
    public EditExitDialog()
    {
        InitializeComponent();
        this.WhenActivated(d =>
        {
            d(ViewModel!.OkCommand    .Subscribe(ok => Close(ok)));
            d(ViewModel!.CancelCommand.Subscribe(_  => Close(false)));
        });
    }
}
