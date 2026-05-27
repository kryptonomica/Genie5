namespace Genie.Core.Parsing;

/// <summary>
/// Tokenises a Genie 4 command line into argv-style parts.
///
/// Recognises three token shapes:
/// <list type="bullet">
/// <item><c>bare</c> — whitespace-delimited.</item>
/// <item><c>"quoted"</c> — double quotes group spaces; outer quotes are stripped.</item>
/// <item><c>{braced}</c> — Genie 4's canonical grouping; balanced nesting allowed
///   (<c>{outer {inner} more}</c> is one token whose value is
///   <c>outer {inner} more</c>); only the outermost pair is stripped.</item>
/// </list>
/// Brace grouping is what scripts and saved <c>*.cfg</c> files rely on, so it
/// must survive a save-then-load round-trip without splitting on the spaces
/// inside the braces.
/// </summary>
public static class ArgumentParser
{
    public static IReadOnlyList<string> ParseArgs(string text)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        var current   = new System.Text.StringBuilder();
        var inQuotes  = false;
        var braceDepth = 0;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            // Inside a quoted run, only " and { tracking matter (and {…} are
            // literal characters that don't open groups inside quotes).
            if (inQuotes)
            {
                if (ch == '"') { inQuotes = false; continue; }
                current.Append(ch);
                continue;
            }

            // Inside a braced group, only nesting matters; spaces stay
            // intact and quotes are literal.
            if (braceDepth > 0)
            {
                if (ch == '{') { braceDepth++; current.Append(ch); continue; }
                if (ch == '}')
                {
                    braceDepth--;
                    if (braceDepth == 0) continue;   // strip outermost
                    current.Append(ch);
                    continue;
                }
                current.Append(ch);
                continue;
            }

            // Outside any group: handle openers, separators, and literals.
            if (ch == '"') { inQuotes = true; continue; }
            if (ch == '{') { braceDepth = 1; continue; }   // strip outermost
            if (ch == ' ' || ch == '\t')
            {
                if (current.Length > 0) { results.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(ch);
        }

        if (current.Length > 0) results.Add(current.ToString());
        return results;
    }
}
