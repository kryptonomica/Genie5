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

await using var core = new GenieCore(connCfg, aiCfg, loggerFactory);

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
    await core.ConnectAsync(cts.Token);
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
