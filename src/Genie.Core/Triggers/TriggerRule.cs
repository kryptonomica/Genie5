using System.Text.RegularExpressions;
using Genie.Core.Diagnostics;

namespace Genie.Core.Triggers;

public sealed class TriggerRule
{
    private string? _hint;
    private bool    _safe = true;
    private readonly StringComparison _cmp;

    public TriggerRule(string pattern, string action, bool caseSensitive = false,
                       bool isEnabled = true, string className = "", bool safe = true)
    {
        Pattern       = pattern;
        Action        = action;
        CaseSensitive = caseSensitive;
        IsEnabled     = isEnabled;
        ClassName     = className;
        _cmp          = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        Rebuild(safe);
    }
    public string Pattern       { get; }
    public string Action        { get; }
    public bool   CaseSensitive { get; }
    public bool   IsEnabled     { get; set; }
    public string ClassName     { get; set; }
    public Regex  Regex         { get; private set; } = null!;

    public bool IsMatch(string line) => IsEnabled && SafeMatch(line) is { Success: true };

    /// <summary>
    /// Match with the safety layer: a cheap literal pre-filter before the regex,
    /// and a match-timeout guard that returns null (no match) instead of hanging
    /// the read thread on catastrophic backtracking. Returns the <see cref="Match"/>
    /// so callers can expand <c>$0..$n</c> capture groups.
    /// </summary>
    public Match? SafeMatch(string line)
    {
        if (_safe && _hint is not null && !line.Contains(_hint, _cmp)) return null;
        try { var m = Regex.Match(line); return m.Success ? m : null; }
        catch (RegexMatchTimeoutException) { RegexSafety.ReportTimeout(PipelineStage.Triggers); return null; }
    }

    /// <summary>(Re)build the regex with or without the safety match-timeout.</summary>
    internal void Rebuild(bool safe)
    {
        _safe = safe;
        var opts = RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        Regex = RegexSafety.Build(Pattern, opts, safe);
        _hint = safe ? RegexSafety.LiteralHint(Pattern) : null;
    }
}
