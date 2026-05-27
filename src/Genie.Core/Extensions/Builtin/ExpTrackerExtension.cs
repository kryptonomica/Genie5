using System.Globalization;
using System.Text.RegularExpressions;

namespace Genie.Core.Extensions.Builtin;

public sealed class ExpTrackerExtension : IGameExtension
{
    public string Name        => "EXPTracker";
    public string Version     => "1.0";
    public string Description => "Tracks DragonRealms experience output and exposes per-skill globals.";
    public bool   Enabled     { get; set; } = true;

    private IExtensionHost _host = null!;

    private static readonly string[] MindStates =
    {
        "clear","dabbling","perusing","learning","thoughtful","thinking","considering",
        "pondering","ruminating","concentrating","attentive","deliberative","interested",
        "examining","understanding","absorbing","studious","focused","very focused",
        "engaged","very engaged","cogitating","fascinated","captivated","engrossed",
        "riveted","very riveted","rapt","very rapt","enthralled","nearly locked",
        "mind lock","mind lock","mind lock","mind lock",
    };

    private static readonly Regex SkillRegex = new(
        @"(?<name>[A-Z][A-Za-z][A-Za-z _'-]*?)\s*:\s*(?<ranks>\d+)\s+(?<pct>\d{1,3})%\s+(?<state>[a-z][a-z ]*?)(?=\s{2,}|\s*\(\s*\d+\s*/\s*34\s*\)|$)",
        RegexOptions.Compiled);

    private static readonly Regex TdpRegex = new(
        @"\b(?<n>\d+)\s+(?:Trait\s+Points|TDPs?|TPs?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public void Initialize(IExtensionHost host) { _host = host; }
    public void OnCommandSent(string command) { }
    public void OnPrompt()                    { }
    public void Shutdown()                    { }

    public void OnGameLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (line.IndexOf(':') >= 0 && line.IndexOf('%') >= 0)
        {
            foreach (Match m in SkillRegex.Matches(line))
            {
                var rawName = m.Groups["name"].Value.Trim();
                if (rawName.Length < 3 || LooksLikeNoiseField(rawName)) continue;
                var name      = rawName.Replace(' ', '_');
                var ranks     = m.Groups["ranks"].Value;
                var stateText = m.Groups["state"].Value.Trim();
                _host.Globals[name + ".Ranks"]            = ranks;
                _host.Globals[name + ".LearningRateName"] = stateText;
                _host.Globals[name + ".LearningRate"]     = LookupMindStateValue(stateText).ToString(CultureInfo.InvariantCulture);
            }
        }
        var tdp = TdpRegex.Match(line);
        if (tdp.Success && (line.IndexOf("Trait", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("TDP", StringComparison.OrdinalIgnoreCase) >= 0))
            _host.Globals["TDPs"] = tdp.Groups["n"].Value;
        if (line.Contains("You go to sleep", StringComparison.OrdinalIgnoreCase) || line.Contains("You are sleeping", StringComparison.OrdinalIgnoreCase))
            _host.Globals["Sleeping"] = "y";
        else if (line.Contains("You wake up", StringComparison.OrdinalIgnoreCase) || line.Contains("You are no longer sleeping", StringComparison.OrdinalIgnoreCase))
            _host.Globals["Sleeping"] = "n";
        if (line.Contains("You begin to chant", StringComparison.OrdinalIgnoreCase) || line.Contains("you start preparing", StringComparison.OrdinalIgnoreCase))
            _host.Globals["Concentrating"] = "y";
        else if (line.Contains("you let your concentration lapse", StringComparison.OrdinalIgnoreCase) || line.Contains("you stop chanting", StringComparison.OrdinalIgnoreCase))
            _host.Globals["Concentrating"] = "n";
    }

    private static bool LooksLikeNoiseField(string name) => name switch
    {
        "Time" or "Roundtime" or "RT" or "Health" or "Mana" or "Stamina" or "Spirit" or "Concentration" => true,
        _ => false,
    };

    private static int LookupMindStateValue(string state)
    {
        if (string.IsNullOrEmpty(state)) return -1;
        for (int i = 0; i < MindStates.Length; i++)
            if (MindStates[i].Equals(state, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
