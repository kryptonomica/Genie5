using Genie.Core.Classes;

namespace Genie.Core.Highlights;

public sealed class HighlightEngine
{
    private readonly List<HighlightRule> _rules = new();
    public IReadOnlyList<HighlightRule> Rules => _rules;
    public ClassEngine? Classes { get; set; }

    public HighlightRule AddRule(string pattern, string foregroundColor, string backgroundColor = "",
                                 HighlightMatchType matchType = HighlightMatchType.String,
                                 bool caseSensitive = false, bool isEnabled = true, string className = "")
    {
        var rule = new HighlightRule(pattern, foregroundColor, backgroundColor, matchType, caseSensitive, isEnabled, className);
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
