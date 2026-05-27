using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

public class CommandViewModel : ReactiveObject
{
    private readonly Func<string, Task> _send;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;

    [Reactive] public string CommandText { get; set; } = "";

    public ReactiveCommand<Unit, Unit> SubmitCommand { get; }

    public CommandViewModel(Func<string, Task> send)
    {
        _send = send;
        SubmitCommand = ReactiveCommand.CreateFromTask(Submit,
            this.WhenAnyValue(x => x.CommandText, t => !string.IsNullOrWhiteSpace(t)));
    }

    private async Task Submit()
    {
        var cmd = CommandText.Trim();
        if (string.IsNullOrEmpty(cmd)) return;

        _history.Add(cmd);
        _historyIndex = -1;
        CommandText = "";

        await _send(cmd);
    }

    public void HistoryUp()
    {
        if (_history.Count == 0) return;
        if (_historyIndex < 0) _historyIndex = _history.Count;
        if (_historyIndex > 0)
        {
            _historyIndex--;
            CommandText = _history[_historyIndex];
        }
    }

    public void HistoryDown()
    {
        if (_historyIndex < 0) return;
        _historyIndex++;
        CommandText = _historyIndex < _history.Count ? _history[_historyIndex] : "";
        if (_historyIndex >= _history.Count) _historyIndex = -1;
    }
}
