using Avalonia.ReactiveUI;
using Genie.App.ViewModels;
using ReactiveUI;

namespace Genie.App.Views;

public partial class DisplaySettingsDialog : ReactiveWindow<DisplaySettingsViewModel>
{
    public DisplaySettingsDialog()
    {
        InitializeComponent();
        this.WhenActivated(d =>
        {
            d(ViewModel!.OkCommand    .Subscribe(result => Close(result)));
            d(ViewModel!.CancelCommand.Subscribe(result => Close(result)));
        });
    }
}
