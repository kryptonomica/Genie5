using System.Globalization;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Genie.Core.Scripting.Js;

/// <summary>
/// A persistent JavaScript context owned by a single running <c>.cmd</c> script,
/// for Genie 4-style "array script" interop (#104). A <c>.cmd</c> can
/// <c>include</c> a <c>.js</c> function library, then call its functions with
/// <c>js &lt;expr&gt;</c> / <c>jscall &lt;var&gt; &lt;expr&gt;</c>. Those functions
/// read/write the OWNING <c>.cmd</c> script's variables through the bare
/// <c>getVar/setVar/getGlobal/setGlobal</c> globals — the Genie 4 surface.
///
/// <para>Distinct from <see cref="JsScriptRuntime"/>, which runs a whole <c>.js</c>
/// file as a standalone, long-lived program on its own thread. This context is
/// reused across the script's <c>js</c> calls (so functions defined by an
/// <c>include</c> persist) and evaluates <b>synchronously and bounded</b> on the
/// <c>.cmd</c> tick thread — these are quick array ops, not hunt loops, and must
/// never hang the loop.</para>
/// </summary>
internal sealed class JsLibraryContext
{
    private readonly Engine           _engine;
    private readonly RunawayLoopGuard _guard;
    private readonly Action<string>   _echo;

    /// <summary>Per-call statement cap: js/jscall run synchronously on the .cmd
    /// tick thread, so a runaway expression must not freeze the client. Reset
    /// before each Evaluate/LoadLibrary. Generous — real array ops are tiny.</summary>
    private const long MaxStatementsPerCall = 50_000_000;

    public JsLibraryContext(
        Func<string, string>   getVar,
        Action<string, string> setVar,
        Func<string, string>   getGlobal,
        Action<string, string> setGlobal,
        Action<string>         echo,
        Action<string>         put)
    {
        _echo  = echo;
        _guard = new RunawayLoopGuard(MaxStatementsPerCall);

        _engine = new Engine(opts =>
        {
            opts.LimitRecursion(128);
            opts.LimitMemory(128L * 1024 * 1024);
            opts.Constraints.Constraints.Add(_guard);
        });

        _engine.SetValue("__h", new Bridge(getVar, setVar, getGlobal, setGlobal, echo, put));
        _engine.Execute(Prelude);
    }

    /// <summary>Load a <c>.js</c> file's definitions into the context (an
    /// <c>include foo.js</c>): strips a leading UTF-8 BOM and applies the Genie 4
    /// <c>.length()</c> → <c>.length</c> compatibility rewrite (#104) so legacy
    /// array libraries load unmodified. Returns false (and echoes) on a JS error.</summary>
    public bool LoadLibrary(string path)
    {
        string src;
        try { src = StripBom(File.ReadAllText(path)); }
        catch (Exception ex) { _echo($"[script] include: cannot read '{path}': {ex.Message}"); return false; }

        return Run(() => _engine.Execute(MigrateLengthCalls(src)), $"include '{Path.GetFileName(path)}'");
    }

    /// <summary>Evaluate a JS expression in the context (a <c>js</c> / <c>jscall</c>
    /// body) and return its result as a string ("" for undefined/null) for
    /// <c>jscall</c> to store into a .cmd variable. Echoes + returns "" on error.</summary>
    public string Evaluate(string expression)
    {
        string result = "";
        Run(() => { result = JsToString(_engine.Evaluate(MigrateLengthCalls(expression))); }, "js");
        return result;
    }

    /// <summary>Run a bounded engine action with the standard guards; report
    /// failures through the script echo (never throws to the .cmd tick loop).</summary>
    private bool Run(Action action, string what)
    {
        _guard.ResetCounter();
        try { action(); return true; }
        catch (RunawayLoopException)
        {
            _echo($"[script] {what}: aborted — JS ran {MaxStatementsPerCall:N0} statements (runaway loop?).");
        }
        catch (MemoryLimitExceededException) { _echo($"[script] {what}: aborted — JS memory limit (128 MB) exceeded."); }
        catch (JavaScriptException jse)       { _echo($"[script] {what}: JS error — {jse.Message}"); }
        catch (Exception ex)                  { _echo($"[script] {what}: error — {ex.Message}"); }
        return false;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    internal static string StripBom(string s) =>
        s.Length > 0 && s[0] == '﻿' ? s[1..] : s;

    /// <summary>Convert a Jint result to the string a .cmd variable should hold.
    /// Integral numbers render without a trailing ".0" (so an index reads "3",
    /// not "3.0"); strings/bools pass through; null/undefined → "".</summary>
    private static string JsToString(JsValue v)
    {
        if (v is null || v.IsUndefined() || v.IsNull()) return "";
        if (v.IsString())  return v.AsString();
        if (v.IsBoolean()) return v.AsBoolean() ? "true" : "false";
        if (v.IsNumber())
        {
            var d = v.AsNumber();
            return d == Math.Truncate(d) && !double.IsInfinity(d)
                ? ((long)d).ToString(CultureInfo.InvariantCulture)
                : d.ToString(CultureInfo.InvariantCulture);
        }
        return v.ToString();
    }

    /// <summary>
    /// Rewrite Genie 4's <c>arr.length()</c> (method call) to standard
    /// <c>arr.length</c> (property) — #104. Genie 4 ran Jint 0.8.8, which tolerated
    /// calling the length; modern Jint is spec-correct (length is a data property),
    /// so <c>.length()</c> throws. A char-scanner does the rewrite only in normal
    /// code — it skips string ('/"/`) and comment (// , /* */) spans so a
    /// <c>".length()"</c> inside a string or comment is never touched.
    /// (Regex-literal spans are a known follow-up; the real libraries use only
    /// simple regexes with no embedded <c>.length()</c>.)
    /// </summary>
    internal static string MigrateLengthCalls(string src)
    {
        if (src.IndexOf(".length", StringComparison.Ordinal) < 0) return src;

        var sb = new StringBuilder(src.Length);
        int i = 0, n = src.Length;
        while (i < n)
        {
            char c = src[i];

            // line comment //…
            if (c == '/' && i + 1 < n && src[i + 1] == '/')
            {
                int start = i; i += 2;
                while (i < n && src[i] != '\n') i++;
                sb.Append(src, start, i - start);
                continue;
            }
            // block comment /* … */
            if (c == '/' && i + 1 < n && src[i + 1] == '*')
            {
                int start = i; i += 2;
                while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/')) i++;
                i = Math.Min(n, i + 2);
                sb.Append(src, start, i - start);
                continue;
            }
            // string literal '…' "…" `…`
            if (c == '"' || c == '\'' || c == '`')
            {
                char q = c; int start = i; i++;
                while (i < n)
                {
                    if (src[i] == '\\') { i += 2; continue; }
                    if (src[i] == q) { i++; break; }
                    i++;
                }
                sb.Append(src, start, Math.Min(i, n) - start);
                continue;
            }
            // .length() → .length   (normal code only)
            if (c == '.' && IsLengthCall(src, i, out int consumed))
            {
                sb.Append(".length");
                i += consumed;
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>True if <paramref name="src"/> at the '.' index is exactly
    /// <c>.length</c> followed by an empty call <c>()</c> (allowing whitespace),
    /// and NOT a longer identifier like <c>.lengthy</c>. Sets the char count from
    /// the '.' through the ')'.</summary>
    private static bool IsLengthCall(string src, int dot, out int consumed)
    {
        consumed = 0;
        const string kw = ".length";
        if (dot + kw.Length > src.Length) return false;
        for (int k = 0; k < kw.Length; k++) if (src[dot + k] != kw[k]) return false;

        int p = dot + kw.Length;
        if (p < src.Length && (char.IsLetterOrDigit(src[p]) || src[p] == '_' || src[p] == '$'))
            return false;   // .lengthFoo — not a length call

        while (p < src.Length && (src[p] == ' ' || src[p] == '\t')) p++;
        if (p >= src.Length || src[p] != '(') return false;
        p++;
        while (p < src.Length && (src[p] == ' ' || src[p] == '\t')) p++;
        if (p >= src.Length || src[p] != ')') return false;
        p++;

        consumed = p - dot;
        return true;
    }

    /// <summary>Bare-global Genie 4 surface + a <c>genie.*</c> alias. The libraries
    /// call <c>getVar/setVar/getGlobal/setGlobal/echo</c> directly (no namespace).</summary>
    private const string Prelude = @"
function getVar(n){ return __h.GetVar(String(n)); }
function setVar(n, v){ __h.SetVar(String(n), v==null?'':String(v)); }
function getGlobal(n){ return __h.GetGlobal(String(n)); }
function setGlobal(n, v){ __h.SetGlobal(String(n), v==null?'':String(v)); }
function get(n){ return __h.GetGlobal(String(n)); }
function set(n, v){ __h.SetGlobal(String(n), v==null?'':String(v)); }
function echo(t){ __h.Echo(t==null?'':String(t)); }
function put(c){ __h.Put(String(c)); }
function send(c){ __h.Put(String(c)); }
var genie = { getVar:getVar, setVar:setVar, getGlobal:getGlobal, setGlobal:setGlobal,
              get:get, set:set, echo:echo, put:put, send:send };
var game = genie;
";

    /// <summary>CLR object bridged into the JS context; the prelude wraps these as
    /// the bare lowercase globals. Bound to the OWNING .cmd instance's variable
    /// scope by the caller (getVar/setVar → that instance's %vars; getGlobal/
    /// setGlobal → session globals / user #vars).</summary>
    private sealed class Bridge
    {
        private readonly Func<string, string>   _getVar, _getGlobal;
        private readonly Action<string, string> _setVar, _setGlobal;
        private readonly Action<string>         _echo, _put;

        public Bridge(
            Func<string, string> getVar, Action<string, string> setVar,
            Func<string, string> getGlobal, Action<string, string> setGlobal,
            Action<string> echo, Action<string> put)
        {
            _getVar = getVar; _setVar = setVar;
            _getGlobal = getGlobal; _setGlobal = setGlobal;
            _echo = echo; _put = put;
        }

        public string GetVar(string n)            => _getVar(n ?? "") ?? "";
        public void   SetVar(string n, string v)  { if (!string.IsNullOrEmpty(n)) _setVar(n, v ?? ""); }
        public string GetGlobal(string n)         => _getGlobal(n ?? "") ?? "";
        public void   SetGlobal(string n, string v){ if (!string.IsNullOrEmpty(n)) _setGlobal(n, v ?? ""); }
        public void   Echo(string t)              => _echo(t ?? "");
        public void   Put(string c)               { if (!string.IsNullOrEmpty(c)) _put(c); }
    }
}
