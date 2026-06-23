// Genie.Core — console test harness
//
// Modes:
//   dotnet run -- DR <account> <password> <character>
//       Connect live in StormFront (XML) mode. Logs to test_results/raw_session_{char}_{ts}.xml.
//
//   dotnet run -- WIZ <account> <password> <character>
//       Connect live in Wizard (plain-text) mode. Logs to test_results/raw_session_{char}_{ts}.txt.
//       Run this in a second terminal alongside DR to capture ground-truth plain text.
//
//   dotnet run -- LICH - - -
//       Connect to Lich5 proxy on localhost:8000.
//
//   dotnet run -- REPLAY <session-file> [speed]
//       Start a local replay server and stream the recorded file.
//       speed: 0=max (default), 1.0=real-time, 2.0=double speed
//       File is resolved from test_results/ if no directory is given.
//
//   dotnet run -- DROP [watchdog]
//       Self-contained repro of the disconnect-detection path (#87) — no live
//       game, no recording needed. Connects via DevReplay, drops the link, and
//       asserts Disconnected fires and $connected flips 1→0. Exit code 0 = all
//       checks passed. `DROP watchdog` holds the socket open but silent to also
//       exercise the server-activity watchdog (Error → Disconnected).
//
//   dotnet run -- COMPARE <session-file>
//       Run replay at max speed, then write two diff-ready files to test_results/:
//         <name>_baseline.txt  — tag-stripped ground truth from raw XML
//         <name>_parsed.txt    — TextEvent output from our parser
//       Console summary shows lines unique to each file.
//
//   dotnet run -- ALIGN <xml-session> <txt-session>
//       Compare an XML session (StormFront) against a Wizard plain-text session
//       captured in the same room with the same commands. Generates baselines from
//       both files and diffs them — TXT is ground truth, XML parser output is tested.
//       Both files resolved from test_results/ if no directory given.
//
// AI commands (only active when ANTHROPIC_API_KEY env var is set):
//   .parser   — AI identifies unknown XML tags and suggests new C# records
//   .insight  — AI summarises what the character is doing and suggests actions
//   .ai <q>   — Free-form question with current game state injected
//   .drain    — Print and clear the raw XML buffer
//   .quit     — Disconnect and exit

using System.Net;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Genie.Core;
using Genie.Core.AI;
using Genie.Core.Connection;
using Genie.Core.Events;
using Microsoft.Extensions.Logging;

// All generated files (raw sessions, replay outputs, compare results) land here.
const string ResultsDir = "test_results";
Directory.CreateDirectory(ResultsDir);

var loggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(LogLevel.Warning));

var args_list = args.ToList();
if (args_list.Count < 1)
{
    PrintUsage();
    return;
}

var mode = args_list[0].ToUpperInvariant();

// ── REPLAY / COMPARE mode — start local server and connect to it ─────────────

DevReplayServer? replayServer = null;

// COMPARE-mode state — populated by the COMPARE case below.
bool         isCompare   = false;
string       compareName = "";
var          parsedLines = new List<string>();

ConnectionConfig connCfg;

switch (mode)
{
    case "COMPARE":
    {
        if (args_list.Count < 2)
        {
            Console.WriteLine("Usage: dotnet run -- COMPARE <session-file>");
            return;
        }
        var filePath = ResolveSessionFile(args_list[1], ResultsDir);
        if (filePath is null) return;

        compareName = Path.GetFileNameWithoutExtension(filePath);
        var baselineLines = GenerateXmlBaseline(filePath);
        var baselinePath  = Path.Combine(ResultsDir, compareName + "_baseline.txt");
        await File.WriteAllLinesAsync(baselinePath, baselineLines);
        Console.WriteLine($"[COMPARE] Baseline → {baselinePath} ({baselineLines.Count} lines)");

        replayServer = new DevReplayServer(
            filePath, port: 8000, speed: 0,
            log: loggerFactory.CreateLogger<DevReplayServer>());
        replayServer.Start();
        await Task.Delay(100);

        connCfg = new ConnectionConfig
        {
            Mode                 = ConnectionMode.DevReplay,
            LichProxyHost        = "127.0.0.1",
            LichProxyPort        = 8000,
            MaxReconnectAttempts = 0
        };

        isCompare = true;
        mode      = "REPLAY";
        break;
    }

    case "XMLGAP":
    {
        // Prototype of the "Report XML Gap" flow: for each element type in a
        // recording that the parser does NOT consume, draft a redacted,
        // review-ready GitHub issue (title + body + prefill URL). No network,
        // no posting — just prints the drafts a user would review.
        if (args_list.Count < 2)
        {
            Console.WriteLine("Usage: dotnet run -- XMLGAP <session-file>");
            return;
        }
        var filePath = ResolveSessionFile(args_list[1], ResultsDir);
        if (filePath is null) return;

        var xml = await File.ReadAllTextAsync(filePath);
        var ctx = new Genie.Core.Diagnostics.XmlGapReport.ReportContext(
            AppVersion: "5.0.0-alpha.6.1",
            Os: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Commit: "local",
            Trigger: $"replaying {Path.GetFileName(filePath)}");

        var tagRx = new System.Text.RegularExpressions.Regex(@"<([A-Za-z][A-Za-z0-9]*)");
        var seen  = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gaps  = 0;

        foreach (System.Text.RegularExpressions.Match m in tagRx.Matches(xml))
        {
            var tag = m.Groups[1].Value.ToLowerInvariant();
            if (!seen.Add(tag)) continue;
            var fate = Genie.Core.Parser.DrXmlParser.ClassifyTag(tag);
            if (fate is not (Genie.Core.Parser.DrXmlParser.TagFate.DroppedData
                          or Genie.Core.Parser.DrXmlParser.TagFate.Unknown))
                continue;

            // Sample = this element up to the next <prompt> (or +300 chars), so
            // the redactor sees a tag-complete chunk.
            var start = m.Index;
            var end   = xml.IndexOf("<prompt", start + 1, StringComparison.OrdinalIgnoreCase);
            if (end < 0 || end - start > 300) end = Math.Min(xml.Length, start + 300);

            var draft = Genie.Core.Diagnostics.XmlGapReport.Build(tag, fate, xml[start..end], ctx);
            gaps++;
            Console.WriteLine(new string('=', 72));
            Console.WriteLine($"GAP #{gaps}: <{tag}>  ({fate})");
            Console.WriteLine(new string('-', 72));
            Console.WriteLine("TITLE : " + draft.Title);
            Console.WriteLine("LABELS: " + draft.Labels);
            Console.WriteLine("BODY  :\n" + draft.Body + "\n");
            Console.WriteLine("PREFILL URL (opens the GitHub new-issue form, user submits):");
            Console.WriteLine(draft.Url + "\n");
        }
        Console.WriteLine(new string('=', 72));
        Console.WriteLine($"[XMLGAP] {gaps} unconsumed element type(s) drafted.");
        return;
    }

    case "REPLAY":
    {
        if (args_list.Count < 2)
        {
            Console.WriteLine("Usage: dotnet run -- REPLAY <session-file> [speed]");
            Console.WriteLine("  speed: 0=max (default), 1.0=real-time, 2.0=double");
            return;
        }
        var filePath = ResolveSessionFile(args_list[1], ResultsDir);
        if (filePath is null) return;

        var speed = args_list.Count > 2 ? double.Parse(args_list[2]) : 0.0;

        replayServer = new DevReplayServer(
            filePath, port: 8000, speed: speed,
            log: loggerFactory.CreateLogger<DevReplayServer>());
        replayServer.Start();
        await Task.Delay(100);

        connCfg = new ConnectionConfig
        {
            Mode                 = ConnectionMode.DevReplay,
            LichProxyHost        = "127.0.0.1",
            LichProxyPort        = 8000,
            MaxReconnectAttempts = 0
        };
        break;
    }

    case "DROP":
    {
        // Self-contained repro of the disconnect-DETECTION path (#87) — no live
        // game. Streams a tiny synthetic session through the real GenieCore +
        // GameConnection + DevReplayServer stack, drops the link, and asserts:
        //   • ConnectionState fires Disconnected
        //   • $connected flips "1" → "0"
        // `DROP watchdog` instead holds the socket open but SILENT, with a short
        // ServerActivityTimeoutMs, to exercise the watchdog → Error → Disconnected
        // path (the half-open shape TCP keepalive normally handles in the field).
        var watchdog = args_list.Count > 1
            && args_list[1].Equals("watchdog", StringComparison.OrdinalIgnoreCase);

        // Minimal but valid-ish session: <settingsInfo/> is GenieCore's ready
        // signal; the rest is one bold line + a prompt so there's real traffic
        // to stamp server activity before the drop.
        var synthetic =
            "<settingsInfo instance='DR'/>\n" +
            "<pushBold/>You are standing in a featureless test void.<popBold/>\n" +
            "<prompt time=\"0\">&gt;</prompt>\n";
        var dropFile = Path.Combine(ResultsDir, "drop_repro_session.xml");
        await File.WriteAllTextAsync(dropFile, synthetic);

        await using var dropServer = new DevReplayServer(
            dropFile, port: 8000, speed: 0, hangAfterStream: watchdog,
            log: loggerFactory.CreateLogger<DevReplayServer>());
        dropServer.Start();
        await Task.Delay(100);

        var dropCfg = new ConnectionConfig
        {
            Mode                    = ConnectionMode.DevReplay,
            LichProxyHost           = "127.0.0.1",
            LichProxyPort           = 8000,
            MaxReconnectAttempts    = 0,
            ServerActivityTimeoutMs = watchdog ? 3_000 : 0,
        };

        var ok = await RunDropRepro(dropCfg, watchdog, loggerFactory);
        Environment.ExitCode = ok ? 0 : 1;
        return;
    }

    case "RECONNECT":
    {
        // Persistent-core repro (#88 / #46 Phase 3) — no live game. Connects TWICE
        // through DevReplay on ONE GenieCore and asserts the persistent core holds:
        //   • Connected fires on BOTH connects (the relay observables survive reconnect)
        //   • a TextEvent arrives on the SECOND connection (the relay re-feed works —
        //     a fresh parser is wired into the SAME relay the subscriber holds)
        //   • Scripts / Commands are the SAME instance across reconnect (engines persist)
        var session =
            "<settingsInfo instance='DR'/>\n" +
            "<pushBold/>You are standing in a featureless reconnect void.<popBold/>\n" +
            "<prompt time=\"0\">&gt;</prompt>\n";
        var rcFile = Path.Combine(ResultsDir, "reconnect_repro_session.xml");
        await File.WriteAllTextAsync(rcFile, session);

        var okrc = await RunReconnectRepro(rcFile, loggerFactory);
        Environment.ExitCode = okrc ? 0 : 1;
        return;
    }

    case "MAPCOALESCE":
    {
        // PR #92 / #91 Bug 1 — no live game, no map data. Drives the real
        // DrXmlParser → GameStateEngine → MapperGameStateAdapter through the exact
        // incoherent room sequence and asserts the adapter coalesces to the prompt.
        var okmc = RunMapCoalesceRepro(loggerFactory);
        Environment.ExitCode = okmc ? 0 : 1;
        return;
    }

    case "MOVEORDER":
    {
        // F1 (PR #92 ordering) — no live game. Demonstrates that PR #92's
        // OnPrompt-before-OnRoomChanged ordering prematurely unblocks a `move`
        // issued by a pause-resumed script in the same turn, and that the safe
        // (OnRoomChanged-first) order fixes it.
        var okmo = RunMoveOrderRepro(loggerFactory);
        Environment.ExitCode = okmo ? 0 : 1;
        return;
    }

    case "REQGATE":
    {
        // Pathfinder "No path" fix — pure-function check of ExitRequirement.IsMet.
        // Locks the "assumed reachable when class/level/skill are UNKNOWN" contract
        // (class-gated / level-gated exits were wrongly blocked when class was null
        // or level was 0, contradicting the mapper's own banner) while keeping
        // known-and-failing gates blocked.
        var okrq = RunReqGateRepro();
        Environment.ExitCode = okrq ? 0 : 1;
        return;
    }

    case "LICH":
        connCfg = new ConnectionConfig
        {
            Mode          = ConnectionMode.LichProxy,
            LichProxyPort = 8000
        };
        break;

    case "DR":
    {
        if (args_list.Count < 4)
        {
            PrintUsage();
            return;
        }
        connCfg = new ConnectionConfig
        {
            Mode            = ConnectionMode.DirectSGE,
            AccountName     = args_list[1],
            AccountPassword = args_list[2],
            CharacterName   = args_list[3],
            GameCode        = "DR",
            ClientMode      = GameClientMode.StormFront
        };
        break;
    }

    case "WIZ":
    {
        if (args_list.Count < 4)
        {
            PrintUsage();
            return;
        }
        connCfg = new ConnectionConfig
        {
            Mode            = ConnectionMode.DirectSGE,
            AccountName     = args_list[1],
            AccountPassword = args_list[2],
            CharacterName   = args_list[3],
            GameCode        = "DR",
            ClientMode      = GameClientMode.Wizard
        };
        break;
    }

    case "LIST":
    {
        if (args_list.Count < 3)
        {
            Console.WriteLine("Usage: dotnet run -- LIST <account> <password> [gamecode]");
            Console.WriteLine("  Lists available characters for the account without logging in.");
            Console.WriteLine("  gamecode defaults to DR");
            return;
        }
        var listCfg = new ConnectionConfig
        {
            AccountName     = args_list[1],
            AccountPassword = args_list[2],
            GameCode        = args_list.Count > 3 ? args_list[3] : "DR"
        };
        var sge = new SgeAuthClient(loggerFactory.CreateLogger<SgeAuthClient>());
        try
        {
            var chars = await sge.ListCharactersAsync(listCfg);
            Console.WriteLine($"Characters on {listCfg.AccountName} ({listCfg.GameCode}):");
            foreach (var c in chars)
                Console.WriteLine($"  [{c.Code}]  {c.Name}");
            if (chars.Count == 0)
                Console.WriteLine("  (none found)");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAILED] {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
        }
        return;
    }

    case "CAPTUREVERIFY":
    {
        if (args_list.Count < 2)
        {
            Console.WriteLine("Usage: dotnet run -- CAPTUREVERIFY <session-file>");
            Console.WriteLine("  Runs the AnalystCapture redactor over a recording (real parser for the");
            Console.WriteLine("  parsed side, RedactRawXml for the raw side) and reports any other-player");
            Console.WriteLine("  content that LEAKS past redaction. Target: 0 leaks in both artifacts.");
            return;
        }
        var capPath = ResolveSessionFile(args_list[1], ResultsDir);
        if (capPath is null) return;

        var rawAll   = await File.ReadAllTextAsync(capPath);
        var redactor = new Genie.Core.Capture.CaptureRedactor();

        // ── parsed side (_streams.txt): real DrXmlParser → TextEvents → redact ──
        var parser    = new Genie.Core.Parser.DrXmlParser(
            loggerFactory.CreateLogger<Genie.Core.Parser.DrXmlParser>());
        var keptLines = new List<string>();
        int events = 0, droppedStream = 0, droppedContent = 0;
        using (parser.GameEvents.OfType<Genie.Core.Events.TextEvent>().Subscribe(ev =>
        {
            events++;
            if (redactor.ShouldDropStream(ev.Stream)) { droppedStream++;  return; }
            if (redactor.ShouldDropContent(ev.Text))  { droppedContent++; return; }
            keptLines.Add(ev.Text);
        }))
        {
            for (int i = 0; i < rawAll.Length; i += 4096)
                parser.Feed(rawAll.Substring(i, Math.Min(4096, rawAll.Length - i)));
        }

        // ── raw side (.xml): redact the whole raw block ──
        var redactedXml = redactor.RedactRawXml(rawAll);

        // ── leak detector (independent of the redactor's own patterns): any
        //    surviving other-player marker — DEAD>, a quoted third-person utterance,
        //    or an OOC: tag. Self ("You …") is excluded; it isn't other-player. ──
        var leakRe = new System.Text.RegularExpressions.Regex(
            @"(?:^|\n)[ \t]*(?:DEAD>|[A-Z][\w'’.\-]*\b[^""\n]*?\b(?:says|asks|whispers|exclaims|shouts|mutters|murmurs)\b[^""\n]*""|[^\n]*\bOOC:)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        bool IsSelf(string l) => l.TrimStart().StartsWith("You ", StringComparison.OrdinalIgnoreCase);

        var streamLeaks = keptLines.Where(l => !IsSelf(l) && leakRe.IsMatch(l)).ToList();
        var xmlLeaks    = redactedXml.Split('\n').Where(l => !IsSelf(l) && leakRe.IsMatch(l)).ToList();

        Console.WriteLine($"[CAPTUREVERIFY] {Path.GetFileName(capPath)}");
        Console.WriteLine($"  TextEvents: {events}   dropped(stream): {droppedStream}   dropped(content): {droppedContent}   kept: {keptLines.Count}");
        Console.WriteLine($"  XML spans redacted: {redactor.DroppedXmlSpans}");
        Console.ForegroundColor = (streamLeaks.Count + xmlLeaks.Count) == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  LEAKS — _streams.txt: {streamLeaks.Count}   .xml: {xmlLeaks.Count}   (target 0)");
        Console.ResetColor();
        foreach (var l in streamLeaks.Take(12)) Console.WriteLine($"    streams LEAK> {l.Trim()}");
        foreach (var l in xmlLeaks.Take(12))    Console.WriteLine($"    xml     LEAK> {l.Trim()}");
        return;
    }

    case "ALIGN":
    {
        if (args_list.Count < 3)
        {
            Console.WriteLine("Usage: dotnet run -- ALIGN <xml-session> <txt-session>");
            Console.WriteLine("  xml-session : StormFront XML session  (raw_session_CharA_*.xml)");
            Console.WriteLine("  txt-session : Wizard plain-text session (raw_session_CharB_*.txt)");
            return;
        }
        var xmlFile = ResolveSessionFile(args_list[1], ResultsDir);
        var txtFile = ResolveSessionFile(args_list[2], ResultsDir);
        if (xmlFile is null || txtFile is null) return;

        var xmlName = Path.GetFileNameWithoutExtension(xmlFile);
        var txtName = Path.GetFileNameWithoutExtension(txtFile);

        var xmlBaseline = GenerateXmlBaseline(xmlFile);
        var txtBaseline = ReadTxtBaseline(txtFile);

        // Normalize prompt prefixes so both sides compare on content only.
        // SF XML has "H> text", "> text", "H> H> text" (double prompts); WIZ TXT is
        // already stripped by ReadTxtBaseline. Also collapse runs of whitespace and
        // deduplicate (SF sends speech/whisper to both talk and main streams).
        var promptNormRe = new Regex(@"^[A-Za-z]*>\s*");
        var wsNormRe     = new Regex(@"\s{2,}");
        static string NormalizeLine(string l, Regex promptRe, Regex wsRe)
        {
            while (promptRe.IsMatch(l))
                l = promptRe.Replace(l, "");
            return wsRe.Replace(l.Trim(), " ");
        }
        xmlBaseline = xmlBaseline
            .Select(l => NormalizeLine(l, promptNormRe, wsNormRe))
            .Where(l => l.Length > 0)
            .Distinct()
            .ToList();
        txtBaseline = txtBaseline
            .Select(l => NormalizeLine(l, promptNormRe, wsNormRe))
            .Where(l => l.Length > 0)
            .Distinct()
            .ToList();

        var xmlBaselinePath = Path.Combine(ResultsDir, xmlName + "_baseline.txt");
        var txtBaselinePath = Path.Combine(ResultsDir, txtName + "_baseline.txt");

        await File.WriteAllLinesAsync(xmlBaselinePath, xmlBaseline);
        await File.WriteAllLinesAsync(txtBaselinePath, txtBaseline);

        Console.WriteLine($"[ALIGN] XML baseline → {xmlBaselinePath} ({xmlBaseline.Count} lines)");
        Console.WriteLine($"[ALIGN] TXT baseline → {txtBaselinePath} ({txtBaseline.Count} lines)");
        Console.WriteLine($"[ALIGN] TXT = ground truth  |  XML = parser output under test");

        // TXT is ground truth; XML tag-stripped output is what the parser produced.
        PrintCompareSummary(txtBaselinePath, xmlBaselinePath);
        Console.WriteLine("Done.");
        return;
    }

    case "VERBS":
    {
        // Scan every recorded XML session for <d> / <d cmd> link occurrences
        // and write a rich markdown catalog. No live connection needed; no
        // parser invocation either — pure regex over the raw bytes so we see
        // exactly what the server emitted, not what our parser made of it.
        var pattern = args_list.Count > 1 ? args_list[1] : "*.xml";
        var outFile = Path.Combine(ResultsDir, "verb_catalog.md");
        var verbCount = GenerateVerbCatalog(ResultsDir, pattern, outFile);
        Console.WriteLine($"[VERBS] Wrote {outFile}");
        Console.WriteLine($"[VERBS] {verbCount.UniqueCount} unique canonical verbs / {verbCount.OccurrenceCount} total occurrences across {verbCount.FileCount} files");
        return;
    }

    case "FE_DIFF":
    {
        // Compare two recordings tag-by-tag. Designed for FE:GENIE vs
        // FE:STORM A/B comparisons but works on any two .xml captures.
        // The A/B workflow: run the same script twice (once with each FE
        // identifier) and pass both recordings to this mode.
        if (args_list.Count < 3)
        {
            Console.WriteLine("Usage: dotnet run -- FE_DIFF <fileA> <fileB>");
            Console.WriteLine("  Compares two recordings and writes test_results/fe_diff_<ts>.md");
            Console.WriteLine("  Convention: fileA = FE:GENIE, fileB = FE:STORM");
            return;
        }
        var fileA = ResolveSessionFile(args_list[1], ResultsDir);
        var fileB = ResolveSessionFile(args_list[2], ResultsDir);
        if (fileA is null || fileB is null) return;

        var outFile = Path.Combine(ResultsDir, $"fe_diff_{DateTime.Now:yyyyMMdd_HHmmss}.md");
        GenerateFeDiff(fileA, fileB, outFile);
        Console.WriteLine($"[FE_DIFF] Wrote {outFile}");
        return;
    }

    default:
        PrintUsage();
        return;
}

// ── AI config — only active if API key is set ────────────────────────────────

var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
var aiCfg  = string.IsNullOrEmpty(apiKey) ? null : new AiConfig
{
    ApiKey                      = apiKey,
    MaxContextChars             = 6_000,
    MaxResponseTokens           = 1_024,
    AutoAnalysisIntervalSeconds = 0
};

if (aiCfg is null)
    Console.WriteLine("[INFO] No ANTHROPIC_API_KEY set — AI commands disabled. Running in parser-dev mode.");

// ── Wire up GenieCore ────────────────────────────────────────────────────────

// Persistent core: build once (data root fixed from the connection's override),
// then ConnectAsync(connCfg) builds the per-connection layer and dials.
await using var core = new GenieCore(connCfg.DataDirectoryOverride, aiCfg, loggerFactory);

// ── Tracker verification hook (opt-in: GENIE_VERIFY_TRACKERS=1) ───────────────
// Observe the built-in trackers' window renders so a REPLAY can prove
// SpellTimer / Experience / TimeTracker work end-to-end without a UI. The
// script globals they publish are dumped after the replay completes.
var verifyTrackers = Environment.GetEnvironmentVariable("GENIE_VERIFY_TRACKERS") == "1";
var trackerPanels  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
if (verifyTrackers)
    core.SetPluginWindow += (window, content) => trackerPanels[window] = content;

// Stream-routing verification (opt-in: GENIE_VERIFY_STREAMS=1). Bucket every
// parsed TextEvent by Stream id, exactly as the App's StreamTabsViewModel does,
// to prove what the PARSER tags — isolating "Whispers shows main content" to the
// parser (buffer really gets main lines) vs the view layer. Subscribed HERE,
// before connect, so a speed-0 replay is captured in full.
var verifyStreams = Environment.GetEnvironmentVariable("GENIE_VERIFY_STREAMS") == "1";
var streamBuckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
if (verifyStreams)
    core.GameEvents.OfType<TextEvent>().Subscribe(te =>
    {
        if (!streamBuckets.TryGetValue(te.Stream, out var list))
            streamBuckets[te.Stream] = list = new List<string>();
        list.Add(te.Text);
    });

// Log filename includes character name when available; WIZ sessions get .txt extension.
var charPart   = !string.IsNullOrEmpty(connCfg.CharacterName) ? $"_{connCfg.CharacterName}" : "";
var logExt     = connCfg.ClientMode == GameClientMode.Wizard ? ".txt" : ".xml";
var rawLogName = mode == "REPLAY"
    ? $"replay_out_{DateTime.Now:yyyyMMdd_HHmmss}.xml"
    : $"raw_session{charPart}_{DateTime.Now:yyyyMMdd_HHmmss}{logExt}";
var rawLogPath = Path.Combine(ResultsDir, rawLogName);

var rawLog = File.CreateText(rawLogPath);
core.RawXmlStream.Subscribe(chunk => rawLog.Write(chunk));
Console.WriteLine($"[INFO] Raw log → {rawLogPath}");

// Structured parsed log — one line per TextEvent, prefixed with [STREAM].
// Skipped for REPLAY mode (raw replay already logs elsewhere).
StreamWriter? parsedLog = null;
if (mode != "REPLAY")
{
    var parsedLogName = rawLogName.Replace(logExt, "_streams.txt").Replace(".xml", "_streams.txt");
    var parsedLogPath = Path.Combine(ResultsDir, parsedLogName);
    parsedLog = File.CreateText(parsedLogPath);
    Console.WriteLine($"[INFO] Streams log → {parsedLogPath}");
}

// Per-stream console colors.
static ConsoleColor StreamColor(string stream) => stream switch
{
    "main"        => ConsoleColor.White,
    "logons"      => ConsoleColor.DarkGray,
    "talk"        => ConsoleColor.Green,
    "whispers"    => ConsoleColor.Cyan,
    "thoughts"    => ConsoleColor.Magenta,
    "familiar"    => ConsoleColor.DarkCyan,
    "atmospherics"=> ConsoleColor.DarkGreen,
    "combat"      => ConsoleColor.Red,
    "experience"  => ConsoleColor.DarkYellow,
    _             => ConsoleColor.Gray,
};

// Game text — color-coded by stream. In COMPARE mode also capture for diff.
core.GameEvents
    .OfType<TextEvent>()
    .Subscribe(e =>
    {
        var label   = e.Stream == "main" ? "" : $"[{e.Stream.ToUpper()}] ";
        var display = label + e.Text;

        parsedLog?.WriteLine(display);

        if (isCompare)
            parsedLines.Add(display);

        Console.ForegroundColor = StreamColor(e.Stream);
        Console.WriteLine(display);
        Console.ResetColor();
    });

// Vitals
core.GameEvents
    .OfType<ProgressBarEvent>()
    .Subscribe(e =>
    {
        if (e.BarId == "health2") return;
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"  {e.BarId,-15} {e.Value,3}%");
        Console.ResetColor();
    });

// Round time
core.GameEvents
    .OfType<RoundTimeEvent>()
    .Subscribe(e =>
    {
        var secs = (e.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  RT: {secs:0.0}s");
        Console.ResetColor();
    });

// Connection state
core.ConnectionState.Subscribe(e =>
{
    Console.ForegroundColor = e.Kind == ConnectionEventKind.Connected
        ? ConsoleColor.Green : ConsoleColor.DarkYellow;
    Console.WriteLine($"-- {e.Kind} {(e.Message is not null ? "— " + e.Message : "")}");
    Console.ResetColor();
});

// AI results
if (core.AiBuffer is not null)
{
    core.AiBuffer.AnalysisReady += (_, result) =>
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n═══ AI [{result.Mode}] ═══");
        Console.WriteLine(result.Response);
        Console.WriteLine("════════════════════\n");
        Console.ResetColor();
    };
}

// ── Connect ──────────────────────────────────────────────────────────────────

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Connecting ({mode})...");
try
{
    await core.ConnectAsync(connCfg, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Connection cancelled.");
    return;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FATAL] {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException is not null)
        Console.WriteLine($"        Caused by: {ex.InnerException.Message}");
    Console.ResetColor();
    return;
}
Console.WriteLine("Connected. Press Ctrl+C or type .quit to exit.");

// ── #82 var-persistence verification (opt-in: GENIE_VERIFY_VARS=1) ────────────
// Runs the exact bug repro through the fully-wired engine: a script's
// `put #var` must set the PERSISTENT user var (readable as $name + saved by
// `#var save`), not a throwaway script-local. Started here so the replay's
// prompts tick it to completion before we inspect.
var verifyVars     = Environment.GetEnvironmentVariable("GENIE_VERIFY_VARS") == "1";
var vartestEchoes  = new List<string>();
if (verifyVars)
{
    core.ScriptOutputLine += line => { if (line.Contains("VARTEST")) vartestEchoes.Add(line); };
    var vtPath = Path.Combine(core.Scripts.ScriptsDir, "vartest.cmd");
    File.WriteAllText(vtPath,
        "echo VARTEST before: [$testvar]\n" +
        "put #var testvar 80\n" +
        "put #var save\n" +
        "echo VARTEST after: [$testvar]\n");
    core.Scripts.TryStart("vartest", Array.Empty<string>());
}

// ── Replay mode: wait for server to close the connection ─────────────────────
if (mode == "REPLAY")
{
    var replayDone = new TaskCompletionSource();
    core.ConnectionState.Subscribe(e =>
    {
        if (e.Kind == ConnectionEventKind.Disconnected)
            replayDone.TrySetResult();
    });
    await Task.WhenAny(replayDone.Task, Task.Delay(60_000, cts.Token));
    Console.WriteLine("-- Replay complete.");

    if (verifyTrackers)
    {
        await Task.Delay(300);   // let any trailing prompt/render settle
        Console.WriteLine();
        Console.WriteLine("==================== TRACKER VERIFICATION ====================");
        if (trackerPanels.Count == 0)
            Console.WriteLine("(no tracker windows rendered — did the recording carry percWindow / exp / sky data?)");
        foreach (var (window, content) in trackerPanels.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"┌─ window: {window}");
            foreach (var line in content.Split('\n'))
                Console.WriteLine($"│ {line}");
            Console.WriteLine("└────");
        }

        var g = core.Scripts.Globals.ToList();   // snapshot to avoid mutation-during-enum
        void DumpGlobals(string label, Func<string, bool> match)
        {
            Console.WriteLine($"── {label} ──");
            var hits = g.Where(kv => match(kv.Key)).OrderBy(kv => kv.Key).ToList();
            if (hits.Count == 0) { Console.WriteLine("   (none)"); return; }
            foreach (var kv in hits) Console.WriteLine($"   ${kv.Key} = {kv.Value}");
        }
        DumpGlobals("SpellTimer globals",
            k => k.StartsWith("SpellTimer.", StringComparison.Ordinal));
        DumpGlobals("Experience globals",
            k => k.EndsWith(".Ranks", StringComparison.Ordinal)
              || k.EndsWith(".LearningRate", StringComparison.Ordinal)
              || k.EndsWith(".LearningRateName", StringComparison.Ordinal)
              || k == "TDPs");
        Console.WriteLine("==============================================================");
    }

    if (verifyVars)
    {
        for (int i = 0; i < 50 && core.Scripts.AnyRunning; i++) await Task.Delay(100);
        await Task.Delay(200);
        Console.WriteLine();
        Console.WriteLine("==================== VAR VERIFICATION (#82) ====================");
        if (core.Scripts.AnyRunning)
            Console.WriteLine("(! vartest script never finished — not enough replay prompts to tick it)");
        foreach (var e in vartestEchoes) Console.WriteLine($"   echo → {e}");
        Console.WriteLine($"   in-session  Variables.Store[testvar] = [{core.Variables?.Store.Get("testvar") ?? "(null)"}]");
        var vcfg = Path.Combine(core.Config.ConfigProfileDir, "variables.cfg");
        Console.WriteLine($"   variables.cfg: {vcfg}");
        Console.WriteLine($"   exists: {File.Exists(vcfg)}");
        if (File.Exists(vcfg))
            Console.WriteLine("   contents: " + File.ReadAllText(vcfg).Replace("\n", "\n             ").TrimEnd());
        try { File.Delete(Path.Combine(core.Scripts.ScriptsDir, "vartest.cmd")); } catch { }
        Console.WriteLine("================================================================");
    }

    if (verifyStreams)
    {
        await Task.Delay(200);
        Console.WriteLine();
        Console.WriteLine("==================== STREAM-ROUTING VERIFICATION ====================");
        bool LooksMain(string s) => s.Contains("runs ") || s.Contains("just arrived")
            || s.Contains("went through") || s.Contains("came through") || s.Contains("You see");
        foreach (var kv in streamBuckets.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var lines = kv.Value;
            var mainish = lines.Count(LooksMain);
            Console.WriteLine($"── stream '{kv.Key}': {lines.Count} lines"
                + (kv.Key != "main" && mainish > 0 ? $"  (!! {mainish} look like MAIN content — leak)" : ""));
            foreach (var l in lines.Take(3))               Console.WriteLine($"     first: {l}");
            if (lines.Count > 3) foreach (var l in lines.Skip(Math.Max(3, lines.Count - 2)))
                Console.WriteLine($"     last:  {l}");
        }
        Console.WriteLine("=====================================================================");
    }
    goto done;
}

// ── Interactive loop ──────────────────────────────────────────────────────────

PrintHelp();
while (!cts.IsCancellationRequested)
{
    var line = Console.ReadLine();
    if (line is null || line == ".quit") { cts.Cancel(); break; }

    switch (line)
    {
        case ".parser" when core.AiBuffer is not null:
            await core.AiBuffer.AnalyzeAsync(AiAnalysisMode.ParserAnalysis, cts.Token);
            break;

        case ".insight" when core.AiBuffer is not null:
            await core.AiBuffer.AnalyzeAsync(AiAnalysisMode.GameplayInsight, cts.Token);
            break;

        case ".drain" when core.AiBuffer is not null:
        {
            var xml = core.AiBuffer.DrainBuffer();
            Console.WriteLine($"[DRAIN] {xml.Length:N0} chars cleared from AI buffer.");
            break;
        }

        case ".help":
            PrintHelp();
            break;

        default:
            if (line.StartsWith(".ai ") && core.AiBuffer is not null)
            {
                var question = line[4..].Trim();
                Console.WriteLine("[AI] Thinking...");
                var answer = await core.AiBuffer.AskAsync(question, cts.Token);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[AI] {answer}");
                Console.ResetColor();
            }
            else if (!line.StartsWith("."))
            {
                await core.SendCommandAsync(line, cts.Token);
            }
            else
            {
                Console.WriteLine($"Unknown command: {line}. Type .help for list.");
            }
            break;
    }
}

done:
await rawLog.FlushAsync();
rawLog.Dispose();
if (parsedLog is not null)
{
    await parsedLog.FlushAsync();
    parsedLog.Dispose();
}

if (replayServer is not null)
    await replayServer.DisposeAsync();

// ── COMPARE mode output ───────────────────────────────────────────────────────
if (isCompare)
{
    var parsedPath = Path.Combine(ResultsDir, compareName + "_parsed.txt");
    await File.WriteAllLinesAsync(parsedPath, parsedLines);
    Console.WriteLine($"[COMPARE] Parsed   → {parsedPath} ({parsedLines.Count} lines)");
    PrintCompareSummary(Path.Combine(ResultsDir, compareName + "_baseline.txt"), parsedPath);
}

Console.WriteLine("Done.");

// ── Helpers ───────────────────────────────────────────────────────────────────

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- DR  <account> <password> <character>   (XML/StormFront)");
    Console.WriteLine("  dotnet run -- WIZ <account> <password> <character>   (Wizard plain-text)");
    Console.WriteLine("  dotnet run -- LICH - - -");
    Console.WriteLine("  dotnet run -- REPLAY  <session-file> [speed]    (looks in test_results/)");
    Console.WriteLine("  dotnet run -- DROP    [watchdog]                 (#87 drop-path self-test, no game)");
    Console.WriteLine("  dotnet run -- COMPARE <session-file>             (looks in test_results/)");
    Console.WriteLine("  dotnet run -- LIST    <account> <password> [gamecode]");
    Console.WriteLine("  dotnet run -- ALIGN   <xml-session> <txt-session>");
    Console.WriteLine("  dotnet run -- VERBS   [pattern]                   (scan captures → verb_catalog.md)");
    Console.WriteLine("  dotnet run -- FE_DIFF <fileA> <fileB>             (compare 2 recordings — FE:GENIE vs FE:STORM)");
    Console.WriteLine("    speed: 0=max (default), 1.0=real-time");
    Console.WriteLine();
    Console.WriteLine("ALIGN workflow:");
    Console.WriteLine("  1. Terminal A: dotnet run -- DR  acct1 pass1 CharA");
    Console.WriteLine("  2. Terminal B: dotnet run -- WIZ acct2 pass2 CharB");
    Console.WriteLine("  3. Both chars in same room — type the same commands in each terminal");
    Console.WriteLine("  4. .quit both sessions");
    Console.WriteLine("  5. dotnet run -- ALIGN raw_session_CharA_*.xml raw_session_CharB_*.txt");
}

static void PrintHelp()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  <game command>   Send to game (e.g. look, go north)");
    Console.WriteLine("  ; between cmds   Chain multiple commands on one line");
    Console.WriteLine("  .parser          AI: identify unknown XML tags");
    Console.WriteLine("  .insight         AI: gameplay summary and suggestions");
    Console.WriteLine("  .ai <question>   AI: free-form question with game context");
    Console.WriteLine("  .drain           Clear AI buffer and report size");
    Console.WriteLine("  .help            Show this list");
    Console.WriteLine("  .quit            Disconnect and exit");
}

// ── DROP mode — assert the disconnect-DETECTION path end-to-end (#87) ──────────
// Drives the real GenieCore + GameConnection + DevReplayServer stack, then checks
// that a dropped link is actually detected and surfaced: ConnectionState fires
// Disconnected and the $connected script global flips "1" → "0". The `watchdog`
// variant additionally asserts the server-activity watchdog raised an Error first.
// Returns true iff every check passed.
static async Task<bool> RunDropRepro(ConnectionConfig cfg, bool watchdog, ILoggerFactory lf)
{
    await using var core = new GenieCore(cfg.DataDirectoryOverride, null, lf);

    var disconnected     = new TaskCompletionSource();
    string? connectedVal = null, disconnectedVal = null;
    var     sawError     = false;
    string? errorMsg     = null;

    using var _ = core.ConnectionState.Subscribe(e =>
    {
        switch (e.Kind)
        {
            case ConnectionEventKind.Connected:
                connectedVal = core.Scripts.Globals.TryGetValue("connected", out var c) ? c : "(unset)";
                break;
            case ConnectionEventKind.Error:
                sawError = true; errorMsg = e.Message;
                break;
            case ConnectionEventKind.Disconnected:
                disconnectedVal = core.Scripts.Globals.TryGetValue("connected", out var d) ? d : "(unset)";
                disconnected.TrySetResult();
                break;
        }
    });

    Console.WriteLine($"[DROP] {(watchdog ? "watchdog (silent-hang)" : "clean-close")} repro — connecting via DevReplay...");
    await core.ConnectAsync(cfg);

    // Clean close arrives almost immediately; the watchdog trips after
    // ServerActivityTimeoutMs (3s here), so allow generous headroom.
    var timeoutMs = watchdog ? 15_000 : 8_000;
    var gotDrop   = await Task.WhenAny(disconnected.Task, Task.Delay(timeoutMs)) == disconnected.Task;
    await Task.Delay(50);   // let the $connected write settle

    var checks = new List<(string name, bool pass, string detail)>
    {
        ("Connected fired",             connectedVal is not null, $"$connected at connect = {connectedVal ?? "(never connected)"}"),
        ("$connected was 1 on connect", connectedVal == "1",      $"expected 1, got {connectedVal ?? "(none)"}"),
        ("Disconnected fired",          gotDrop,                  gotDrop ? "ok" : $"no Disconnected within {timeoutMs}ms"),
        ("$connected reset to 0",       disconnectedVal == "0",   $"expected 0, got {disconnectedVal ?? "(none)"}"),
    };
    if (watchdog)
        checks.Add(("Watchdog raised Error",
            sawError && (errorMsg?.Contains("no data", StringComparison.OrdinalIgnoreCase) ?? false),
            sawError ? $"Error: {errorMsg}" : "no Error event seen"));

    Console.WriteLine();
    Console.WriteLine("==================== DROP-PATH VERIFICATION (#87) ====================");
    var allPass = true;
    foreach (var (name, pass, detail) in checks)
    {
        allPass &= pass;
        Console.ForegroundColor = pass ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name,-28} — {detail}");
    }
    Console.ResetColor();
    Console.WriteLine("=====================================================================");
    Console.ForegroundColor = allPass ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine(allPass ? "[DROP] ALL CHECKS PASSED" : "[DROP] SOME CHECKS FAILED");
    Console.ResetColor();
    return allPass;
}

// ── RECONNECT mode — assert the PERSISTENT-CORE path end-to-end (#88 / #46 P3) ──
// Connects twice through DevReplay on a SINGLE GenieCore. Proves the core (and the
// public relay observables a subscriber holds) survive disconnect→reconnect: the
// subscription is taken ONCE, yet sees Connected on both connects and a TextEvent
// from the SECOND connection's fresh parser; and Scripts/Commands are the same
// instances throughout. Returns true iff every check passed.
static async Task<bool> RunReconnectRepro(string sessionFile, ILoggerFactory lf)
{
    await using var core = new GenieCore(dataDirectoryOverride: null, aiConfig: null, loggerFactory: lf);

    var scriptsBefore  = core.Scripts;
    var commandsBefore = core.Commands;

    var connectedCount = 0;
    var textOnSecond   = 0;
    var onSecond       = false;

    // Subscribe ONCE — these must survive the reconnect (the whole point).
    using var csub = core.ConnectionState.Subscribe(e =>
    {
        if (e.Kind == ConnectionEventKind.Connected) connectedCount++;
    });
    using var tsub = core.GameEvents.OfType<TextEvent>().Subscribe(_ =>
    {
        if (onSecond) textOnSecond++;
    });

    var cfg = new ConnectionConfig
    {
        Mode                 = ConnectionMode.DevReplay,
        LichProxyHost        = "127.0.0.1",
        LichProxyPort        = 8000,
        MaxReconnectAttempts = 0,
    };

    // ── Connect #1 ──
    Console.WriteLine("[RECONNECT] connect #1 via DevReplay...");
    await using (var s1 = new DevReplayServer(sessionFile, port: 8000, speed: 0,
                     hangAfterStream: false, log: lf.CreateLogger<DevReplayServer>()))
    {
        s1.Start();
        await Task.Delay(100);
        await core.ConnectAsync(cfg);
        await Task.Delay(600);   // stream + clean close
    }

    // Add a runtime rule + user variable, as if a running script created them, plus
    // a fetched live skill rank. A SAME-character reconnect (reloadRules:false,
    // clearPerCharacter:false) must preserve ALL of these — the rules/script state
    // (Change #2) and the skill ranks so the Mapper doesn't re-prompt (Change #4).
    core.Triggers.AddTrigger("reconnect-persist-test", "echo hi", false, true, "");
    core.Variables.Store.Set("reconnectpersistvar", "kept");
    core.State.LiveSkills.SetRank("Athletics", 212);

    // ── Connect #2 on the SAME core (exercises TeardownConnectionAsync + rebuild) ──
    // Same-character reconnect — runtime rules + fetched skills must survive.
    onSecond = true;
    Console.WriteLine("[RECONNECT] connect #2 via DevReplay (same core, reloadRules:false, clearPerCharacter:false)...");
    await using (var s2 = new DevReplayServer(sessionFile, port: 8000, speed: 0,
                     hangAfterStream: false, log: lf.CreateLogger<DevReplayServer>()))
    {
        s2.Start();
        await Task.Delay(100);
        await core.ConnectAsync(cfg, reloadRules: false, clearPerCharacter: false);
        await Task.Delay(600);
    }

    var trigSurvived  = core.Triggers.Triggers.Any(t => t.Pattern == "reconnect-persist-test");
    var varSurvived   = core.Variables.Store.Get("reconnectpersistvar") == "kept";
    var skillSurvived = core.State.LiveSkills.Rank("Athletics") == 212;   // Change #4: skills kept on same-char reconnect

    // Now prove clear-then-load drops them (the character-change path).
    core.ResetRuleEngines();
    var trigCleared = core.Triggers.Triggers.All(t => t.Pattern != "reconnect-persist-test");
    var varCleared  = core.Variables.Store.Get("reconnectpersistvar") is null;

    var checks = new List<(string name, bool pass, string detail)>
    {
        ("Connected fired on both connects", connectedCount >= 2,
            $"expected >= 2, got {connectedCount}"),
        ("Text relayed on 2nd connection",   textOnSecond >= 1,
            $"TextEvents after reconnect = {textOnSecond} (proves relay re-feed)"),
        ("ScriptEngine instance persisted",  ReferenceEquals(scriptsBefore, core.Scripts),
            ReferenceEquals(scriptsBefore, core.Scripts) ? "same instance" : "REBUILT (lost script state!)"),
        ("CommandEngine instance persisted", ReferenceEquals(commandsBefore, core.Commands),
            ReferenceEquals(commandsBefore, core.Commands) ? "same instance" : "REBUILT"),
        ("Same-char reconnect kept rules",   trigSurvived && varSurvived,
            $"trigger kept={trigSurvived}, var kept={varSurvived} (reloadRules:false)"),
        ("Same-char reconnect kept skills",  skillSurvived,
            skillSurvived ? "Athletics=212 survived (Change #4 — no Mapper re-prompt)" : "skills WIPED on reconnect!"),
        ("ResetRuleEngines clears rules",    trigCleared && varCleared,
            $"trigger cleared={trigCleared}, user var cleared={varCleared}"),
    };

    Console.WriteLine();
    Console.WriteLine("============== PERSISTENT-CORE VERIFICATION (#88 / #46 P3) ==============");
    var allPass = true;
    foreach (var (name, pass, detail) in checks)
    {
        allPass &= pass;
        Console.ForegroundColor = pass ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name,-34} — {detail}");
    }
    Console.ResetColor();
    Console.WriteLine("========================================================================");
    Console.ForegroundColor = allPass ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine(allPass ? "[RECONNECT] ALL CHECKS PASSED" : "[RECONNECT] SOME CHECKS FAILED");
    Console.ResetColor();
    return allPass;
}

// ── MAPCOALESCE — assert PR #92's room-placement coalescing end-to-end (#91 Bug 1) ──
// Feeds the REAL DrXmlParser → GameStateEngine → MapperGameStateAdapter the exact
// incoherent sequence from issue #91 (room B's TITLE arrives before B's compass,
// while exits still hold room A's value) and asserts the adapter now fires
// StateChanged ONCE per room — on the <prompt> — with a COHERENT title+exits
// snapshot, instead of firing per-component (which produced a B-title + A-exits
// fingerprint that missed every zone). No live game / no map data needed.
static bool RunMapCoalesceRepro(ILoggerFactory lf)
{
    var parser  = new Genie.Core.Parser.DrXmlParser(lf.CreateLogger<Genie.Core.Parser.DrXmlParser>());
    var state   = new Genie.Core.Models.GameState();
    // GameStateEngine subscribes BEFORE the adapter (same order as GenieCore) so
    // _state is updated before the adapter reads it on the prompt.
    var engine  = new Genie.Core.GameState.GameStateEngine(
        parser.GameEvents, state, lf.CreateLogger<Genie.Core.GameState.GameStateEngine>());
    var adapter = new Genie.Core.Mapper.MapperGameStateAdapter(state, parser.GameEvents);

    var fires = new List<(string title, string exits)>();
    adapter.StateChanged += () => fires.Add((adapter.RoomTitle, string.Join(" ", adapter.Exits)));

    // Room A: title → exits "out" → prompt.
    parser.Feed("<streamWindow id='room' title='Room' subtitle=\" - [Test, Room A]\" location='center'/>\n");
    parser.Feed("<compass><dir value=\"out\"/></compass>\n");
    var firesBeforePromptA = fires.Count;                 // expect 0 (coalesced; old fired per-component)
    parser.Feed("<prompt time=\"1\">&gt;</prompt>\n");
    var firesAfterPromptA  = fires.Count;                 // expect 1

    // Room B: TITLE arrives while exits still hold A's "out" ...
    parser.Feed("<streamWindow id='room' title='Room' subtitle=\" - [Test, Room B]\" location='center'/>\n");
    var firesAfterTitleB   = fires.Count;                 // expect 1 (old fired here INCOHERENTLY: B-title + "out")
    // ... then B's own compass ...
    parser.Feed("<compass><dir value=\"north\"/><dir value=\"east\"/></compass>\n");
    var firesAfterCompassB = fires.Count;                 // expect 1
    // ... then the prompt that ends B's turn.
    parser.Feed("<prompt time=\"2\">&gt;</prompt>\n");
    var firesAfterPromptB  = fires.Count;                 // expect 2

    var bCoherent = fires.Count >= 2
        && fires[1].title.Contains("Room B", StringComparison.OrdinalIgnoreCase)
        && fires[1].exits == "north east";
    var noIncoherent = !fires.Any(f =>
        f.title.Contains("Room B", StringComparison.OrdinalIgnoreCase) && f.exits == "out");

    var checks = new List<(string name, bool pass, string detail)>
    {
        ("Coalesced: no fire before room A's prompt", firesBeforePromptA == 0, $"fires before prompt = {firesBeforePromptA}"),
        ("Fired once on room A's prompt",             firesAfterPromptA  == 1, $"fires = {firesAfterPromptA}"),
        ("Coalesced: B's title alone did NOT fire",   firesAfterTitleB   == 1, $"fires after B-title = {firesAfterTitleB} (old fired here, incoherently)"),
        ("Coalesced: B's compass alone did NOT fire", firesAfterCompassB == 1, $"fires after B-compass = {firesAfterCompassB}"),
        ("Fired once on room B's prompt",             firesAfterPromptB  == 2, $"fires = {firesAfterPromptB}"),
        ("Room B snapshot COHERENT (title+own exits)", bCoherent, fires.Count >= 2 ? $"fire#2 = (\"{fires[1].title}\", \"{fires[1].exits}\")" : "no 2nd fire"),
        ("No incoherent B-title + A-exits fire ever", noIncoherent, "the exact bug #92 fixes"),
    };

    Console.WriteLine();
    Console.WriteLine("=========== MAPPER COALESCE VERIFICATION (PR #92 / #91 Bug 1) ===========");
    var allPass = true;
    foreach (var (name, pass, detail) in checks)
    {
        allPass &= pass;
        Console.ForegroundColor = pass ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name,-44} — {detail}");
    }
    Console.ResetColor();
    Console.WriteLine($"  recorded fires: {string.Join(" | ", fires.Select(f => $"(\"{f.title}\",\"{f.exits}\")"))}");
    Console.WriteLine("========================================================================");
    Console.ForegroundColor = allPass ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine(allPass ? "[MAPCOALESCE] ALL CHECKS PASSED" : "[MAPCOALESCE] SOME CHECKS FAILED");
    Console.ResetColor();

    adapter.Dispose();
    engine.Dispose();
    parser.Dispose();
    return allPass;
}

// ── MOVEORDER — demonstrate F1: PR #92's prompt-handler ordering vs the safe order ──
// PR #92's PromptEvent handler runs Scripts.OnPrompt() BEFORE the coalesced
// Scripts.OnRoomChanged(). Tick() runs scripts forward-until-block, so a
// pause-resumed script reaches a `move` INSIDE OnPrompt()'s tick (sends the command,
// parks PauseMode.Move); the trailing OnRoomChanged() then unblocks that move the
// SAME turn — before the room actually changed in response. This drives a real
// ScriptEngine both ways and shows the safe order (OnRoomChanged BEFORE OnPrompt)
// leaves the move correctly parked. PASS = the bug reproduces under #92's order AND
// the flip fixes it (so flipping GenieCore's PromptEvent handler is warranted).
static bool RunMoveOrderRepro(ILoggerFactory lf)
{
    var scriptsDir = Path.Combine(ResultsDir, "moveorder_scripts");
    Directory.CreateDirectory(scriptsDir);
    // pause expires this turn → resume → `move east` (sends + parks) → echo (only
    // reached if the move unblocks). Script gone from Instances == ran past the move.
    File.WriteAllText(Path.Combine(scriptsDir, "mv.cmd"),
        "pause 0.05\nmove east\necho reached-after-move\n");

    // Run one turn with the given order; returns a trace of what happened.
    static (bool parked, int eastCount, bool reachedEcho) RunOnce(string scriptsDir, bool promptFirst)
    {
        var sent   = new List<string>();
        var echoed = new List<string>();
        var ta     = new Genie.Core.Scripting.TypeAheadSession { Limit = 3 };  // ample budget so `move` sends
        var se     = new Genie.Core.Scripting.ScriptEngine(scriptsDir, ta,
            sendCommand: c => sent.Add(c),
            echo:        e => echoed.Add(e));
        se.InRoundtime               = () => false;
        se.RoundTimeRemainingSeconds = () => 0;
        se.SpellTimeSeconds          = () => 0;

        se.TryStart("mv", System.Array.Empty<string>());   // ticks to the `pause`
        System.Threading.Thread.Sleep(120);                 // let `pause 0.05` expire

        if (promptFirst) { se.OnPrompt(); se.OnRoomChanged(); }   // PR #92's order
        else             { se.OnRoomChanged(); se.OnPrompt(); }   // safe order

        var parked      = se.Instances.Count > 0;   // still present == parked at move; gone == ran past it
        var eastCount   = sent.Count(c => c.Equals("east", StringComparison.OrdinalIgnoreCase));
        var reachedEcho = echoed.Any(e => e.Contains("reached-after-move", StringComparison.OrdinalIgnoreCase));
        se.StopAll();
        return (parked, eastCount, reachedEcho);
    }

    var pf = RunOnce(scriptsDir, promptFirst: true);
    var rf = RunOnce(scriptsDir, promptFirst: false);

    Console.WriteLine($"  trace #92-order  : parked={pf.parked}  eastSent={pf.eastCount}  reachedPostMoveEcho={pf.reachedEcho}");
    Console.WriteLine($"  trace safe-order : parked={rf.parked}  eastSent={rf.eastCount}  reachedPostMoveEcho={rf.reachedEcho}");

    // F1 = the script runs PAST the move (reaches the post-move echo) the SAME turn
    // under #92's OnPrompt-first order, but correctly waits under the safe
    // OnRoomChanged-first order. The test PASSES when it demonstrates exactly that
    // (which is why GenieCore's PromptEvent handler must use the safe order).
    var f1Repros = pf.reachedEcho && !rf.reachedEcho;

    var checks = new List<(string name, bool pass, string detail)>
    {
        ("`move east` was sent in both orders",            pf.eastCount > 0 && rf.eastCount > 0, "sanity: the move ran"),
        ("Unsafe order (#92, OnPrompt-first) unblocks early", pf.reachedEcho,  pf.reachedEcho ? "script ran PAST the move same turn — F1" : "did not repro"),
        ("Safe order (OnRoomChanged-first) waits at move",    !rf.reachedEcho, rf.reachedEcho ? "ran past move (unexpected)" : "correctly parked at the move"),
    };

    Console.WriteLine();
    Console.WriteLine("============ MOVE-UNBLOCK ORDERING (F1, PR #92 PromptEvent) ============");
    var allPass = true;
    foreach (var (name, pass, detail) in checks)
    {
        allPass &= pass;
        Console.ForegroundColor = pass ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name,-46} — {detail}");
    }
    Console.ResetColor();
    Console.WriteLine("=======================================================================");
    Console.ForegroundColor = allPass ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine(allPass
        ? "[MOVEORDER] F1 demonstrated — GenieCore's PromptEvent handler must call\n" +
          "            OnRoomChanged() BEFORE OnPrompt() (the safe order), which it now does."
        : "[MOVEORDER] unexpected outcome — see trace above.");
    Console.ResetColor();
    return allPass;
}

// Pathfinder gating contract — no game, no map. Asserts ExitRequirement.IsMet
// treats UNKNOWN class (null) / level (0) / skill (not in store) as "assumed
// reachable" (so the pathfinder doesn't return a false "No path" before stats
// are read), while still BLOCKING known-and-failing gates.
static bool RunReqGateRepro()
{
    var classGate = Genie.Core.Mapper.ExitRequirement.Parse("class=Thief");
    var levelGate = Genie.Core.Mapper.ExitRequirement.Parse("level>=25");
    var skillGate = Genie.Core.Mapper.ExitRequirement.Parse("Climbing>=50");

    var lowSkills = new Genie.Core.Skills.SkillStore();   // known: can't climb
    lowSkills.SetRank("Climbing", 10);
    var hiSkills = new Genie.Core.Skills.SkillStore();    // known: can climb
    hiSkills.SetRank("Climbing", 80);
    var emptySkills = new Genie.Core.Skills.SkillStore(); // store present but rank unseen

    var checks = new List<(string name, bool pass, string detail)>
    {
        // UNKNOWN → PASS (the fix; these returned false before, causing "No path").
        ("class gate passes when class UNKNOWN (null)",  classGate.IsMet(null, null, 0),                "assumed reachable"),
        ("level gate passes when level UNKNOWN (0)",     levelGate.IsMet(null, null, 0),                "assumed reachable"),
        ("skill gate passes when skills UNKNOWN (null)", skillGate.IsMet(null, null, 0),                "assumed reachable"),
        ("skill gate passes when rank unseen in store",  skillGate.IsMet(emptySkills, "Thief", 50),     "unknown skill → pass"),

        // KNOWN-and-failing → BLOCK (no over-permissive regression).
        ("class gate blocks a KNOWN wrong class",        !classGate.IsMet(null, "Empath", 50),          "class known + mismatch"),
        ("level gate blocks a KNOWN low level",          !levelGate.IsMet(null, "Thief", 10),           "10 < 25"),
        ("skill gate blocks a KNOWN low rank",           !skillGate.IsMet(lowSkills, "Thief", 50),      "Climbing 10 < 50"),

        // KNOWN-and-passing → PASS.
        ("class gate passes the KNOWN right class",      classGate.IsMet(null, "Thief", 50),            "class match"),
        ("level gate passes a KNOWN high level",         levelGate.IsMet(null, "Thief", 50),            "50 >= 25"),
        ("skill gate passes a KNOWN high rank",          skillGate.IsMet(hiSkills, "Thief", 50),        "Climbing 80 >= 50"),
    };

    Console.WriteLine();
    Console.WriteLine("============ EXIT-REQUIREMENT GATING (pathfinder No-path fix) ============");
    var allPass = true;
    foreach (var (name, pass, detail) in checks)
    {
        allPass &= pass;
        Console.ForegroundColor = pass ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name,-48} — {detail}");
    }
    Console.ResetColor();
    Console.WriteLine("=========================================================================");
    Console.ForegroundColor = allPass ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine(allPass
        ? "[REQGATE] unknown class/level/skill assumed-reachable (no false 'No path');\n" +
          "          known-and-failing gates still block. Contract honored."
        : "[REQGATE] gating contract violated — see failures above.");
    Console.ResetColor();
    return allPass;
}

// Resolves a session file path. Tries current dir first, then resultsDir/.
static string? ResolveSessionFile(string given, string resultsDir)
{
    if (File.Exists(given))
        return given;

    if (!Path.IsPathRooted(given) && !given.Contains(Path.DirectorySeparatorChar)
                                  && !given.Contains(Path.AltDirectorySeparatorChar))
    {
        var candidate = Path.Combine(resultsDir, given);
        if (File.Exists(candidate))
            return candidate;
    }

    Console.WriteLine($"Session file not found: {given}");
    Console.WriteLine($"  checked: {given}");
    Console.WriteLine($"  checked: {Path.Combine(resultsDir, given)}");
    return null;
}

// ── Baseline generators ───────────────────────────────────────────────────────

// Produces a clean line list from a StormFront XML session file.
// Skips the initial settings dump, strips XML tags, decodes entities.
static List<string> GenerateXmlBaseline(string filePath)
{
    var allText = File.ReadAllText(filePath);

    // Skip the initial Wrayth settings dump (everything up to <settingsInfo/>).
    var siIdx = allText.IndexOf("<settingsInfo", StringComparison.OrdinalIgnoreCase);
    if (siIdx >= 0)
    {
        var tagEnd = allText.IndexOf('>', siIdx);
        if (tagEnd >= 0) allText = allText[(tagEnd + 1)..];
    }

    // Remove <component ...>...</component> blocks — DR dual-encodes room text;
    // the component form would create false "only in baseline" noise.
    allText = Regex.Replace(allText,
        @"<component\b[^>]*>.*?</component>",
        string.Empty,
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    var promptRe = new Regex(@"^[A-Z]*>$");
    var result   = new List<string>();

    foreach (var rawLine in allText.Split('\n'))
    {
        var stripped = Regex.Replace(rawLine, "<[^>]+>", " ");
        stripped = WebUtility.HtmlDecode(stripped);
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim();

        if (stripped.Length == 0)       continue;
        if (promptRe.IsMatch(stripped)) continue;

        result.Add(stripped);
    }

    return result;
}

// Produces a clean line list from a Wizard plain-text session file.
// Handles: initial XML settings dump (line 1 starts with '<'), ANSI escape
// sequences ([1m, [0m), blank lines, and prompt prefixes (">" or "COMMAND>").
// Prompt prefixes are stripped and the remaining content is kept; pure prompt
// lines (nothing after ">") are dropped.
static List<string> ReadTxtBaseline(string filePath)
{
    var ansiRe   = new Regex(@"\x1b?\[[0-9;]*m");
    var promptRe = new Regex(@"^[A-Z]*>");
    var result   = new List<string>();

    foreach (var rawLine in File.ReadAllLines(filePath))
    {
        var line = ansiRe.Replace(rawLine, "").Trim();

        if (line.Length == 0)     continue;
        if (line.StartsWith('<')) continue;  // XML settings dump on line 1

        // Strip prompt prefix ">" or "COMMAND>" and keep whatever follows.
        if (promptRe.IsMatch(line))
        {
            line = promptRe.Replace(line, "").TrimStart();
            if (line.Length == 0) continue;
        }

        result.Add(line);
    }

    return result;
}

// ── COMPARE diff summary ──────────────────────────────────────────────────────

static void PrintCompareSummary(string baselinePath, string parsedPath)
{
    var baseline       = File.ReadAllLines(baselinePath).ToHashSet(StringComparer.Ordinal);
    var parsed         = new HashSet<string>(File.ReadAllLines(parsedPath), StringComparer.Ordinal);
    var onlyInBaseline = baseline.Where(l => !parsed.Contains(l)).OrderBy(l => l).ToList();
    var onlyInParsed   = parsed.Where(l => !baseline.Contains(l)).OrderBy(l => l).ToList();

    Console.WriteLine();
    Console.WriteLine($"══ COMPARE summary ════════════════════════════════");
    Console.WriteLine($"  Baseline unique lines : {onlyInBaseline.Count,5}  (text the parser may be dropping)");
    Console.WriteLine($"  Parsed unique lines   : {onlyInParsed.Count,5}  (text the parser adds or reformats)");

    if (onlyInBaseline.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n── Only in baseline (first 15) ──────────────────────");
        foreach (var l in onlyInBaseline.Take(15))
            Console.WriteLine($"  - {l}");
        Console.ResetColor();
    }

    if (onlyInParsed.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n── Only in parsed (first 15) ────────────────────────");
        foreach (var l in onlyInParsed.Take(15))
            Console.WriteLine($"  + {l}");
        Console.ResetColor();
    }

    Console.WriteLine($"════════════════════════════════════════════════════");
}

// ── VERBS mode — scan captures for <d> / <d cmd> link occurrences ─────────────

/// <summary>
/// Walk every XML file in <paramref name="dir"/> matching <paramref name="pattern"/>,
/// extract all <c>&lt;d&gt;</c> and <c>&lt;d cmd&gt;</c> occurrences with their
/// surrounding context (the enclosing stream/preset, 60 chars on each side),
/// canonicalise verbs by collapsing <c>#NNNN</c> item-ids into <c>#N</c>, and
/// emit a markdown catalog to <paramref name="outFile"/>.
/// <para>
/// Operates on the raw bytes (not the parser) on purpose — the goal is to
/// audit what the server actually emits, so any parser bugs don't get mixed
/// into the picture.
/// </para>
/// Returns (FileCount, OccurrenceCount, UniqueCount).
/// </summary>
static (int FileCount, int OccurrenceCount, int UniqueCount) GenerateVerbCatalog(string dir, string pattern, string outFile)
{
    var files = Directory.GetFiles(dir, pattern)
                         .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f)
                         .ToArray();

    if (files.Length == 0)
    {
        Console.WriteLine($"[VERBS] No matching .xml files in {dir} (pattern: {pattern})");
        return (0, 0, 0);
    }

    // Matches:
    //   <d>label</d>             — bare-text; display IS the command
    //   <d cmd="X">label</d>     — double-quoted cmd attribute
    //   <d cmd='X'>label</d>     — single-quoted cmd attribute (DR's actual form)
    // Group 1: double-quoted cmd. Group 2: single-quoted cmd. Group 3: label.
    var dRe = new Regex(
        @"<d(?:\s+cmd=(?:""([^""]*)""|'([^']*)'))?>([^<]+)</d>",
        RegexOptions.Compiled);

    // Looks back into a 2000-char window before each <d> match for the
    // nearest pushStream/streamWindow declaration — gives us a rough
    // "this verb appears inside the <inv>/<room>/<experience>/... stream"
    // attribution for free.
    var streamCtxRe = new Regex(
        @"<(?:pushStream|streamWindow)\s+[^>]*id=['""]([^'""]+)['""]",
        RegexOptions.Compiled);

    var occurrences = new List<(string File, int Offset, string? Cmd, string Label, string Context, string? StreamCtx)>();
    var perFile     = new Dictionary<string, int>();

    foreach (var f in files)
    {
        var content = File.ReadAllText(f);
        var fname   = Path.GetFileName(f);

        foreach (Match m in dRe.Matches(content))
        {
            var cmd = m.Groups[1].Success ? m.Groups[1].Value
                    : m.Groups[2].Success ? m.Groups[2].Value
                    : null;
            var label = m.Groups[3].Value;

            // Context: 60 chars before, 60 chars after — normalised for one-line display.
            var ctxStart = Math.Max(0, m.Index - 60);
            var ctxEnd   = Math.Min(content.Length, m.Index + m.Length + 60);
            var ctx      = content.Substring(ctxStart, ctxEnd - ctxStart)
                                 .Replace('\n', ' ')
                                 .Replace('\r', ' ')
                                 .Trim();

            // Enclosing stream: last pushStream/streamWindow declared before this match.
            string? streamCtx = null;
            var lookbackStart = Math.Max(0, m.Index - 2000);
            var lookback = content.Substring(lookbackStart, m.Index - lookbackStart);
            var streamMatches = streamCtxRe.Matches(lookback);
            if (streamMatches.Count > 0)
                streamCtx = streamMatches[^1].Groups[1].Value;

            occurrences.Add((fname, m.Index, cmd, label, ctx, streamCtx));
            perFile[fname] = (perFile.TryGetValue(fname, out var n) ? n : 0) + 1;
        }
    }

    // Canonicalize: collapse #NNNN → #N so all item-id verbs (get #37634685,
    // get #37634686, …) fold into a single "get #N" bucket.
    static string Canon(string? cmd, string label)
    {
        var c = cmd ?? label;
        return Regex.Replace(c, @"#\d+", "#N");
    }

    var grouped = occurrences
        .GroupBy(o => Canon(o.Cmd, o.Label))
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var withCmdNotLabel = grouped.Count(g => g.Any(o => o.Cmd is not null && o.Cmd != o.Label));
    var bareText        = grouped.Count(g => g.All(o => o.Cmd is null));
    var withItemId      = grouped.Count(g => g.Any(o => o.Cmd?.Contains('#') == true));

    var sb = new StringBuilder();
    sb.AppendLine("# DR Verb Catalog");
    sb.AppendLine();
    sb.AppendLine($"_Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss} by `dotnet run -- VERBS`._");
    sb.AppendLine();
    sb.AppendLine("## Summary");
    sb.AppendLine();
    sb.AppendLine($"- **Source files**: {files.Length} `.xml` capture(s) in `{Path.GetFileName(dir.TrimEnd('/', '\\'))}/`");
    sb.AppendLine($"- **Total `<d>` occurrences**: {occurrences.Count}");
    sb.AppendLine($"- **Unique canonical verbs**: {grouped.Length}");
    sb.AppendLine($"- **`<d cmd>` where cmd ≠ label**: {withCmdNotLabel} verb(s)");
    sb.AppendLine($"- **Bare `<d>` (no cmd attribute)**: {bareText} verb(s)");
    sb.AppendLine($"- **Contains item ID `#NNNN`**: {withItemId} verb(s)");
    sb.AppendLine();

    sb.AppendLine("## Per-file `<d>` counts");
    sb.AppendLine();
    foreach (var kv in perFile.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
        sb.AppendLine($"- `{kv.Key}` — {kv.Value}");
    sb.AppendLine();

    sb.AppendLine("## Verbs by frequency");
    sb.AppendLine();
    foreach (var g in grouped)
    {
        var first = g.First();
        var shape = first.Cmd is null
            ? "bare `<d>` (display text IS the command sent)"
            : first.Cmd == first.Label
                ? "`<d cmd>` where cmd == label"
                : "`<d cmd>` where cmd ≠ label";
        var hasIds = g.Any(o => o.Cmd?.Contains('#') == true);

        sb.AppendLine($"### `{g.Key}` — {g.Count()} occurrence{(g.Count() == 1 ? "" : "s")}");
        sb.AppendLine();
        sb.AppendLine($"- **Shape**: {shape}{(hasIds ? "; **contains item ID `#NNNN`**" : "")}");
        sb.AppendLine($"- **Sample cmd**: `{first.Cmd ?? "(none)"}`");
        sb.AppendLine($"- **Sample label**: `{first.Label}`");

        var streams = g.Where(o => o.StreamCtx is not null)
                       .Select(o => o.StreamCtx!)
                       .Distinct()
                       .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                       .ToArray();
        if (streams.Length > 0)
            sb.AppendLine($"- **Stream context**: {string.Join(", ", streams.Select(s => "`" + s + "`"))}");

        var fileGroups = g.GroupBy(o => o.File)
                          .OrderByDescending(fg => fg.Count())
                          .Select(fg => $"`{fg.Key}` ({fg.Count()})")
                          .ToArray();
        sb.AppendLine($"- **Files**: {string.Join(", ", fileGroups)}");
        sb.AppendLine();

        // Show up to 3 sample contexts. For verbs with item-IDs, show the
        // distinct cmd values so we can see what server-side IDs looked like
        // in practice.
        if (hasIds)
        {
            var distinctCmds = g.Select(o => o.Cmd).Where(c => c is not null).Distinct().Take(5);
            sb.AppendLine("**Distinct cmd values seen** (up to 5):");
            sb.AppendLine();
            foreach (var c in distinctCmds)
                sb.AppendLine($"- `{c}`");
            sb.AppendLine();
        }

        sb.AppendLine("**Sample contexts** (up to 3):");
        sb.AppendLine();
        foreach (var o in g.Take(3))
        {
            sb.AppendLine("```");
            sb.AppendLine($"{o.File}@{o.Offset}: …{o.Context}…");
            sb.AppendLine("```");
        }
        sb.AppendLine();
    }

    File.WriteAllText(outFile, sb.ToString());
    return (files.Length, occurrences.Count, grouped.Length);
}

// ── FE_DIFF mode — compare two recordings for FE:GENIE vs FE:STORM A/B ────────

/// <summary>
/// Compare two XML recordings tag-by-tag and write a markdown diff report.
/// The convention is fileA = baseline (FE:GENIE), fileB = treatment (FE:STORM),
/// but the diff is symmetric — it just calls them A and B.
///
/// Compares: tag-name frequencies, attribute presence on common tags,
/// component IDs, stream IDs (push/streamWindow), progressBar IDs, indicator
/// IDs, preset IDs, dialog/dialogData IDs, &lt;d&gt; link counts (the key
/// FE-gating hypothesis), &lt;a href&gt; URL counts, distinct &lt;d cmd&gt; values.
///
/// Pure regex over raw bytes — same approach as VERBS mode, so any parser
/// bugs don't taint the comparison.
/// </summary>
static void GenerateFeDiff(string fileA, string fileB, string outFile)
{
    var contentA = File.ReadAllText(fileA);
    var contentB = File.ReadAllText(fileB);

    static Dictionary<string,int> TagCounts(string content)
    {
        var dict = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(content, @"<([a-zA-Z][a-zA-Z0-9]*)"))
            dict[m.Groups[1].Value] = dict.TryGetValue(m.Groups[1].Value, out var n) ? n + 1 : 1;
        return dict;
    }

    static SortedSet<string> AttrValuesFor(string content, string tag, string attr)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        // Match <tag ... attr='value'> or <tag ... attr="value">
        var re = new Regex($@"<{tag}\b[^>]*\b{attr}=['""]([^'""]+)['""]",
                           RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (Match m in re.Matches(content)) set.Add(m.Groups[1].Value);
        return set;
    }

    static int CountLinks(string content, bool urlVariant)
    {
        var re = urlVariant
            ? new Regex(@"<a\s+[^>]*\bhref=", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            : new Regex(@"<d\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return re.Matches(content).Count;
    }

    var tagsA = TagCounts(contentA);
    var tagsB = TagCounts(contentB);
    var allTags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var k in tagsA.Keys) allTags.Add(k);
    foreach (var k in tagsB.Keys) allTags.Add(k);

    var componentsA = AttrValuesFor(contentA, "component", "id");
    var componentsB = AttrValuesFor(contentB, "component", "id");

    var streamsA = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var s in AttrValuesFor(contentA, "pushStream",   "id")) streamsA.Add(s);
    foreach (var s in AttrValuesFor(contentA, "streamWindow", "id")) streamsA.Add(s);
    var streamsB = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var s in AttrValuesFor(contentB, "pushStream",   "id")) streamsB.Add(s);
    foreach (var s in AttrValuesFor(contentB, "streamWindow", "id")) streamsB.Add(s);

    var indicatorsA = AttrValuesFor(contentA, "indicator", "id");
    var indicatorsB = AttrValuesFor(contentB, "indicator", "id");

    var presetsA = AttrValuesFor(contentA, "preset", "id");
    var presetsB = AttrValuesFor(contentB, "preset", "id");

    var dialogsA = AttrValuesFor(contentA, "dialogData", "id");
    var dialogsB = AttrValuesFor(contentB, "dialogData", "id");

    var dCmdsA = AttrValuesFor(contentA, "d", "cmd");
    var dCmdsB = AttrValuesFor(contentB, "d", "cmd");

    var dCountA = CountLinks(contentA, urlVariant: false);
    var dCountB = CountLinks(contentB, urlVariant: false);
    var aCountA = CountLinks(contentA, urlVariant: true);
    var aCountB = CountLinks(contentB, urlVariant: true);

    var sb = new StringBuilder();
    var nameA = Path.GetFileName(fileA);
    var nameB = Path.GetFileName(fileB);

    sb.AppendLine("# FE:GENIE vs FE:STORM Diff");
    sb.AppendLine();
    sb.AppendLine($"_Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}._");
    sb.AppendLine();
    sb.AppendLine($"- **A** (baseline): `{nameA}` ({new FileInfo(fileA).Length:N0} bytes)");
    sb.AppendLine($"- **B** (treatment): `{nameB}` ({new FileInfo(fileB).Length:N0} bytes)");
    sb.AppendLine();
    sb.AppendLine("> Convention for FE testing: A = FE:GENIE, B = FE:STORM. " +
                  "If the hypothesis holds, B should have more `<d>` links and possibly different tags.");
    sb.AppendLine();

    sb.AppendLine("## Key link counts");
    sb.AppendLine();
    sb.AppendLine("| Element | A | B | Δ |");
    sb.AppendLine("|---|---:|---:|---:|");
    sb.AppendLine($"| `<d>` clickable links (game commands) | {dCountA} | {dCountB} | {dCountB - dCountA:+0;-0;0} |");
    sb.AppendLine($"| `<a href>` URL links | {aCountA} | {aCountB} | {aCountB - aCountA:+0;-0;0} |");
    sb.AppendLine();
    if (dCountB > dCountA)
        sb.AppendLine("**Δ`<d>` > 0 supports the hypothesis: STORM gets richer clickable markup.**");
    else if (dCountB < dCountA)
        sb.AppendLine("**Δ`<d>` < 0 contradicts the hypothesis: GENIE got more clickable markup this run.**");
    else
        sb.AppendLine("**Δ`<d>` = 0: identical link count — hypothesis neither confirmed nor disconfirmed by this metric.**");
    sb.AppendLine();

    sb.AppendLine("## Tag frequency comparison");
    sb.AppendLine();
    sb.AppendLine("Tags appearing in either recording. Δ > 0 = more in B; Δ < 0 = more in A.");
    sb.AppendLine();
    sb.AppendLine("| Tag | A | B | Δ |");
    sb.AppendLine("|---|---:|---:|---:|");
    foreach (var tag in allTags)
    {
        var a = tagsA.GetValueOrDefault(tag, 0);
        var b = tagsB.GetValueOrDefault(tag, 0);
        if (a == b && a == 0) continue;
        var marker = a == 0 ? " **(B-only)**" : b == 0 ? " **(A-only)**" : "";
        sb.AppendLine($"| `<{tag}>` | {a} | {b} | {b - a:+0;-0;0}{marker} |");
    }
    sb.AppendLine();

    void WriteSetDiff(string title, SortedSet<string> a, SortedSet<string> b)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        var onlyA = a.Except(b, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        var onlyB = b.Except(a, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        var both  = a.Intersect(b, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        sb.AppendLine($"- **Only in A**: {(onlyA.Length == 0 ? "(none)" : string.Join(", ", onlyA.Select(s => $"`{s}`")))}");
        sb.AppendLine($"- **Only in B**: {(onlyB.Length == 0 ? "(none)" : string.Join(", ", onlyB.Select(s => $"`{s}`")))}");
        sb.AppendLine($"- **Common ({both.Length})**: {(both.Length == 0 ? "(none)" : string.Join(", ", both.Select(s => $"`{s}`")))}");
        sb.AppendLine();
    }

    WriteSetDiff("Component IDs",                componentsA, componentsB);
    WriteSetDiff("Stream IDs (push + streamWindow)", streamsA, streamsB);
    WriteSetDiff("Indicator IDs",                indicatorsA, indicatorsB);
    WriteSetDiff("Preset IDs",                   presetsA,    presetsB);
    WriteSetDiff("Dialog/dialogData IDs",        dialogsA,    dialogsB);

    sb.AppendLine("## `<d cmd>` distinct values");
    sb.AppendLine();
    var dOnlyA = dCmdsA.Except(dCmdsB, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    var dOnlyB = dCmdsB.Except(dCmdsA, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    sb.AppendLine($"- Distinct in A only ({dOnlyA.Length}):");
    foreach (var c in dOnlyA.Take(40)) sb.AppendLine($"  - `{c}`");
    if (dOnlyA.Length > 40) sb.AppendLine($"  - ... ({dOnlyA.Length - 40} more)");
    sb.AppendLine($"- Distinct in B only ({dOnlyB.Length}):");
    foreach (var c in dOnlyB.Take(40)) sb.AppendLine($"  - `{c}`");
    if (dOnlyB.Length > 40) sb.AppendLine($"  - ... ({dOnlyB.Length - 40} more)");
    sb.AppendLine();

    sb.AppendLine("## How to read this report");
    sb.AppendLine();
    sb.AppendLine("- **`<d>` count is the headline number.** The hypothesis is that DR sends more clickable links to STORM clients. A positive Δ here is the strongest signal.");
    sb.AppendLine("- **B-only tags/IDs are the discovery.** Anything that appears in STORM and not in GENIE is FE-gated markup we wouldn't see at all on the default identifier.");
    sb.AppendLine("- **Caveat**: the comparison is only as good as the actions taken. Run the SAME verb sequence in both recordings (e.g. `.verb_xml_walk`) for the diff to be meaningful. Different actions produce different XML regardless of FE.");
    sb.AppendLine("- **Caveat 2**: server-side state (NPCs in room, time of day, weather, who's online) drifts between recordings. Small Δ values may just be noise. Look for systematic patterns, not single-tag deltas.");

    File.WriteAllText(outFile, sb.ToString());
}
