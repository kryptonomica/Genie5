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

    private bool _safetyEnabled = true;
    /// <summary>When true, trigger regexes run with a match-timeout + literal
    /// pre-filter — the main guard against a catastrophic user pattern freezing
    /// the read thread. Toggling rebuilds every trigger's regex.</summary>
    public bool SafetyEnabled
    {
        get => _safetyEnabled;
        set { if (_safetyEnabled == value) return; _safetyEnabled = value; foreach (var t in _triggers) t.Rebuild(value); }
    }

    public TriggerRule AddTrigger(string pattern, string action, bool caseSensitive = false,
                                  bool isEnabled = true, string className = "", string soundFile = "")
    {
        var trigger = new TriggerRule(pattern, action, caseSensitive, isEnabled, className, _safetyEnabled, soundFile);
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
            if (trigger.SafeMatch(line) is not { } match) continue;
            // Optional per-trigger SFX (host applies the PlaySounds gate).
            if (!string.IsNullOrEmpty(trigger.SoundFile))
                _host?.PlaySound(trigger.SoundFile);
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
