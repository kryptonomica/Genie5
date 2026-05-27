using System.Text.RegularExpressions;
using Genie.Core.Classes;

namespace Genie.Core.Substitutes;

public sealed class SubstituteRule
{
    private Regex? _regex;
    public SubstituteRule(string pattern, string replacement, bool caseSensitive = false, bool isEnabled = true, string className = "")
    { Pattern = pattern; Replacement = replacement; CaseSensitive = caseSensitive; IsEnabled = isEnabled; ClassName = className; RebuildRegex(); }
    public string Pattern       { get; }
    public string Replacement   { get; }
    public bool   CaseSensitive { get; }
    public bool   IsEnabled     { get; set; }
    public string ClassName     { get; }
    public string Apply(string line) { if (_regex is null || !IsEnabled) return line; return _regex.Replace(line, Replacement); }
    private void RebuildRegex()
    {
        var opts = RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        try { _regex = new Regex(Pattern, opts); } catch { _regex = null; }
    }
}

public sealed class SubstituteEngine
{
    private readonly List<SubstituteRule> _rules = new();
    public IReadOnlyList<SubstituteRule> Rules => _rules;
    public ClassEngine? Classes { get; set; }

    public SubstituteRule AddRule(string pattern, string replacement, bool caseSensitive = false, bool isEnabled = true, string className = "")
    {
        var rule = new SubstituteRule(pattern, replacement, caseSensitive, isEnabled, className);
        _rules.Add(rule);
        if (!string.IsNullOrEmpty(className)) Classes?.Ensure(className);
        return rule;
    }

    public bool RemoveRule(string pattern) => _rules.RemoveAll(r => r.Pattern == pattern) > 0;
    public void Clear() => _rules.Clear();

    public string Apply(string line)
    {
        foreach (var rule in _rules)
        {
            if (Classes is not null && !Classes.IsActive(rule.ClassName)) continue;
            line = rule.Apply(line);
        }
        return line;
    }
}
