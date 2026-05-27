using Avalonia.Threading;
using Avalonia.ReactiveUI;
using Genie.Core.Connection;
using Genie.App.ViewModels;
using ReactiveUI;

namespace Genie.App.Views;

public partial class ConnectDialog : ReactiveWindow<ConnectDialogViewModel>
{
    public ConnectDialog()
    {
        InitializeComponent();
        this.WhenActivated(d =>
        {
            d(ViewModel!.OkCommand.Subscribe(result => Close(result)));
            d(ViewModel!.CancelCommand.Subscribe(_ => Close(null)));

            // After Fetch completes, auto-open the Character dropdown so
            // the user sees the populated list immediately instead of
            // having to hunt for it. The SGE handshake returns
            // synchronously enough that the visual transition from "(click
            // Fetch)" placeholder → expanded list feels like one motion.
            //
            // Only pop the dropdown when fetch actually produced characters
            // — opening an empty list after a failed fetch would be jarring
            // (the user just saw the "Failed: ..." status text). Posted
            // through the dispatcher so the binding has flushed the
            // ItemsSource before we set IsDropDownOpen.
            d(ViewModel!.FetchCharactersCommand.Subscribe(_ =>
            {
                if (ViewModel!.AvailableCharacters.Count == 0) return;
                Dispatcher.UIThread.Post(() => CharacterCombo.IsDropDownOpen = true);
            }));
        });
    }
}
