using System.Text;
using System.Text.RegularExpressions;
using Genie.Core.Events;

namespace Genie.Core.Extensions.Builtin;

/// <summary>
/// Built-in Experience tracker (was Plugin_EXPTrackerV5; ports Genie 4's EXPTracker).
/// Reads the live <c>&lt;component id='exp Skill'&gt;… rank pct% mindstate …</c> push in
/// <see cref="OnXml"/> and the <c>exp</c> full-dump lines in <see cref="OnGameLine"/>,
/// keeps a per-skill table (rank, mindstate 0–34), publishes the Genie 4-parity
/// script globals, and re-renders the actively-learning skills to the "Experience"
/// dock panel on each prompt.
///
/// <para>Skill names are accepted dynamically from the stream; the 35 learning-state
/// names are hardcoded (they effectively never change in DR).</para>
/// </summary>
public sealed class ExperienceExtension : IGameExtension
{
    public string Name        => "Experience";
    public string Version     => "2.0";
    public string Description => "Tracks skill ranks and learning rates; $Skill.* / $TDPs globals + a dock panel.";

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value) _host?.SetWindow(WindowName, "(Experience disabled)");
            else        _dirty = true;
        }
    }

    private const string WindowName = "Experience";

    private IExtensionHost _host = null!;
    private bool _dirty;

    private readonly Dictionary<string, SkillInfo> _skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly record struct SkillInfo(int Rank, int Percent, int Mindstate);

    // Guards _skills structural access. Writes (Apply's insert, the empty-clear's
    // Remove) run on the connection read-loop thread; the /exp console command and
    // OnReset read/clear it on the UI thread. Without this, a /exp typed while a
    // skill is pulsing experience can enumerate _skills mid-mutation →
    // "collection was modified".
    private readonly object _gate = new();

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

    private static readonly Regex TagRe    = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex DigitsRe = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex SkillLineRe = new(
        @"([A-Z][A-Za-z '\-]+?):\s+(\d+)\s+(\d+)%\s+([a-z][a-z ]*?)(?=\s*\(|\s{2,}|$)",
        RegexOptions.Compiled);
    private static readonly Regex TdpRe = new(
        @"Time Development Points:\s*(\d+)", RegexOptions.Compiled);

    public void Initialize(IExtensionHost host) => _host = host;
    public void OnCommandSent(string command) { }
    public void Shutdown() { }

    /// <summary>Character switch (clear-then-load connect): drop the accumulated
    /// skill table so the next character's Experience window and <c>$Skill.*</c>
    /// globals start blank instead of inheriting the previous character's ranks and
    /// learning rates. A same-character reconnect does NOT call this.</summary>
    public void OnReset()
    {
        lock (_gate) _skills.Clear();
        _dirty = false;
        _host?.SetWindow(WindowName, Render());
    }

    public void OnGameEvent(GameEvent ev)
    {
        // The live experience push arrives as a parsed ComponentEvent per skill —
        // <component id='exp Attunement'>Attunement: 550 73% dabbling</component> —
        // reliable across the connection's tag-splitting chunk boundaries (raw XML
        // is not). DR also pushes a few non-skill sub-components under the same
        // "exp " prefix (tdp / rexp / favor) which we handle or skip.
        if (ev is not ComponentEvent c
            || !c.ComponentId.StartsWith("exp ", StringComparison.Ordinal))
            return;

        var sub   = c.ComponentId.Substring(4).Trim();   // "Attunement", "tdp", "rexp", …
        var inner = TagRe.Replace(c.Content ?? "", "").Trim();

        if (sub.Equals("tdp", StringComparison.OrdinalIgnoreCase))
        {
            var m = DigitsRe.Match(inner);               // "TDPs:  3017"
            if (m.Success) _host.Globals["TDPs"] = m.Value;
            return;
        }
        if (sub.Equals("rexp",  StringComparison.OrdinalIgnoreCase) ||
            sub.Equals("favor", StringComparison.OrdinalIgnoreCase) ||
            sub.Equals("mxp",   StringComparison.OrdinalIgnoreCase))
            return;                                       // not skills — ignore

        if (inner.Length == 0)                            // empty = skill pulsed to clear
        {
            lock (_gate) { if (_skills.Remove(sub)) _dirty = true; }
            _host.Globals[$"{Var(sub)}.LearningRate"] = "0";
            return;
        }
        ApplyLine(inner);
    }

    public void OnGameLine(string line)
    {
        // The `exp`/`experience` full dump arrives as plain text (two skills per
        // line). The skill regex is specific enough to be safe across streams.
        if (line.IndexOf('%') >= 0 && line.IndexOf(':') >= 0)
            foreach (Match m in SkillLineRe.Matches(line))
                Apply(m.Groups[1].Value.Trim(), m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);

        var tdp = TdpRe.Match(line);
        if (tdp.Success) _host.Globals["TDPs"] = tdp.Groups[1].Value;
    }

    public void OnPrompt()
    {
        if (!_dirty) return;
        _dirty = false;
        _host.SetWindow(WindowName, Render());
    }

    public bool OnSlashCommand(string input)
    {
        var t = input.Trim();
        if (!t.StartsWith("/experience", StringComparison.OrdinalIgnoreCase) &&
            !t.Equals("/exp", StringComparison.OrdinalIgnoreCase))
            return false;
        _host.SetWindow(WindowName, Render());
        _host.Echo("[Experience] window updated (Window → Experience to show it).");
        return true;
    }

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

        var v = Var(name);
        _host.Globals[$"{v}.Ranks"]            = rank.ToString();
        _host.Globals[$"{v}.LearningRate"]     = mind.ToString();
        _host.Globals[$"{v}.LearningRateName"] = mindstateText;

        var info = new SkillInfo(rank, pct, mind);
        lock (_gate)
        {
            if (_skills.TryGetValue(name, out var prev) && prev == info) return;  // no display change
            _skills[name] = info;
        }
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
        List<KeyValuePair<string, SkillInfo>> learning;
        lock (_gate)
            learning = _skills
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
