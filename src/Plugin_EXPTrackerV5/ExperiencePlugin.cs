using System.Text;
using System.Text.RegularExpressions;
using Genie.Plugins;

namespace Genie.Plugins.Experience;

/// <summary>
/// The Experience tracker — the first standalone Genie 5 plugin and the proving
/// ground for the plugin API. Ports Genie 4's EXPTracker behaviour onto the
/// plugin contract:
///
/// <list type="bullet">
/// <item>reads the live <c>&lt;component id='exp Skill'&gt;… rank pct% mindstate …</c>
/// push in <see cref="OnXml"/>, and the <c>exp</c> full-dump lines in
/// <see cref="OnGameText"/>;</item>
/// <item>keeps a per-skill table (rank, mindstate 0–34);</item>
/// <item>re-renders the actively-learning skills to the "Experience" window via
/// <see cref="IPluginHost.SetWindow"/> on each prompt.</item>
/// </list>
///
/// Output is via the named-window seam, so this plugin has no UI/Avalonia
/// dependency — the host surfaces "Experience" as a dock panel. It references
/// only <c>Genie.Plugins.Abstractions</c> and loads as a DLL from the Plugins
/// folder.
/// </summary>
public sealed class ExperiencePlugin : IGeniePlugin
{
    public string Id             => "genie.experience";
    public string Name           => "Experience";
    public string Version        => "1.0";
    public string Author         => "Genie 5";
    public string Description     => "Tracks skill ranks and learning rates, à la the StormFront experience window.";
    public string MinHostVersion => "5.0.0";

    private bool _enabled = true;
    /// <summary>When toggled off, blank the window so a disabled plugin doesn't
    /// leave stale data on screen; when re-enabled, mark dirty so it repaints on
    /// the next prompt. (Window visibility itself is separate — Window →
    /// Experience.)</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value) _host?.SetWindow("Experience", "(Experience plugin disabled)");
            else        _dirty = true;
        }
    }

    private IPluginHost _host = null!;
    private bool _dirty;

    // skill name → (rank, percent, mindstate 0–34)
    private readonly Dictionary<string, SkillInfo> _skills = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct SkillInfo(int Rank, int Percent, int Mindstate);

    /// <summary>Canonical 35 DR learning states (0–34), authoritative order from
    /// Genie 4's EXPTracker.</summary>
    private static readonly string[] MindStates =
    {
        "clear", "dabbling", "perusing", "learning", "thoughtful", "thinking",
        "considering", "pondering", "ruminating", "concentrating", "attentive",
        "deliberative", "interested", "examining", "understanding", "absorbing",
        "intrigued", "scrutinizing", "analyzing", "studious", "focused",
        "very focused", "engaged", "very engaged", "cogitating", "fascinated",
        "captivated", "engrossed", "riveted", "very riveted", "rapt",
        "very rapt", "enthralled", "nearly locked", "mind lock",
    };

    // <component id='exp Small Edged'> … inner text … </component>
    private static readonly Regex ComponentRe = new(
        @"<component id='exp ([^']+)'>(.*?)</component>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Strip any nested tags (e.g. <b>, <preset id='whisper'>) from the inner text.
    private static readonly Regex TagRe = new("<[^>]*>", RegexOptions.Compiled);

    // "Small Edged:  142 71% examining"  (tolerates a trailing "(13/34)")
    private static readonly Regex SkillLineRe = new(
        @"([A-Z][A-Za-z '\-]+?):\s+(\d+)\s+(\d+)%\s+([a-z][a-z ]*?)(?=\s*\(|\s{2,}|$)",
        RegexOptions.Compiled);

    // "Time Development Points: 3022"
    private static readonly Regex TdpRe = new(
        @"Time Development Points:\s*(\d+)", RegexOptions.Compiled);

    public void Initialize(IPluginHost host) => _host = host;

    public void Shutdown() { }

    public void OnXml(string xml)
    {
        if (xml.IndexOf("<component id='exp ", StringComparison.Ordinal) < 0) return;

        foreach (Match m in ComponentRe.Matches(xml))
        {
            var name  = m.Groups[1].Value.Trim();
            var inner = TagRe.Replace(m.Groups[2].Value, "").Trim();

            if (inner.Length == 0)        // empty component = skill pulsed to clear
            {
                if (_skills.Remove(name)) _dirty = true;
                _host.SetVariable($"{Var(name)}.LearningRate", "0");   // scripts: skill cleared
                continue;
            }
            ApplyLine(inner);
        }
    }

    public string? OnGameText(string text, string stream)
    {
        // The `exp`/`experience` full dump arrives as plain text (two skills per
        // line). Parse every skill occurrence; observe-only (return unchanged).
        if (stream == "main" && text.IndexOf('%') >= 0 && text.IndexOf(':') >= 0)
            foreach (Match m in SkillLineRe.Matches(text))
                Apply(m.Groups[1].Value.Trim(),
                      m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);

        var tdp = TdpRe.Match(text);
        if (tdp.Success) _host.SetVariable("TDPs", tdp.Groups[1].Value);
        return text;
    }

    public void OnPrompt()
    {
        if (!_dirty) return;
        _dirty = false;
        _host.SetWindow("Experience", Render());
    }

    public string? OnInput(string input)          => input;
    public void    OnCommandSent(string command)  { }
    public void    OnVariableChanged(string n, string v) { }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void ApplyLine(string line)
    {
        var m = SkillLineRe.Match(line);
        if (m.Success)
            Apply(m.Groups[1].Value.Trim(), m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
    }

    private void Apply(string name, string rankText, string pctText, string mindstateText)
    {
        if (!int.TryParse(rankText, out var rank) || !int.TryParse(pctText, out var pct)) return;
        mindstateText = mindstateText.Trim();
        var mind = MindstateValue(mindstateText);

        // Publish the script globals scripts read ($Small_Edged.Ranks etc.).
        // The plugin is now the single source of these (replaces the old
        // ExpTrackerExtension).
        var v = Var(name);
        _host.SetVariable($"{v}.Ranks",            rank.ToString());
        _host.SetVariable($"{v}.LearningRate",     mind.ToString());
        _host.SetVariable($"{v}.LearningRateName", mindstateText);

        var info = new SkillInfo(rank, pct, mind);
        if (_skills.TryGetValue(name, out var prev) && prev == info) return;  // no display change
        _skills[name] = info;
        _dirty = true;
    }

    /// <summary>Skill name → global-variable token (spaces → underscores), e.g.
    /// "Small Edged" → "Small_Edged", matching Genie 4's $Skill.* convention.</summary>
    private static string Var(string name) => name.Replace(' ', '_');

    private static int MindstateValue(string state)
    {
        for (int i = 0; i < MindStates.Length; i++)
            if (MindStates[i].Equals(state, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    private string Render()
    {
        // Actively-learning skills (mindstate > 0), highest mindstate first then
        // by name — the "what's filling" view.
        var learning = _skills
            .Where(kv => kv.Value.Mindstate > 0)
            .OrderByDescending(kv => kv.Value.Mindstate)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("Learning: ").Append(learning.Count).Append('\n');
        sb.Append("──────────────────────────────────────\n");
        foreach (var (name, info) in learning)
            sb.AppendLine(
                $"{name,-18} {info.Rank,3} {info.Percent,2}%  {MindStates[info.Mindstate]} ({info.Mindstate}/34)");

        if (learning.Count == 0)
            sb.Append("(nothing learning — train a skill, or type 'exp')");

        return sb.ToString().TrimEnd();
    }
}
