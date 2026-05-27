using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Genie.Core;
using Genie.Core.Events;
using ReactiveUI;

namespace Genie.App.ViewModels;

public class StreamTabsViewModel : ReactiveObject
{
    public StreamBuffer Logons   { get; } = new("Logons");
    public StreamBuffer Talk     { get; } = new("Talk");
    public StreamBuffer Whispers { get; } = new("Whispers");
    public StreamBuffer Thoughts { get; } = new("Thoughts");
    public StreamBuffer Combat   { get; } = new("Combat");

    public IReadOnlyList<StreamBuffer> All => [Logons, Talk, Whispers, Thoughts, Combat];

    public void Attach(GenieCore core)
    {
        core.GameEvents.OfType<TextEvent>()
            .Where(e => e.Stream != "main")
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e =>
            {
                var buf = e.Stream switch
                {
                    "logons"   => Logons,
                    "talk"     => Talk,
                    "whispers" => Whispers,
                    "thoughts" => Thoughts,
                    "combat"   => Combat,
                    _          => null
                };
                buf?.Add(e.Text);
            });
    }
}

public class StreamBuffer(string name) : ReactiveObject
{
    private const int Max = 500;

    public string Name { get; } = name;

    /// <summary>
    /// Lines as <see cref="TextLine"/> records so the template can use the
    /// same <c>InlinesBehavior</c> + <c>DefaultHighlights.Tokenize</c> pipeline
    /// the main game window uses — user-defined highlights apply to side
    /// streams (logons, talk, whispers, thoughts, combat) as well as main.
    /// </summary>
    public ObservableCollection<TextLine> Lines { get; } = [];

    public void Add(string line)
    {
        Lines.Add(new TextLine(line, StreamColor.Main));
        while (Lines.Count > Max)
            Lines.RemoveAt(0);
    }
}
