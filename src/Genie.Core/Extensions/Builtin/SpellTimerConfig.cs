using Genie.Core.Parsing;

namespace Genie.Core.Extensions.Builtin;

/// <summary>How the duration field of an Active-Spells line is interpreted. Most
/// spells report "N roisaen" (<see cref="Roisaen"/>); a handful report a percentage
/// or a charge instead, and Moonblade reports a count phrase.</summary>
public enum SpellParseRule
{
    /// <summary>"N roisaen" remaining — the default for every spell.</summary>
    Roisaen,
    /// <summary>"N%" charge level, stored as <c>$SpellTimer.&lt;spell&gt;.charge</c>
    /// (e.g. Stellar Collector).</summary>
    Charge,
    /// <summary>"N%" stored as the duration (e.g. Osrel Meraud).</summary>
    Percent,
    /// <summary>"Many/One/No small orbiting slivers of lunar magic" → 2/1/0
    /// (Moonblade Slivers).</summary>
    Slivers,
}

/// <summary>
/// User-editable parse rules + variable-name overrides for the built-in Spell
/// Timer. Spell <b>names</b> are auto-discovered from DR's <c>percWindow</c>
/// stream, so a brand-new spell needs no config — it tracks automatically. This
/// file is only for the exceptions:
/// <list type="bullet">
/// <item>spells whose duration field isn't "N roisaen" (charge / percent / a count
///   phrase) — so DR adding another charge-style spell needs no recompile;</item>
/// <item>overriding the <c>$SpellTimer.&lt;token&gt;.*</c> variable token a spell
///   publishes under.</item>
/// </list>
///
/// <para>File: <c>{Config}/spelltimer.cfg</c>, brace-delimited lines matching the
/// rest of Genie's <c>.cfg</c> format:</para>
/// <code>
/// rule {Stellar Collector} {charge}
/// rule {Osrel Meraud} {percent}
/// var  {Clear Vision} {ClearVision}
/// </code>
/// </summary>
public sealed class SpellTimerConfig
{
    public const string FileName = "spelltimer.cfg";

    // The three special cases the Genie 4 plugin hardcoded ship as defaults; the
    // file can add to or override them. Keyed case-sensitively on the display name
    // exactly as DR sends it.
    private readonly Dictionary<string, SpellParseRule> _rules = new(StringComparer.Ordinal)
    {
        ["Stellar Collector"] = SpellParseRule.Charge,
        ["Osrel Meraud"]      = SpellParseRule.Percent,
        ["Moonblade Slivers"] = SpellParseRule.Slivers,
    };

    private readonly Dictionary<string, string> _varOverrides = new(StringComparer.Ordinal);

    /// <summary>Parse rule for a spell — <see cref="SpellParseRule.Roisaen"/> unless
    /// a built-in default or the config file says otherwise.</summary>
    public SpellParseRule RuleFor(string spellName)
        => _rules.TryGetValue(spellName, out var r) ? r : SpellParseRule.Roisaen;

    /// <summary>The <c>$SpellTimer.&lt;token&gt;</c> token for a spell: a config
    /// override if present, else the Genie 4 convention (strip spaces, apostrophes,
    /// hyphens) so existing scripts keep working.</summary>
    public string VarToken(string spellName)
        => _varOverrides.TryGetValue(spellName, out var v) && v.Length > 0
            ? v
            : spellName.Replace(" ", "").Replace("'", "").Replace("-", "");

    /// <summary>Load <c>spelltimer.cfg</c> from the given config dir, layering its
    /// rules/overrides on top of the built-in defaults. Missing file or malformed
    /// lines are ignored — the defaults always stand. Writes a commented starter
    /// file on first run so users have a template to edit.</summary>
    public void Load(string configDir)
    {
        if (string.IsNullOrWhiteSpace(configDir)) return;
        var path = Path.Combine(configDir, FileName);
        try
        {
            if (!File.Exists(path)) { WriteStarter(path); return; }
            foreach (var line in File.ReadLines(path))
            {
                var parts = ArgumentParser.ParseArgs(line);
                if (parts.Count != 3) continue;
                var verb = parts[0].ToLowerInvariant();
                var name = parts[1].Trim();
                var val  = parts[2].Trim();
                if (name.Length == 0) continue;

                if (verb == "rule" && Enum.TryParse<SpellParseRule>(val, ignoreCase: true, out var rule))
                    _rules[name] = rule;
                else if (verb == "var")
                    _varOverrides[name] = val;
            }
        }
        catch { /* best-effort — defaults stand */ }
    }

    private static void WriteStarter(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, new[]
            {
                "# spelltimer.cfg — Spell Timer parse rules and variable-name overrides.",
                "#",
                "# Spell NAMES are auto-discovered from DR's Active Spells window, so a new",
                "# spell tracks automatically with no entry here. Use this file only for:",
                "#",
                "#   rule {Spell Name} {roisaen|charge|percent|slivers}",
                "#       how the duration field is read. 'roisaen' is the default for every",
                "#       spell; 'charge'/'percent' read an 'N%' field; 'slivers' reads the",
                "#       Moonblade count phrase.",
                "#",
                "#   var  {Spell Name} {Token}",
                "#       override the $SpellTimer.<Token>.* variable name a spell uses.",
                "#",
                "# Built-in defaults (uncomment to change):",
                "# rule {Stellar Collector} {charge}",
                "# rule {Osrel Meraud} {percent}",
                "# rule {Moonblade Slivers} {slivers}",
            });
        }
        catch { /* best-effort */ }
    }
}
