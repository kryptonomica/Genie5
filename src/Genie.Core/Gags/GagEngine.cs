using System.Text.RegularExpressions;
using Genie.Core.Classes;

namespace Genie.Core.Gags;

public sealed class GagRule
{
    private Regex? _regex;
    public GagRule(string pattern, bool caseSensitive = false, bool isEnabled = true, string className = "")
    { Pattern = pattern; CaseSensitive = caseSensitive; IsEnabled = isEnabled; ClassName = className; RebuildRegex(); }
    public string Pattern       { get; }
    public bool   CaseSensitive { get; }
    public bool   IsEnabled     { get; set; }
    public string ClassName     { get; }
    public bool Matches(string line) { if (_regex is null || !IsEnabled) return false; return _regex.IsMatch(line); }
    private void RebuildRegex()
    {
        var opts = RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        try { _regex = new Regex(Pattern, opts); } catch { _regex = null; }
    }
}

public sealed class GagEngine
{
    private readonly List<GagRule> _rules = new();
    public IReadOnlyList<GagRule> Rules => _rules;
    public ClassEngine? Classes { get; set; }

    public GagRule AddRule(string pattern, bool caseSensitive = false, bool isEnabled = true, string className = "")
    {
        var rule = new GagRule(pattern, caseSensitive, isEnabled, className);
        _rules.Add(rule);
        if (!string.IsNullOrEmpty(className)) Classes?.Ensure(className);
        return rule;
    }

    public bool RemoveRule(string pattern) => _rules.RemoveAll(r => r.Pattern == pattern) > 0;
    public void Clear() => _rules.Clear();

    public bool ShouldGag(string line)
    {
        foreach (var rule in _rules)
        {
            if (Classes is not null && !Classes.IsActive(rule.ClassName)) continue;
            if (rule.Matches(line)) return true;
        }
        return false;
    }
}
