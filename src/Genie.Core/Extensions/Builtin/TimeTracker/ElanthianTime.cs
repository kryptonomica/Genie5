using System.Text.RegularExpressions;

namespace Genie.Core.Extensions.Builtin;

/// <summary>
/// Canonical Elanthian calendar tables (months, year-cycle, anlaen) and the fixed
/// conversion constants. Authoritative ordering from Elanthipedia, verified against
/// a live <c>time</c> reading.
/// </summary>
internal static class ElanthianCalendar
{
    public const int DaysPerMonth          = 40;
    public const int MonthsPerYear         = 10;
    public const int DaysPerYear           = DaysPerMonth * MonthsPerYear;   // 400
    public const int AnlaenPerDay          = 12;
    public const int RoisaenPerAnlas       = 30;   // an anlas is 30 roisaen …
    public const int RealSecondsPerRoisaen = 60;   // … and a roisaen is one Earth minute
    // 12 anlaen × 30 roisaen × 60 s = 21 600 s = 6 Earth hours = one Elanthian day.

    public static readonly string[] Months =
    {
        "Akroeg the Ram", "Ka'len the Sea Drake", "Lirisa the Archer",
        "Shorka the Cobra", "Uthmor the Giant", "Arhat the Fire Lion",
        "Moliko the Balance", "Skullcleaver the Dwarven Axe",
        "Dolefaren the Brigantine", "Nissa the Maiden",
    };

    public static readonly string[] Years =
    {
        "Silver Unicorn", "Bronze Wyvern", "Golden Panther", "Amber Phoenix",
        "Iron Toad", "Emerald Dolphin", "Crystal Snow Hare",
    };

    public static readonly string[] Anlaen =
    {
        "Anduwen", "Starwatch", "Asketi's Hunt", "Berengaria's Touch",
        "Hodierna's Blessing", "Peri'el's Watch", "Dergati's Bane",
        "Firulf's Flame", "Tamsine's Toil", "Meraud's Cloak",
        "Phelim's Vigil", "Revelfae",
    };

    /// <summary>Short form of a month epithet: "Moliko the Balance" → "Moliko".</summary>
    public static string ShortMonth(string full)
    {
        var i = full.IndexOf(" the ", StringComparison.Ordinal);
        return i < 0 ? full : full[..i];
    }
}

/// <summary>
/// A parsed snapshot of the <c>time</c> verb output, anchored to the wall-clock
/// moment it was read. From this the tracker live-ticks the Elanthian time-of-day
/// (and rolls the date) using only the system clock — no server round-trips.
/// </summary>
internal sealed class ElanthianTimeReading
{
    public DateTimeOffset CapturedAt;

    public int    YearsSinceVictory;
    public int    DayOfYear;
    public int    MonthOrdinal;
    public string MonthName = "";
    public string YearName  = "";
    public string Season    = "";
    public bool   IsNight;
    public int    RoisaenToAnlas;
    public string AnlasTarget = "";
    public string Confidence  = "";

    public bool Valid => MonthOrdinal > 0;

    private static readonly Regex VictoryRe = new(
        @"It has been (\d+) years?,\s*(\d+) days? since the Victory of Lanival",
        RegexOptions.Compiled);
    private static readonly Regex MonthRe = new(
        @"It is the (\d+)(?:st|nd|rd|th) month of (.+?) in the year of the (.+?)\.",
        RegexOptions.Compiled);
    private static readonly Regex SeasonRe = new(
        @"It is currently (\w+) and it is (day|night)\.",
        RegexOptions.Compiled);
    private static readonly Regex AnlasRe = new(
        @"(?:You(?:'re| are| would| think)?\s*(?<conf>[\w' ]+?)\s+)?it's (?:roughly |about |approximately )?(?<rois>\d+) roisae?n before the Anlas of (?<anlas>.+?)\.",
        RegexOptions.Compiled);

    /// <summary>Feed one game-text line; updates whichever field the line carries.
    /// Returns true if the line was a recognised <c>time</c> line.</summary>
    public bool Feed(string line, DateTimeOffset now)
    {
        var m = VictoryRe.Match(line);
        if (m.Success)
        {
            YearsSinceVictory = int.Parse(m.Groups[1].Value);
            DayOfYear         = int.Parse(m.Groups[2].Value);
            CapturedAt        = now;
            return true;
        }

        m = MonthRe.Match(line);
        if (m.Success)
        {
            MonthOrdinal = int.Parse(m.Groups[1].Value);
            MonthName    = m.Groups[2].Value.Trim();
            YearName     = m.Groups[3].Value.Trim();
            CapturedAt   = now;
            return true;
        }

        m = SeasonRe.Match(line);
        if (m.Success)
        {
            Season  = m.Groups[1].Value.Trim();
            IsNight = m.Groups[2].Value == "night";
            return true;
        }

        m = AnlasRe.Match(line);
        if (m.Success)
        {
            Confidence     = m.Groups["conf"].Value.Trim();
            RoisaenToAnlas = int.Parse(m.Groups["rois"].Value);
            AnlasTarget    = m.Groups["anlas"].Value.Trim();
            CapturedAt     = now;
            return true;
        }

        return false;
    }

    /// <summary>The live time-of-day, advanced from the system clock since capture.</summary>
    public (int roisaen, string anlas, int daysElapsed) NowTimeOfDay(DateTimeOffset now)
    {
        var elapsed = (int)Math.Floor((now - CapturedAt).TotalSeconds
                                      / ElanthianCalendar.RealSecondsPerRoisaen);
        if (elapsed < 0) elapsed = 0;

        var idx = Array.IndexOf(ElanthianCalendar.Anlaen, AnlasTarget);
        var remaining = RoisaenToAnlas - elapsed;
        if (idx < 0) return (Math.Max(remaining, 0), AnlasTarget, 0);   // unknown name: don't advance

        var days = 0;
        while (remaining <= 0)
        {
            remaining += ElanthianCalendar.RoisaenPerAnlas;
            idx = (idx + 1) % ElanthianCalendar.AnlaenPerDay;
            if (idx == 0) days++;            // wrapped to Anduwen → a new Elanthian day
        }
        return (remaining, ElanthianCalendar.Anlaen[idx], days);
    }

    /// <summary>The current date, rolled forward by <paramref name="daysElapsed"/>
    /// whole Elanthian days.</summary>
    public (int years, int dayOfYear, int month, string monthName, string yearName)
        NowDate(int daysElapsed)
    {
        var doy   = DayOfYear + daysElapsed;
        var years = YearsSinceVictory + doy / ElanthianCalendar.DaysPerYear;
        doy %= ElanthianCalendar.DaysPerYear;

        var month     = doy / ElanthianCalendar.DaysPerMonth + 1;             // 1–10
        var monthName = ElanthianCalendar.Months[month - 1];

        var yearName = YearName;
        var baseIdx  = Array.IndexOf(ElanthianCalendar.Years, YearName);
        if (baseIdx >= 0)
        {
            var rolled = daysElapsed > 0
                ? (DayOfYear + daysElapsed) / ElanthianCalendar.DaysPerYear
                : 0;
            yearName = ElanthianCalendar.Years[(baseIdx + rolled) % ElanthianCalendar.Years.Length];
        }
        return (years, doy, month, monthName, yearName);
    }
}
