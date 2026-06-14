using System.Text.RegularExpressions;
using Genie.Core.Diagnostics;

namespace Genie.Core.Highlights;

public enum HighlightMatchType { String, Line, BeginsWith, Regex }

public sealed class HighlightRule
{
    private Regex?  _regex;
    private string? _hint;        // literal pre-filter for Regex match type
    private bool    _safe = true;

    public HighlightRule(string pattern, string foregroundColor, string backgroundColor = "",
                         HighlightMatchType matchType = HighlightMatchType.String,
                         bool caseSensitive = false, bool isEnabled = true, string className = "",
                         bool safe = true, string soundFile = "")
    {
        Pattern         = pattern;
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
        MatchType       = matchType;
        CaseSensitive   = caseSensitive;
        IsEnabled       = isEnabled;
        ClassName       = className;
        SoundFile       = soundFile;
        Rebuild(safe);
    }

    public string             Pattern         { get; }
    public string             ForegroundColor { get; }
    public string             BackgroundColor { get; }
    public HighlightMatchType MatchType       { get; }
    public bool               CaseSensitive   { get; }
    public bool               IsEnabled       { get; set; }
    public string             ClassName       { get; set; }
    /// <summary>Optional sound played when this highlight matches a line
    /// (resolved against SoundDir). Empty = silent.</summary>
    public string             SoundFile       { get; set; }

    public bool Matches(string line)
    {
        var cmp = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return MatchType switch
        {
            HighlightMatchType.Regex      => RegexIsMatch(line, cmp),
            HighlightMatchType.BeginsWith => line.StartsWith(Pattern, cmp),
            _                             => line.Contains(Pattern, cmp),
        };
    }

    private bool RegexIsMatch(string line, StringComparison cmp)
    {
        if (_regex is null) return false;
        if (_safe && _hint is not null && !line.Contains(_hint, cmp)) return false;
        try { return _regex.IsMatch(line); }
        catch (RegexMatchTimeoutException) { RegexSafety.ReportTimeout(PipelineStage.Highlights); return false; }
    }

    /// <summary>
    /// Returns the (start, length) of every span in <paramref name="line"/>
    /// this rule highlights. Empty if no match. The renderer uses this to
    /// paint only the matched portion of a line rather than the whole line.
    /// </summary>
    public IEnumerable<(int Start, int Length)> GetMatchPositions(string line)
    {
        if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(Pattern))
            yield break;

        var cmp = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        switch (MatchType)
        {
            case HighlightMatchType.Regex:
                if (_regex is null) yield break;
                if (_safe && _hint is not null && !line.Contains(_hint, cmp)) yield break;
                // Materialise under the timeout guard so a catastrophic pattern
                // can't hang the render path; yield outside the try (C# rule).
                List<(int, int)>? spans = null;
                try
                {
                    foreach (Match m in _regex.Matches(line))
                        if (m.Success && m.Length > 0)
                            (spans ??= new()).Add((m.Index, m.Length));
                }
                catch (RegexMatchTimeoutException) { RegexSafety.ReportTimeout(PipelineStage.Highlights); yield break; }
                if (spans is not null)
                    foreach (var s in spans) yield return s;
                yield break;

            case HighlightMatchType.Line:
                // Whole-line highlight if the line contains the pattern anywhere.
                if (line.Contains(Pattern, cmp))
                    yield return (0, line.Length);
                yield break;

            case HighlightMatchType.BeginsWith:
                if (line.StartsWith(Pattern, cmp))
                    yield return (0, Pattern.Length);
                yield break;

            case HighlightMatchType.String:
            default:
                // All non-overlapping occurrences of the substring.
                int i = 0;
                while (i <= line.Length - Pattern.Length)
                {
                    int hit = line.IndexOf(Pattern, i, cmp);
                    if (hit < 0) yield break;
                    yield return (hit, Pattern.Length);
                    i = hit + Pattern.Length;
                }
                yield break;
        }
    }

    /// <summary>(Re)build the regex (Regex match type only) with or without the
    /// safety match-timeout + literal pre-filter.</summary>
    internal void Rebuild(bool safe)
    {
        _safe = safe;
        if (MatchType != HighlightMatchType.Regex) { _regex = null; _hint = null; return; }
        var opts = RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        try { _regex = RegexSafety.Build(Pattern, opts, safe); _hint = safe ? RegexSafety.LiteralHint(Pattern) : null; }
        catch { _regex = null; _hint = null; }
    }
}
