namespace Genie.Core.Runtime;

/// <summary>
/// Tiny helper for Genie 4's "one command per line" .cfg files.
///
/// Genie 4 saves user rules (classes, aliases, variables, triggers,
/// highlights, …) into per-profile .cfg files in <c>ConfigProfileDir</c>.
/// The file format is just a script: each line is the command you would
/// type at the bar to recreate the rule. Loading is "run every line
/// through the command pipeline".
///
/// This helper keeps the I/O surface in one place so every engine doesn't
/// reinvent the same try/catch + file-not-found dance.
/// </summary>
public static class ConfigPersistence
{
    /// <summary>
    /// Write <paramref name="lines"/> to <paramref name="path"/>, creating
    /// the parent directory if it doesn't exist. Returns false on any I/O
    /// error so callers can echo "save failed" without crashing.
    /// </summary>
    public static bool WriteLines(string path, IEnumerable<string> lines)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(path, lines);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Read <paramref name="path"/> as a list of command lines. Blank lines
    /// and lines starting with <c>//</c> or <c>;</c> are filtered out so
    /// hand-edited files can carry comments. Returns null if the file
    /// doesn't exist.
    /// </summary>
    public static IReadOnlyList<string>? ReadLines(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var lines = File.ReadAllLines(path);
            var result = new List<string>(lines.Length);
            foreach (var raw in lines)
            {
                var trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("//") || trimmed.StartsWith(";")) continue;
                result.Add(trimmed);
            }
            return result;
        }
        catch { return null; }
    }

    /// <summary>
    /// Escape a value so it can safely appear inside Genie 4's <c>{…}</c>
    /// argument syntax. Genie 4 itself doesn't escape braces in saved
    /// configs (they're expected not to appear in user values), but we err
    /// on the side of robustness — if the value contains <c>}</c> we
    /// quote-wrap it instead so the round-trip still parses.
    /// </summary>
    public static string FormatArg(string value)
    {
        if (string.IsNullOrEmpty(value)) return "{}";
        if (value.Contains('}') || value.Contains('{'))
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        return "{" + value + "}";
    }
}
