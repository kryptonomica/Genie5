using System.Text.RegularExpressions;

namespace Genie.Core.Highlights;

public enum HighlightMatchType { String, Line, BeginsWith, Regex }

public sealed class HighlightRule
{
    private Regex? _regex;

    public HighlightRule(string pattern, string foregroundColor, string backgroundColor = "",
                         HighlightMatchType matchType = HighlightMatchType.String,
                         bool caseSensitive = false, bool isEnabled = true, string className = "")
    {
        Pattern         = pattern;
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
        MatchType       = matchType;
        CaseSensitive   = caseSensitive;
        IsEnabled       = isEnabled;
        ClassName       = className;
        RebuildRegex();
    }

    public string             Pattern         { get; }
    public string             ForegroundColor { get; }
    public string             BackgroundColor { get; }
    public HighlightMatchType MatchType       { get; }
    public bool               CaseSensitive   { get; }
    public bool               IsEnabled       { get; set; }
    public string             ClassName       { get; set; }

    public bool Matches(string line)
    {
        var cmp = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return MatchType switch
        {
            HighlightMatchType.Regex      => _regex?.IsMatch(line) ?? false,
            HighlightMatchType.BeginsWith => line.StartsWith(Pattern, cmp),
            _                             => line.Contains(Pattern, cmp),
        };
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
                foreach (Match m in _regex.Matches(line))
                    if (m.Success && m.Length > 0)
                        yield return (m.Index, m.Length);
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

    private void RebuildRegex()
    {
        if (MatchType != HighlightMatchType.Regex) return;
        var opts = RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        _regex = new Regex(Pattern, opts);
    }
}
