using Genie.Core.Classes;

namespace Genie.Core.Highlights;

public sealed class HighlightEngine
{
    private readonly List<HighlightRule> _rules = new();
    public IReadOnlyList<HighlightRule> Rules => _rules;
    public ClassEngine? Classes { get; set; }

    private bool _safetyEnabled = true;
    /// <summary>When true, regex-type highlight rules run with a match-timeout +
    /// literal pre-filter. Toggling rebuilds every rule.</summary>
    public bool SafetyEnabled
    {
        get => _safetyEnabled;
        set { if (_safetyEnabled == value) return; _safetyEnabled = value; foreach (var r in _rules) r.Rebuild(value); }
    }

    public HighlightRule AddRule(string pattern, string foregroundColor, string backgroundColor = "",
                                 HighlightMatchType matchType = HighlightMatchType.String,
                                 bool caseSensitive = false, bool isEnabled = true, string className = "",
                                 string soundFile = "")
    {
        var rule = new HighlightRule(pattern, foregroundColor, backgroundColor, matchType, caseSensitive, isEnabled, className, _safetyEnabled, soundFile);
        _rules.Add(rule);
        if (!string.IsNullOrEmpty(className)) Classes?.Ensure(className);
        return rule;
    }

    public bool RemoveRule(string pattern) => _rules.RemoveAll(r => r.Pattern == pattern) > 0;
    public void Clear() => _rules.Clear();

    public HighlightRule? Match(string plainText)
    {
        foreach (var rule in _rules)
            if (rule.IsEnabled && (Classes?.IsActive(rule.ClassName) ?? true) && rule.Matches(plainText))
                return rule;
        return null;
    }
}
