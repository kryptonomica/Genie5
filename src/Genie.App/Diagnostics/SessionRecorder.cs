using System.IO;
using Genie.Core;

namespace Genie.App.Diagnostics;

/// <summary>
/// Opt-in raw-XML capture for the live game session — mirrors what
/// <c>TestHarness</c> does in console mode, but driven from the GUI.
/// Writes each chunk emitted by <see cref="GenieCore.RawXmlStream"/>
/// verbatim into a per-session file under <c>{AppData}/Genie5/Logs/</c>.
///
/// <para>
/// Lifecycle: user toggles via the File menu → <see cref="Start"/> opens a
/// fresh file (one per Start invocation) and subscribes; <see cref="Stop"/>
/// (or Dispose) unsubscribes and closes the writer. Stop on disconnect is
/// the caller's responsibility — the recorder doesn't observe the
/// connection state itself.
/// </para>
///
/// <para>
/// Policy note: matches gate #6 from the May 24, 2026 compliance review.
/// In public builds this defaults to OFF. Recording the user's own client
/// stream locally is fine under DR policy; transmitting it externally is
/// gated separately by the AI pipeline rules.
/// </para>
/// </summary>
public sealed class SessionRecorder : IDisposable
{
    private readonly string     _logsDir;
    private readonly Action<string>? _diagnosticLog;
    private StreamWriter?       _writer;
    private IDisposable?        _subscription;
    private string?             _currentFile;
    private long                _chunkCount;
    private long                _byteCount;

    /// <summary>
    /// Fires whenever recording starts (path) or stops (null). The
    /// view-model uses this to flip its <c>IsRecording</c> reactive bool.
    /// </summary>
    public event Action<string?>? CurrentFileChanged;

    public SessionRecorder(string logsDir, Action<string>? diagnosticLog = null)
    {
        _logsDir       = logsDir;
        _diagnosticLog = diagnosticLog;
        Directory.CreateDirectory(_logsDir);
    }

    /// <summary>True while a session file is open and the subscription is live.</summary>
    public bool    IsRecording => _writer is not null;

    /// <summary>Absolute path of the currently-recording file, or null when stopped.</summary>
    public string? CurrentFile => _currentFile;

    /// <summary>
    /// Begin recording from the given core's raw XML stream. If a previous
    /// recording is still open it's closed first — Start is idempotent in
    /// the sense that calling it twice doesn't double-subscribe. Character
    /// name is sanitised for the filename.
    /// </summary>
    public void Start(GenieCore core, string characterName)
    {
        Stop();

        var safe = string.IsNullOrWhiteSpace(characterName) ? "unknown" : characterName;
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');

        // YYYYMMDD_HHMMSS gives us session-grade resolution without colons
        // (which Windows filenames can't have). Matches the convention from
        // TestHarness so anything that consumed those files works here too.
        _currentFile = Path.Combine(
            _logsDir,
            $"raw_session_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.xml");

        // AutoFlush so a crash mid-session still leaves the captured prefix
        // on disk — important for debugging exactly the kind of bug that
        // would prompt us to enable recording in the first place.
        _writer = new StreamWriter(_currentFile, append: false)
        {
            AutoFlush = true,
        };
        _chunkCount = 0;
        _byteCount  = 0;

        _subscription = core.RawXmlStream.Subscribe(chunk =>
        {
            try
            {
                _writer?.Write(chunk);
                Interlocked.Increment(ref _chunkCount);
                Interlocked.Add(ref _byteCount, chunk?.Length ?? 0);
            }
            catch (Exception ex) { ErrorLog.Log("SessionRecorder.Write", ex); }
        });

        _diagnosticLog?.Invoke($"[recorder] subscribed to RawXmlStream → {Path.GetFileName(_currentFile)}");
        CurrentFileChanged?.Invoke(_currentFile);
    }

    /// <summary>
    /// Stop the current recording. Safe to call when not recording (no-op).
    /// </summary>
    public void Stop()
    {
        // Snapshot file info BEFORE we null _currentFile, so the diagnostic
        // can name the just-closed recording.
        var closingFile  = _currentFile;
        var chunkCount   = Interlocked.Read(ref _chunkCount);
        var byteCount    = Interlocked.Read(ref _byteCount);

        _subscription?.Dispose();
        _subscription = null;

        try { _writer?.Dispose(); }
        catch (Exception ex) { ErrorLog.Log("SessionRecorder.CloseFile", ex); }
        _writer = null;

        _currentFile = null;
        CurrentFileChanged?.Invoke(null);

        if (closingFile is not null)
        {
            _diagnosticLog?.Invoke(
                $"[recorder] stopped → {Path.GetFileName(closingFile)}  ({chunkCount:N0} chunks / {byteCount:N0} bytes)");
        }
    }

    public void Dispose() => Stop();
}
