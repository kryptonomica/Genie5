using System.Text.RegularExpressions;

namespace Genie.Core.Scripting.Js;

/// <summary>
/// The CLR object handed to every <c>.js</c> script as <c>genie</c> (aliased
/// <c>game</c>). This is the clean Genie 5 scripting surface — no Genie 4 legacy
/// shapes. A thin JS prelude (in <see cref="JsScriptRuntime"/>) wraps these
/// PascalCase methods as lowercase <c>genie.*</c> functions so scripts read
/// naturally.
///
/// <para>Every method runs on the owning script's thread. The blocking ones
/// (<see cref="WaitFor"/>, <see cref="WaitForRe"/>, <see cref="WaitForPrompt"/>,
/// <see cref="Pause"/>) park that thread; <see cref="JsScriptInstance.Checkpoint"/>
/// is called first on each so pause/stop take effect promptly.</para>
/// </summary>
internal sealed class JsHostApi
{
    private readonly JsScriptRuntime  _runtime;
    private readonly JsScriptInstance _inst;

    public JsHostApi(JsScriptRuntime runtime, JsScriptInstance inst)
    {
        _runtime = runtime;
        _inst    = inst;
    }

    /// <summary>Send a command to the game (same path as .cmd <c>put</c>).</summary>
    public void Put(string command)
    {
        _inst.Checkpoint();
        if (!string.IsNullOrEmpty(command)) _runtime.Send(command);
    }

    /// <summary>Echo text to the game window / script output.</summary>
    public void Echo(string text)
    {
        _inst.Checkpoint();
        _runtime.Echo(text ?? "");
    }

    /// <summary>Block until a game line contains <paramref name="text"/>
    /// (case-insensitive). Returns the matched line, or "" on timeout.
    /// <paramref name="timeoutSeconds"/> &lt;= 0 waits forever.</summary>
    public string WaitFor(string text, double timeoutSeconds)
    {
        _inst.Checkpoint();
        var needle = text ?? "";
        var line = _inst.WaitForLine(
            l => l.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0,
            ToTimeout(timeoutSeconds));
        return line ?? "";
    }

    /// <summary>Block until a game line matches the regex
    /// <paramref name="pattern"/>. Returns the capture groups (index 0 is the
    /// whole match), or an empty array on timeout.</summary>
    public string[] WaitForRe(string pattern, double timeoutSeconds)
    {
        _inst.Checkpoint();
        var rx = _runtime.GetRegex(pattern ?? "");
        Match? captured = null;
        var line = _inst.WaitForLine(
            l => { var m = rx.Match(l); if (m.Success) { captured = m; return true; } return false; },
            ToTimeout(timeoutSeconds));

        if (line is null || captured is null) return Array.Empty<string>();
        var groups = new string[captured.Groups.Count];
        for (int i = 0; i < groups.Length; i++) groups[i] = captured.Groups[i].Value;
        return groups;
    }

    /// <summary>Block until a game line contains any of <paramref name="joinedPatterns"/>
    /// (newline-separated, case-insensitive substrings). Returns the pattern that
    /// matched (so the script can branch on it), or "" on timeout. The prelude
    /// joins a JS array of patterns into the newline-delimited form.</summary>
    public string MatchWait(string joinedPatterns, double timeoutSeconds)
    {
        _inst.Checkpoint();
        var patterns = SplitPatterns(joinedPatterns);
        if (patterns.Length == 0) return "";

        string? hit = null;
        var line = _inst.WaitForLine(l =>
        {
            foreach (var p in patterns)
                if (l.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) { hit = p; return true; }
            return false;
        }, ToTimeout(timeoutSeconds));

        return line is null ? "" : (hit ?? "");
    }

    /// <summary>Regex form of <see cref="MatchWait"/>: each pattern is treated as a
    /// (case-insensitive) regex. Returns the pattern string that matched, or "" on
    /// timeout. For capture groups, re-run the winning pattern through
    /// <see cref="WaitForRe"/>.</summary>
    public string MatchWaitRe(string joinedPatterns, double timeoutSeconds)
    {
        _inst.Checkpoint();
        var patterns = SplitPatterns(joinedPatterns);
        if (patterns.Length == 0) return "";

        var rxs = new Regex[patterns.Length];
        for (int i = 0; i < patterns.Length; i++) rxs[i] = _runtime.GetRegex(patterns[i]);

        string? hit = null;
        var line = _inst.WaitForLine(l =>
        {
            for (int i = 0; i < rxs.Length; i++)
                if (rxs[i].IsMatch(l)) { hit = patterns[i]; return true; }
            return false;
        }, ToTimeout(timeoutSeconds));

        return line is null ? "" : (hit ?? "");
    }

    /// <summary>Directed echo: route <paramref name="text"/> to a named window /
    /// colour (same seam as the .cmd <c>#echo &gt;Window #Color</c>). Null/empty
    /// window or colour fall back to the main output / default colour.</summary>
    public void EchoTo(string text, string? window, string? color)
    {
        _inst.Checkpoint();
        _runtime.EchoTo(
            text ?? "",
            string.IsNullOrEmpty(window) ? null : window,
            string.IsNullOrEmpty(color)  ? null : color);
    }

    /// <summary>Start (or restart) this script's stopwatch.</summary>
    public void TimerStart()   { _inst.Checkpoint(); _inst.TimerStart(); }

    /// <summary>Stop and clear this script's stopwatch.</summary>
    public void TimerStop()    { _inst.Checkpoint(); _inst.TimerStop(); }

    /// <summary>Seconds elapsed since <c>timerStart</c> (0 if not running).</summary>
    public double TimerElapsed() { _inst.Checkpoint(); return _inst.TimerElapsed(); }

    /// <summary>Block until the next game prompt. Returns true if one arrived,
    /// false on timeout.</summary>
    public bool WaitForPrompt(double timeoutSeconds)
    {
        _inst.Checkpoint();
        return _inst.WaitForPrompt(ToTimeout(timeoutSeconds));
    }

    /// <summary>Sleep for <paramref name="seconds"/> (default 1 if &lt;= 0).</summary>
    public void Pause(double seconds)
    {
        _inst.Checkpoint();
        _inst.Sleep(TimeSpan.FromSeconds(seconds <= 0 ? 1.0 : seconds));
    }

    /// <summary>Read a session global variable ($Name in .cmd scripts).</summary>
    public string Get(string name)
    {
        _inst.Checkpoint();
        return _runtime.GetGlobal(name ?? "");
    }

    /// <summary>Write a session global variable, visible to .cmd scripts as $Name.</summary>
    public void Set(string name, string value)
    {
        _inst.Checkpoint();
        _runtime.SetGlobal(name ?? "", value ?? "");
    }

    /// <summary>Read a script-local variable.</summary>
    public string GetVar(string name)
    {
        _inst.Checkpoint();
        return _inst.Locals.TryGetValue(name ?? "", out var v) ? v : "";
    }

    /// <summary>Write a script-local variable.</summary>
    public void SetVar(string name, string value)
    {
        _inst.Checkpoint();
        if (!string.IsNullOrEmpty(name)) _inst.Locals[name] = value ?? "";
    }

    /// <summary>Seconds of roundtime remaining (0 when none).</summary>
    public int Roundtime()
    {
        _inst.Checkpoint();
        return _runtime.RoundtimeRemaining();
    }

    /// <summary>Stop this script. Deliberately skips the pause/abort checkpoint so
    /// it works even from a paused state.</summary>
    public void Stop() => _inst.Stop();

    private static TimeSpan ToTimeout(double seconds) =>
        seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);

    private static string[] SplitPatterns(string joined) =>
        string.IsNullOrEmpty(joined)
            ? Array.Empty<string>()
            : joined.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
