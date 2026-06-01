using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Genie.Plugins;

namespace Genie.Plugins.InventoryView;

/// <summary>
/// The V5 port of GenieClient/Plugin_InventoryView (Etherian's Genie 4 plugin,
/// v1.8). Catalogs the items each character owns — on their person, in a vault, a
/// deed register, a home, and Trader storage — into a per-character tree persisted
/// to <c>InventoryView.xml</c>, and lets you search that catalog across characters.
///
/// <para><b>What carried over 1:1</b> — the scan state machine. <c>/iv scan</c> walks
/// the same sequence the Genie 4 plugin did (inventory → vault book → deed register →
/// home → Trader storage book), parsing each list by leading-space indentation into a
/// nested item tree, and saving on completion. The trigger strings and the commands
/// it sends to the game are unchanged, so it behaves identically against DR.</para>
///
/// <para><b>What changed for Genie 5</b>:
/// <list type="bullet">
/// <item><b>No WinForms window.</b> Genie 5 plugins are UI-free and talk to the host
/// through the named-window / echo seam, so the TreeView form is replaced by text
/// commands: <c>/iv open</c> prints the catalog, <c>/iv search &lt;text&gt;</c> lists
/// matches by full path. Output goes to the main window via <see cref="IPluginHost.Echo"/>.</item>
/// <item><b>Non-blocking roundtime wait.</b> The Genie 4 code did
/// <c>Thread.Sleep(rt*1000)</c> on the parse thread; here the post-inventory RT wait
/// is a non-blocking <see cref="Task.Delay(int)"/> so it never stalls the game loop.</item>
/// <item><b>End-of-home detected on the prompt.</b> Genie 4 watched for a bare "&gt;"
/// text line; Genie 5 surfaces that as <see cref="OnPrompt"/>, used here instead.</item>
/// <item><b>Cross-platform wiki + data path.</b> Wiki lookups go through the host's
/// <c>#browser</c> command (works on Win/macOS/Linux); the XML lives under
/// <c>{AppData}/Genie5</c>.</item>
/// </list></para>
///
/// Original author: Etherian &lt;EtherianDR@gmail.com&gt;. Ported to Genie 5.
/// </summary>
public sealed class InventoryViewPlugin : IGeniePlugin
{
    // ── Identity / metadata ────────────────────────────────────────────────────
    public string Id             => "genie.inventoryview";
    public string Name           => "Inventory View";
    public string Version        => "1.8";
    public string Author         => "Etherian <EtherianDR@gmail.com> (ported to Genie 5)";
    public string Description     => "Stores your character inventory and lets you search items across characters.";
    public string MinHostVersion => "5.0.0";

    /// <summary>The named window this plugin renders its catalog into. The host
    /// surfaces it as a dock panel on the first <see cref="IPluginHost.SetWindow"/>
    /// call — this is how a Genie 5 plugin "creates its own window".</summary>
    private const string WindowName = "Inventory View";

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value)
            {
                _scanMode = null;   // abandon any in-progress scan when disabled
                _host?.SetWindow(WindowName, "(Inventory View plugin disabled)");
            }
        }
    }

    private IPluginHost _host = null!;
    private CancellationTokenSource _cts = new();

    // All catalogued inventory (was Class1.characterData — instance state now, since
    // there is no static form reaching into it).
    private List<CharacterData> _characterData = new();

    // ── Scan state (mirrors the Genie 4 fields) ────────────────────────────────
    private string? _scanMode;          // null = not scanning; else the FSM phase
    private int _level = 1;             // current container depth while scanning
    private CharacterData? _currentData; // character+source being filled
    private ItemData? _lastItem;        // last node added (the parent-walk anchor)

    private bool _debug;
    private string _lastText = "";

    // ── Lifecycle ───────────────────────────────────────────────────────────────
    public void Initialize(IPluginHost host)
    {
        _host = host;
        _cts  = new CancellationTokenSource();
        LoadSettings(initial: true);
    }

    public void Shutdown()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    // ── Game text → scan FSM ────────────────────────────────────────────────────
    public string? OnGameText(string text, string stream)
    {
        // Observe-only: never rewrite or gag game text. The scan reads it; the line
        // always flows through unchanged.
        if (_scanMode != null && stream == "main")
            HandleScanLine(text);
        return text;
    }

    /// <summary>End-of-home signal. Genie 4 keyed off a bare "&gt;" text line; in
    /// Genie 5 the prompt arrives here. Only meaningful mid-home-scan.</summary>
    public void OnPrompt()
    {
        if (_scanMode == "Home")
            AfterHome();
    }

    public string? OnInput(string input)
    {
        var lower = input.TrimStart().ToLowerInvariant();
        if (lower == "/inventoryview" || lower == "/iv" ||
            lower.StartsWith("/inventoryview ") || lower.StartsWith("/iv "))
        {
            HandleCommand(input.Trim());
            return null;   // swallow the command (Genie 4 returned string.Empty)
        }
        return input;
    }

    public void OnXml(string xml) { }
    public void OnCommandSent(string command) { }
    public void OnVariableChanged(string name, string value) { }

    // ── Command handling (was ParseInput) ──────────────────────────────────────
    private void HandleCommand(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb  = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "help";
        var arg   = parts.Length >= 3 ? string.Join(' ', parts.Skip(2)) : "";

        switch (verb)
        {
            case "scan":     StartScan();                              break;
            case "open":
            case "list":     RenderCatalog();                         break;
            case "search":   Search(arg);                             break;
            case "reload":   LoadSettings(); _host.Echo("Inventory reloaded."); break;
            case "wiki":     WikiLookup(arg);                         break;
            case "export":   ExportCsv(arg);                          break;
            case "debug":
                _debug = !_debug;
                _host.Echo("InventoryView Debug Mode " + (_debug ? "ON" : "OFF"));
                break;
            case "lasttext": _host.Echo("InventoryView Debug Last Text: " + _lastText); break;
            case "help":
            default:         Help();                                  break;
        }
    }

    private void Help()
    {
        _host.Echo("Inventory View plugin options:");
        _host.Echo("/iv scan            -- scan items on the current character (person, vault, deed, home, trader).");
        _host.Echo("/iv open            -- show the full catalog of every scanned character in the Inventory View window.");
        _host.Echo("/iv search <text>   -- show every catalogued item matching <text> (by full path) in the window.");
        _host.Echo("/iv reload          -- reload InventoryView.xml from disk (sync after scanning on another instance).");
        _host.Echo("/iv wiki <item>     -- open Elanthipedia/drservice for <item> in your browser.");
        _host.Echo("/iv export [path]   -- export the catalog to a CSV file.");
        _host.Echo("(\"/inventoryview\" works as the long form of \"/iv\".)");
    }

    private void StartScan()
    {
        if (_host.GetVariable("connected") == "0")
        {
            _host.Echo("You must be connected to the server to do a scan.");
            return;
        }

        LoadSettings();                       // pick up other instances' data first
        _scanMode = "Start";
        var me = _host.GetVariable("charactername") ?? "";
        _characterData.RemoveAll(c => c.name == me);   // replace this character's old data
        _host.SendCommand("inventory list");
    }

    // ── The scan state machine (faithful port of Genie 4 ParseText) ─────────────
    private void HandleScanLine(string text)
    {
        // Trim spaces and newlines, exactly like the original.
        string trimtext = text.Trim('\n', '\r', ' ');
        _lastText = trimtext;

        if (trimtext.StartsWith("XML") && trimtext.EndsWith("XML")) return; // skip XML lines
        if (string.IsNullOrEmpty(trimtext)) return;                          // skip blanks

        switch (_scanMode)
        {
            case "Start":
                if (trimtext == "You have:")    // start of "inventory list"
                {
                    _host.Echo("Scanning Inventory.");
                    _scanMode    = "Inventory";
                    _currentData = NewSource("Inventory");
                    _level       = 1;
                }
                break;

            case "Inventory":
                if (text.StartsWith("[Use INVENTORY HELP")) { /* skip */ }
                else if (text.StartsWith("Roundtime:"))     // end of "inventory list"
                {
                    // Inventory list applies an RT proportional to item count. Wait it
                    // out (non-blocking) before grabbing the vault book.
                    int seconds = ParseRoundtime(trimtext);
                    _scanMode = "VaultStart";
                    PauseForRtThenSend(seconds, "get my vault book");
                }
                else AddIndentedItem(text, trimtext, indentStep: 3, rootStorage: false);
                break;

            case "VaultStart":
                if (Regex.IsMatch(trimtext, @"^You get a.*vault book.*from") ||
                    trimtext == "You are already holding that.")
                {
                    _host.Echo("Scanning Vault.");
                    _host.SendCommand("read my vault book");
                }
                else if (trimtext == "Vault Inventory:")
                {
                    _scanMode    = "Vault";
                    _currentData = NewSource("Vault");
                    _level       = 1;
                }
                else if (trimtext == "What were you referring to?" ||
                         trimtext == "The script that the vault book is written in is unfamiliar to you.  You are unable to read it." ||
                         trimtext == "The vault book is filled with blank pages pre-printed with branch office letterhead.  An advertisement touting the services of Rundmolen Bros. Storage Co. is pasted on the inside cover.")
                {
                    _host.Echo("Skipping Vault.");
                    _scanMode = "DeedStart";
                    _host.SendCommand("get my deed register");
                }
                break;

            case "Vault":
                if (text.StartsWith("The last note in your book indicates that your vault contains"))
                {
                    _scanMode = "DeedStart";
                    _host.SendCommand("stow my vault book");
                    _host.SendCommand("get my deed register");
                }
                else
                {
                    // Vault nesting: level 1 at <=4 leading spaces, +1 per 2 spaces beyond.
                    int spaces   = text.Length - text.TrimStart().Length;
                    int newlevel = 1 + (spaces > 4 ? (spaces - 4) / 2 : 0);
                    AddAtLevel(newlevel, StripDash(trimtext), rootStorage: true);
                }
                break;

            case "DeedStart":
                if (Regex.IsMatch(trimtext, @"^You get a.*deed register.*from") ||
                    trimtext == "You are already holding that.")
                {
                    _host.Echo("Scanning Deed Register.");
                    _host.SendCommand("turn my deed register to contents");
                    _host.SendCommand("read my deed register");
                }
                else if (trimtext == "Page -- Deed")
                {
                    _scanMode    = "Deed";
                    _currentData = NewSource("Deed");
                    _level       = 1;
                }
                else if (trimtext == "What were you referring to?" ||
                         trimtext.StartsWith("You haven't stored any deeds in this register."))
                {
                    _host.Echo("Skipping Deed Register.");
                    _scanMode = "HomeStart";
                    _host.SendCommand("home recall");
                }
                break;

            case "Deed":
                if (trimtext.StartsWith("Currently stored"))
                {
                    _host.SendCommand("stow my deed register");
                    _scanMode = "HomeStart";
                    _host.SendCommand("home recall");
                }
                else
                {
                    // Deed entries are "Page -- Deed name"; keep the text after "--".
                    int idx = trimtext.IndexOf("--");
                    string tap = idx >= 0 ? trimtext.Substring(idx + 3) : trimtext;
                    _lastItem = _currentData!.AddItem(new ItemData { tap = tap, storage = false });
                }
                break;

            case "HomeStart":
                if (trimtext == "The home contains:")
                {
                    _host.Echo("Scanning Home.");
                    _scanMode    = "Home";
                    _currentData = NewSource("Home");
                    _level       = 1;
                }
                else if (trimtext.StartsWith("Your documentation filed with the Estate Holders"))
                {
                    _host.Echo("Skipping Home.");
                    AfterHome();
                }
                else if (trimtext == "You shouldn't do that while inside of a home.  Step outside if you need to check something.")
                {
                    _host.Echo("You cannot check the contents of your home while inside of a home. Step outside and try again.");
                    AfterHome();
                }
                break;

            case "Home":
                if (trimtext == ">")            // belt-and-braces; OnPrompt also catches this
                {
                    AfterHome();
                }
                else if (trimtext.StartsWith("Attached:"))   // attached to a piece of furniture
                {
                    string tap = trimtext.Replace("Attached: ", "");
                    var holder = _lastItem?.parent ?? _lastItem;
                    _lastItem = holder != null
                        ? holder.AddItem(new ItemData { tap = tap })
                        : _currentData!.AddItem(new ItemData { tap = tap });
                }
                else                                          // a piece of furniture
                {
                    int idx = trimtext.IndexOf(":");
                    string tap = idx >= 0 ? trimtext.Substring(idx + 2) : trimtext;
                    _lastItem = _currentData!.AddItem(new ItemData { tap = tap, storage = true });
                }
                break;

            case "TraderStart":
                if (Regex.IsMatch(trimtext, @"^You get a.*storage book.*from") ||
                    trimtext == "You are already holding that.")
                {
                    _host.Echo("Scanning Trader Storage.");
                    _host.SendCommand("read my storage book");
                }
                else if (trimtext == "in the known realms since 402.")
                {
                    _scanMode    = "Trader";
                    _currentData = NewSource("TraderStorage");
                    _level       = 1;
                }
                else if (trimtext == "What were you referring to?" ||
                         trimtext == "The storage book is filled with complex lists of inventory that make little sense to you.")
                {
                    _host.Echo("Skipping Trader Storage.");
                    CompleteScan();
                }
                break;

            case "Trader":
                if (text.StartsWith("A notation at the bottom indicates"))
                {
                    CompleteScan();
                }
                else
                {
                    // Trader nesting is fixed-width: 4/8/12 spaces = level 1/2/3.
                    int spaces   = text.Length - text.TrimStart().Length;
                    int newlevel = spaces switch { 4 => 1, 8 => 2, 12 => 3, _ => 3 };
                    AddAtLevel(newlevel, StripDash(trimtext), rootStorage: true);
                }
                break;
        }
    }

    /// <summary>After the Home phase (or a skip): Traders then scan their storage
    /// book; everyone else finishes. Mirrors the duplicated Genie 4 tail blocks.</summary>
    private void AfterHome()
    {
        if (_host.GetVariable("guild") == "Trader")
        {
            _scanMode = "TraderStart";
            _host.SendCommand("get my storage book");
        }
        else CompleteScan();
    }

    private void CompleteScan()
    {
        _scanMode = null;
        _host.Echo("Scan Complete.");
        // Re-emit through the parse pipeline so scripts can `waitforre ^InventoryView scan complete`.
        _host.SendCommand("#parse InventoryView scan complete");
        SaveSettings();
        // Surface the freshly-scanned catalog in the plugin's own window so it
        // pops up the moment the scan finishes (creates the panel if needed).
        RenderCatalog();
    }

    // ── Tree-building helpers ───────────────────────────────────────────────────
    private CharacterData NewSource(string source)
    {
        var data = new CharacterData
        {
            name   = _host.GetVariable("charactername") ?? "",
            source = source,
        };
        _characterData.Add(data);
        return data;
    }

    private static string StripDash(string tap) => tap.StartsWith("-") ? tap.Substring(1) : tap;

    /// <summary>Inventory uses a per-(indentStep) leading-space scheme (2,5,8,… for
    /// step 3). Computes the level and delegates to <see cref="AddAtLevel"/>.</summary>
    private void AddIndentedItem(string rawText, string trimtext, int indentStep, bool rootStorage)
    {
        int spaces   = rawText.Length - rawText.TrimStart().Length;
        int newlevel = (spaces + 1) / indentStep;
        if (_debug) _host.Echo($"[IV dbg] sp={spaces} lvl={newlevel}: {trimtext}");
        AddAtLevel(newlevel, StripDash(trimtext), rootStorage);
    }

    /// <summary>The shared tree-insert from the Genie 4 plugin: place a node relative
    /// to the previous one by comparing <paramref name="newlevel"/> to the current
    /// depth, walking up parents when we pop out of a container. Null-guarded so a
    /// stripped-indentation stream degrades to a flat list instead of crashing.</summary>
    private void AddAtLevel(int newlevel, string tap, bool rootStorage)
    {
        if (_currentData is null) return;

        if (newlevel <= 1 || _lastItem is null)            // root item
        {
            _lastItem = _currentData.AddItem(new ItemData { tap = tap, storage = rootStorage });
            _level = 1;
            return;
        }

        if (newlevel == _level)                            // sibling of the previous item
        {
            if (_lastItem.parent != null)
                _lastItem = _lastItem.parent.AddItem(new ItemData { tap = tap });
            else
                _lastItem = _currentData.AddItem(new ItemData { tap = tap, storage = rootStorage });
        }
        else if (newlevel == _level + 1)                   // child of the previous item
        {
            _lastItem = _lastItem.AddItem(new ItemData { tap = tap });
        }
        else                                               // popped up one or more levels
        {
            for (int i = newlevel; i <= _level && _lastItem?.parent != null; i++)
                _lastItem = _lastItem.parent;
            _lastItem = _lastItem != null
                ? _lastItem.AddItem(new ItemData { tap = tap })
                : _currentData.AddItem(new ItemData { tap = tap, storage = rootStorage });
        }
        _level = newlevel;
    }

    private static int ParseRoundtime(string trimtext)
    {
        var m = Regex.Match(trimtext, @"^Roundtime:\s{1,3}(\d{1,3})\s{1,3}secs?\.$");
        return m.Success && int.TryParse(m.Groups[1].Value, out var s) ? s : 1;
    }

    /// <summary>Non-blocking replacement for the Genie 4 <c>Thread.Sleep(rt*1000)</c>:
    /// schedules the follow-up command after the roundtime without stalling the
    /// game/parse loop. Cancelled if the plugin is unloaded mid-wait.</summary>
    private void PauseForRtThenSend(int seconds, string command)
    {
        _host.Echo($"Pausing {seconds} seconds for RT.");
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(Math.Max(0, seconds) * 1000, token); }
            catch (TaskCanceledException) { return; }
            if (!token.IsCancellationRequested) _host.SendCommand(command);
        });
    }

    // ── Catalog display (replaces the WinForms TreeView) ────────────────────────
    // Both the full catalog and search results render into the plugin's OWN dock
    // window via SetWindow — the Genie 5 equivalent of the old InventoryViewForm.
    // A short status line still goes to the main window via Echo so the user gets
    // feedback even if the panel is hidden behind another tab.

    private void RenderCatalog()
    {
        if (_characterData.Count == 0)
        {
            const string empty = "(nothing scanned yet — type '/iv scan' while connected)";
            _host.SetWindow(WindowName, empty);
            _host.Echo("InventoryView: nothing scanned yet. Type /iv scan while connected.");
            return;
        }

        var sb = new StringBuilder();
        sb.Append("Inventory catalog — ")
          .Append(_characterData.Select(c => c.name).Distinct().Count())
          .Append(" character(s)\n");
        sb.Append("──────────────────────────────────────\n");

        foreach (var character in _characterData.Select(c => c.name).Distinct().OrderBy(n => n))
        {
            sb.Append(character).Append('\n');
            foreach (var src in _characterData.Where(c => c.name == character))
            {
                sb.Append("  ").Append(src.source).Append('\n');
                foreach (var item in src.items)
                    RenderItem(sb, item, depth: 2);
            }
        }

        _host.SetWindow(WindowName, sb.ToString().TrimEnd());
        _host.Echo("InventoryView: catalog shown in the Inventory View window (Window → Plugin Windows).");
    }

    private void RenderItem(StringBuilder sb, ItemData item, int depth)
    {
        sb.Append(new string(' ', depth * 2)).Append(item.tap).Append('\n');
        foreach (var child in item.items)
            RenderItem(sb, child, depth + 1);
    }

    private void Search(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            _host.Echo("Usage: /iv search <text>");
            return;
        }

        var matches = new List<string>();
        foreach (var c in _characterData)
            foreach (var item in c.items)
                SearchItem(c.name, c.source, item, term, matches);

        var sb = new StringBuilder();
        sb.Append("Search: \"").Append(term).Append("\"  —  ")
          .Append(matches.Count).Append(" match(es)\n");
        sb.Append("──────────────────────────────────────\n");
        if (matches.Count == 0)
            sb.Append("(no matches)");
        else
            foreach (var path in matches)
                sb.Append(path).Append('\n');

        _host.SetWindow(WindowName, sb.ToString().TrimEnd());
        _host.Echo($"InventoryView: {matches.Count} match(es) for \"{term}\" (see the Inventory View window).");
    }

    private static void SearchItem(string character, string source, ItemData item, string term, List<string> matches)
    {
        if (!string.IsNullOrEmpty(item.tap) &&
            item.tap.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            matches.Add(FullPath(character, source, item));

        foreach (var child in item.items)
            SearchItem(character, source, child, term, matches);
    }

    /// <summary>"Character &gt; Source &gt; container &gt; … &gt; item" for a node.</summary>
    private static string FullPath(string character, string source, ItemData item)
    {
        var parts = new List<string> { item.tap };
        for (var p = item.parent; p != null; p = p.parent)
            parts.Insert(0, p.tap);
        parts.Insert(0, source);
        parts.Insert(0, character);
        return string.Join(" > ", parts);
    }

    private void WikiLookup(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            _host.Echo("Usage: /iv wiki <item text>");
            return;
        }
        // Genie 4 used drservice.info's tap→wiki table; route through Genie 5's
        // cross-platform #browser command (Genie 4 InterfaceVersion 4 used #browser too).
        var url = "https://drservice.info/wiki.ashx?tap=" + Uri.EscapeDataString(item.Trim());
        _host.SendCommand("#browser " + url);
    }

    // ── CSV export (port of the form's Export button) ───────────────────────────
    private void ExportCsv(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(DataDir(), "InventoryView_export.csv");

        try
        {
            using var sw = new StreamWriter(path, append: false);
            sw.WriteLine("Character,Source,Tap,Path");
            foreach (var c in _characterData)
                foreach (var item in c.items)
                    ExportItem(sw, c.name, c.source, item);
            _host.Echo($"InventoryView: export complete -> {path}");
        }
        catch (IOException ex)
        {
            _host.Echo("InventoryView export error: " + ex.Message);
        }
    }

    private void ExportItem(StreamWriter sw, string character, string source, ItemData item)
    {
        var pathParts = new List<string> { source };
        for (var p = item.parent; p != null; p = p.parent)
            pathParts.Insert(1, p.tap);
        sw.WriteLine(string.Join(",",
            CleanCsv(character), CleanCsv(source), CleanCsv(item.tap),
            CleanCsv(string.Join("\\", pathParts))));

        foreach (var child in item.items)
            ExportItem(sw, character, source, child);
    }

    private static string CleanCsv(string data)
    {
        if (!data.Contains(',')) return data;
        if (!data.Contains('"')) return $"\"{data}\"";
        return $"\"{data.Replace("\"", "\"\"")}\"";
    }

    // ── Persistence (port of LoadSettings/SaveSettings) ─────────────────────────
    /// <summary>Where InventoryView.xml lives. Prefers a host-provided
    /// <c>PluginPath</c> variable (Genie 4 used this); otherwise replicates
    /// Genie.Core's AppData resolution (the plugin can't reference Genie.Core).</summary>
    private string DataDir()
    {
        var configured = _host.GetVariable("PluginPath");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root))   // some Linux/headless setups
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");
        return Path.Combine(root, "Genie5");
    }

    private string ConfigFile => Path.Combine(DataDir(), "InventoryView.xml");

    private void LoadSettings(bool initial = false)
    {
        var file = ConfigFile;
        if (!File.Exists(file)) return;
        try
        {
            using (var stream = File.Open(file, FileMode.Open))
            {
                var serializer = new XmlSerializer(typeof(List<CharacterData>));
                _characterData = (List<CharacterData>)serializer.Deserialize(stream)!;
            }
            foreach (var c in _characterData)
                AddParents(c.items, null);
            if (!initial)
                _host.Echo("InventoryView data loaded.");
        }
        catch (Exception ex)   // IOException + XmlSerializer's InvalidOperationException
        {
            _host.Echo("Error reading InventoryView file: " + ex.Message);
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(DataDir());
            // parent is [XmlIgnore], so no circular-ref strip/rebuild dance is needed.
            using var writer = new FileStream(ConfigFile, FileMode.Create);
            var serializer = new XmlSerializer(typeof(List<CharacterData>));
            serializer.Serialize(writer, _characterData);
        }
        catch (Exception ex)
        {
            _host.Echo("Error writing to InventoryView file: " + ex.Message);
        }
    }

    private static void AddParents(List<ItemData> items, ItemData? parent)
    {
        foreach (var item in items)
        {
            item.parent = parent;
            AddParents(item.items, item);
        }
    }
}
