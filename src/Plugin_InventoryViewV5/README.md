# Plugin_InventoryViewV5

The Genie 5 port of [GenieClient/Plugin_InventoryView](https://github.com/GenieClient/Plugin_InventoryView)
(Etherian's Genie 4 plugin, v1.8).

It records what each of your characters owns — on their person, in a **vault**
(if you have a vault book), a **deed register**, a **home**, and **Trader
storage** — into a per-character tree saved to `InventoryView.xml`, and lets you
search that catalog across all of your characters.

## Install

1. Build the project (or grab `Plugin_InventoryViewV5.dll`).
2. Drop `Plugin_InventoryViewV5.dll` into `{AppData}/Genie5/Plugins/`
   (`%APPDATA%\Genie5\Plugins` on Windows).
3. Connect — the host discovers and loads it. Or use `#plugin load Plugin_InventoryViewV5`.

## Commands

| Command | What it does |
|---|---|
| `/iv scan` | Scan the current character (inventory → vault → deed register → home → Trader storage) and save. The **Inventory View** window pops up with the result when the scan finishes. |
| `/iv open` (or `/iv list`) | Show the full catalog of every scanned character in the **Inventory View** dock window. |
| `/iv search <text>` | Show every catalogued item matching `<text>` (each by its full path) in the **Inventory View** window. |
| `/iv reload` | Reload `InventoryView.xml` from disk (run after scanning on another running instance). |
| `/iv wiki <item>` | Open Elanthipedia/drservice for `<item>` in your browser (via `#browser`). |
| `/iv export [path]` | Export the whole catalog to a CSV file (defaults to `{AppData}/Genie5/InventoryView_export.csv`). |
| `/iv debug` | Toggle a per-line scan trace (handy for diagnosing indentation). |

`/inventoryview` is accepted as the long form of `/iv`.

At the end of a scan the plugin prints **Scan Complete.** and re-emits
`InventoryView scan complete` through the parse pipeline, so a login/automation
script can wait on it:

```
send /iv scan
waitforre ^InventoryView scan complete
```

## What changed from the Genie 4 plugin

The **scan state machine is a faithful 1:1 port** — the same trigger strings, the
same sequence of commands sent to the game, the same leading-space indentation
parsing. Against DragonRealms it behaves identically to the original.

The parts that *had* to change for Genie 5:

- **Its own dock window instead of a WinForms form.** Genie 5 plugins are UI-free
  and reference only `Genie.Plugins.Abstractions`, but they *can* create their own
  panel through the host's named-window seam: this plugin calls
  `IPluginHost.SetWindow("Inventory View", …)` and the host surfaces it as a
  dockable, floatable, layout-persisted panel (toggle via **Window → Plugin
  Windows**). `/iv open` and `/iv search` render the catalog / results there; the
  panel auto-opens when a scan completes. Short status lines still go to the main
  window via `Echo`. The old `TreeView`'s expand/collapse/highlight/find-next
  chrome is replaced by the monospace text tree plus `/iv search`.
- **Non-blocking roundtime wait.** The Genie 4 code blocked the parse thread with
  `Thread.Sleep(rt * 1000)` after the inventory list. This port schedules the
  follow-up with a non-blocking `Task.Delay`, so a scan never stalls the game loop.
- **End-of-home on the prompt.** Genie 4 watched for a bare `>` text line to end
  the home scan; Genie 5 surfaces that as `OnPrompt()`, used here.
- **Cross-platform.** Wiki lookups use the host's `#browser` command instead of
  `Process.Start`, and `InventoryView.xml` resolves under `{AppData}/Genie5`
  (honoring macOS/Linux paths), not the Windows-only Genie config dir.
- **Persistence cleanup.** `ItemData.parent` is `[XmlIgnore]` instead of being
  nulled-and-rebuilt around every save. The on-disk XML is unchanged, so an
  existing `InventoryView.xml` from the Genie 4 plugin still loads.

## To verify against a live session

The scan relies on DR sending the inventory/vault/home lists as **main-window
text with their leading-space indentation intact**. That holds in Genie 4; it
should hold through Genie 5's parser, but it hasn't been exercised against a live
DR session yet. Run `/iv debug` then `/iv scan` and watch the `[IV dbg] sp= lvl=`
trace to confirm the indentation→level mapping if the tree ever looks flat.

Original author: Etherian &lt;EtherianDR@gmail.com&gt;. Ported to Genie 5.
