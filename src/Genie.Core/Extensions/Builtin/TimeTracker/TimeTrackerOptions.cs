using System.Xml.Linq;

namespace Genie.Core.Extensions.Builtin;

/// <summary>
/// Display options for the Time Tracker, mirroring the Genie 4 <c>Time_Tracker.xml</c>
/// &lt;Options&gt; block. Defaults match the shipped file; a user copy at
/// <c>{Config}/Time_Tracker.xml</c> overrides them at load.
///
/// <para>The Genie 4 file also carried a &lt;Calculations&gt; block of epoch offsets
/// used to compute moon positions client-side. This tracker reads moon state from
/// DR's own <c>obs sky</c>/<c>perceive</c> output instead, so those offsets are
/// intentionally ignored.</para>
/// </summary>
internal sealed class TimeTrackerOptions
{
    public bool ShowElanthiaTime = true;   // show the Elanthian date/time block
    public bool ShowLongNames    = true;   // "Moliko the Balance" vs "Moliko"
    public bool UseGameTime      = true;   // live-tick from the clock vs. show only the last reading
    public bool IncludeAnlasName = true;   // show the "N roisaen before the Anlas of X" line
    public bool IncludeTimeOfDay = true;   // show day/night
    public bool LogGameEvents    = false;  // host.Log() on new readings / day rollover

    public const string FileName = "Time_Tracker.xml";

    /// <summary>Load options: the built-in defaults, then a user override file in
    /// <paramref name="configDir"/> if one exists. Never throws — a malformed file
    /// just keeps the defaults.</summary>
    public static TimeTrackerOptions Load(string configDir)
    {
        var o = new TimeTrackerOptions();
        if (string.IsNullOrWhiteSpace(configDir)) return o;
        var path = Path.Combine(configDir, FileName);
        if (File.Exists(path))
        {
            try { TryApply(o, File.ReadAllText(path)); } catch { /* keep defaults */ }
        }
        return o;
    }

    private static void TryApply(TimeTrackerOptions o, string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return;
        XElement? opts;
        try { opts = XDocument.Parse(xml).Root?.Element("Options"); }
        catch { return; }
        if (opts is null) return;

        o.ShowElanthiaTime = Bool(opts, "ShowElanthiaTime", o.ShowElanthiaTime);
        o.ShowLongNames    = Bool(opts, "ShowLongNames",    o.ShowLongNames);
        o.UseGameTime      = Bool(opts, "UseGameTime",      o.UseGameTime);
        o.IncludeAnlasName = Bool(opts, "IncludeAnlasName", o.IncludeAnlasName);
        o.IncludeTimeOfDay = Bool(opts, "IncludeTimeOfDay", o.IncludeTimeOfDay);
        o.LogGameEvents    = Bool(opts, "LogGameEvents",    o.LogGameEvents);
    }

    private static bool Bool(XElement parent, string name, bool fallback) =>
        bool.TryParse(parent.Element(name)?.Value, out var b) ? b : fallback;
}
