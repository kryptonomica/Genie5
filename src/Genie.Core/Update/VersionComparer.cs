namespace Genie.Core.Update;

/// <summary>
/// Tolerant SemVer comparison for version strings that come from GitHub release
/// tags, plugin assembly versions, and feed-config files. Handles:
///
///   - Optional leading <c>v</c> (<c>v1.2.3</c> == <c>1.2.3</c>).
///   - Three- or four-segment numeric versions (<see cref="System.Version"/> style).
///   - Pre-release suffixes (<c>1.2.3-alpha.4</c>): two versions with the same
///     numeric prefix compare by suffix ordinal (no suffix &gt; suffix per
///     SemVer 2.0, e.g. <c>1.0.0</c> &gt; <c>1.0.0-alpha</c>).
///   - Unparseable input: returns 0 (treat as equal) so the caller errs on the
///     side of "no update available" rather than spurious downloads.
///
/// NOT a full SemVer 2.0 implementation — good enough for our update path,
/// where a misclassification means at worst the user runs an extra Check and
/// sees the same version twice.
/// </summary>
public static class VersionComparer
{
    /// <summary>True if <paramref name="latest"/> is strictly newer than <paramref name="installed"/>.</summary>
    public static bool IsNewer(string latest, string installed) =>
        Compare(latest, installed) > 0;

    /// <summary>
    /// Returns &gt; 0 if a is newer than b, &lt; 0 if older, 0 if equal /
    /// unparseable. See class docs for the rules.
    /// </summary>
    public static int Compare(string a, string b)
    {
        var (numA, suffA) = Split(a);
        var (numB, suffB) = Split(b);

        // Numeric prefix wins outright if it differs.
        if (!Version.TryParse(Pad(numA), out var va)) return 0;
        if (!Version.TryParse(Pad(numB), out var vb)) return 0;
        var cmp = va.CompareTo(vb);
        if (cmp != 0) return cmp;

        // Same numeric prefix — pre-release rule: no suffix > any suffix.
        if (string.IsNullOrEmpty(suffA) && string.IsNullOrEmpty(suffB)) return 0;
        if (string.IsNullOrEmpty(suffA)) return  1;
        if (string.IsNullOrEmpty(suffB)) return -1;

        // Both have suffixes — compare ordinally. Not perfect SemVer (alpha.10
        // should beat alpha.2 numerically) but adequate for our needs.
        return string.Compare(suffA, suffB, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Strip leading <c>v</c> / whitespace and split into (numeric, suffix).</summary>
    private static (string Numeric, string Suffix) Split(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("", "");
        var s = raw.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var dash = s.IndexOf('-');
        return dash < 0 ? (s, "") : (s[..dash], s[(dash + 1)..]);
    }

    /// <summary>Pad a numeric version to at least 3 segments so System.Version.TryParse accepts it.</summary>
    private static string Pad(string numeric)
    {
        if (string.IsNullOrEmpty(numeric)) return "0.0.0";
        var parts = numeric.Split('.');
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => numeric,
        };
    }
}
