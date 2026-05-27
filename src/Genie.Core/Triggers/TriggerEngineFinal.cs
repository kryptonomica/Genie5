using System.Text.RegularExpressions;
using Genie.Core.Classes;
using Genie.Core.Commanding;

namespace Genie.Core.Triggers;

public sealed class TriggerEngineFinal
{
    private readonly List<TriggerRule> _triggers = new();
    private readonly ICommandHost?     _host;
    private readonly CommandEngine?    _commandEngine;

    /// <summary>
    /// Construct with optional host + command engine. Both are only used when
    /// triggers fire (<see cref="ProcessLine"/>), so offline / draft instances
    /// for the Configuration dialog can pass nulls.
    /// </summary>
    public TriggerEngineFinal(ICommandHost? host = null, CommandEngine? commandEngine = null)
    { _host = host; _commandEngine = commandEngine; }

    public IReadOnlyList<TriggerRule> Triggers => _triggers;
    public ClassEngine? Classes { get; set; }

    public TriggerRule AddTrigger(string pattern, string action, bool caseSensitive = false,
                                  bool isEnabled = true, string className = "")
    {
        var trigger = new TriggerRule(pattern, action, caseSensitive, isEnabled, className);
        _triggers.Add(trigger);
        if (!string.IsNullOrEmpty(className)) Classes?.Ensure(className);
        return trigger;
    }

    public bool RemoveTrigger(string pattern) => _triggers.RemoveAll(t => t.Pattern == pattern) > 0;
    public void Clear() => _triggers.Clear();

    public bool SetEnabled(string pattern, bool isEnabled)
    {
        var t = _triggers.FirstOrDefault(t => t.Pattern == pattern);
        if (t is null) return false;
        t.IsEnabled = isEnabled;
        return true;
    }

    public void ProcessLine(string line, bool echoTriggerDebug = true)
    {
        foreach (var trigger in _triggers)
        {
            if (!trigger.IsEnabled) continue;
            if (Classes is not null && !Classes.IsActive(trigger.ClassName)) continue;
            var match = trigger.Regex.Match(line);
            if (!match.Success) continue;
            var expandedAction = ExpandAction(trigger.Action, match);
            _commandEngine?.ProcessInput(expandedAction);
        }
    }

    private static string ExpandAction(string action, Match match)
    {
        var result = action;
        for (var i = match.Groups.Count - 1; i >= 0; i--)
            result = result.Replace("$" + i, match.Groups[i].Value);
        return result;
    }
}
