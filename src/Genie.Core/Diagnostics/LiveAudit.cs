using System;
using System.IO;
using Genie.Core.Events;

namespace Genie.Core.Diagnostics;

/// <summary>
/// Developer troubleshooting aid: when enabled, tees the live session into a
/// single timestamped, human-readable log so a collaborator can
/// follow exactly what the server sent and how the parser routed it — without
/// the user pasting XML/screenshots.
///
/// <para>Each line is one of:</para>
/// <list type="bullet">
///   <item><c>XML</c> — a raw chunk straight off the wire (newlines folded to ⏎).</item>
///   <item><c>TEXT [stream]</c> — a parsed <see cref="TextEvent"/> with the stream
///         it was routed to (so a "wrong window" leak is obvious at a glance).</item>
///   <item><c>NAV</c> — a room change, annotated with the live <c>$zoneid</c> /
///         <c>$roomid</c> / <c>$zonename</c> so a stale-at-the-boundary mapper
///         (the travel-stalls-at-zone-edge symptom) shows up immediately.</item>
///   <item><c>IND</c> / <c>PROMPT</c> / <c>IMG</c> / <c>COMPASS</c> — the other
///         structured events.</item>
/// </list>
///
/// Off by default; toggled with <c>#audit on|off</c>. Writes to
/// <c>&lt;LogDir&gt;/live_audit.log</c> (truncated on each enable for a clean
/// read). Local-only — never leaves the machine.
/// </summary>
public sealed class LiveAudit : IDisposable
{
    private readonly object                  _lock = new();
    private readonly string                  _path;
    private readonly IObservable<string>     _rawXml;
    private readonly IObservable<GameEvent>  _events;
    private readonly Func<string, string>    _global;   // read a script global ($zoneid …)

    private StreamWriter? _writer;
    private IDisposable?  _rawSub;
    private IDisposable?  _evtSub;

    public bool Enabled { get; private set; }
    public string Path => _path;

    /// <param name="path">Full path to the audit log file.</param>
    /// <param name="rawXml">The raw on-the-wire XML stream.</param>
    /// <param name="events">The parsed game-event stream.</param>
    /// <param name="globalLookup">Reads a script global by name (e.g. "zoneid").</param>
    public LiveAudit(string path, IObservable<string> rawXml,
                     IObservable<GameEvent> events, Func<string, string> globalLookup)
    {
        _path   = path;
        _rawXml = rawXml;
        _events = events;
        _global = globalLookup;
    }

    public void Enable()
    {
        if (Enabled) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        // Truncate for a clean slate each time auditing is turned on.
        _writer = new StreamWriter(_path, append: false) { AutoFlush = true };
        Write("==== LIVE AUDIT START ====");

        // Raw first so an XML chunk and the events it produced sit adjacent.
        _rawSub = _rawXml.Subscribe(WriteRaw, ex => Write($"!! raw stream error: {ex.Message}"));
        _evtSub = _events.Subscribe(WriteEvent, ex => Write($"!! event stream error: {ex.Message}"));
        Enabled = true;
    }

    public void Disable()
    {
        if (!Enabled) return;
        _rawSub?.Dispose(); _rawSub = null;
        _evtSub?.Dispose(); _evtSub = null;
        Write("==== LIVE AUDIT STOP ====");
        lock (_lock) { _writer?.Dispose(); _writer = null; }
        Enabled = false;
    }

    /// <summary>Append an arbitrary annotation (e.g. the App's mapper LoadStatus)
    /// to the audit stream. No-op when auditing is off.</summary>
    public void Note(string tag, string message)
    {
        if (Enabled) Write($"{tag,-5} {message}");
    }

    private void WriteRaw(string chunk)
    {
        var c = chunk.Replace("\r", "").Replace("\n", " ⏎ ").Trim();
        if (c.Length > 0) Write("XML   " + c);
    }

    private void WriteEvent(GameEvent e)
    {
        switch (e)
        {
            case TextEvent t:
                Write($"TEXT  [{t.Stream}] {t.Text}");
                break;
            case NavEvent n:
                Write($"NAV   rm={n.RoomId}  → $zoneid={_global("zoneid")} $roomid={_global("roomid")} $zonename=\"{_global("zonename")}\"");
                break;
            case IndicatorEvent i:
                Write($"IND   {i.IndicatorId}={(i.Visible ? "y" : "n")}");
                break;
            case PromptEvent p:
                Write($"PROMPT '{p.Indicator}'");
                break;
            case RoomImageEvent r:
                Write($"IMG   picture={r.PictureId}");
                break;
            case CompassEvent c:
                Write($"COMP  {c.RawXml}");
                break;
        }
    }

    private void Write(string line)
    {
        lock (_lock)
        {
            try { _writer?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {line}"); }
            catch { /* never let the audit log throw into the session */ }
        }
    }

    public void Dispose() => Disable();
}
