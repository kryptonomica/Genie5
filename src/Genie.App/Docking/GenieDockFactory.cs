using Avalonia.Controls;
using Avalonia.Media;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI;
using Dock.Model.ReactiveUI.Controls;
using Genie.App.ViewModels;

namespace Genie.App.Docking;

public class GenieDockFactory : Factory
{
    private readonly MainWindowViewModel _vm;

    /// <summary>
    /// Map of dockable-id → (instance, its default parent dock). Populated by
    /// <see cref="CreateLayout"/> so the Window menu can show/hide both Tools
    /// and Documents by id while remembering where to put them back.
    /// </summary>
    private readonly Dictionary<string, (IDockable Dockable, IDock Parent)> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The root returned by <see cref="CreateLayout"/>. Stored so we can walk
    /// the whole layout tree (including floating windows) when answering
    /// visibility queries — reference-equality on stored Dockable instances is
    /// unreliable because Dock.Avalonia may reinstantiate during layout init.
    /// </summary>
    private IRootDock? _root;

    public GenieDockFactory(MainWindowViewModel vm)
    {
        _vm = vm;
    }

    public override IRootDock CreateLayout()
    {
        // Wire the host-window locator. Dock.Avalonia's FloatDockable silently
        // no-ops when this is null, which is why "Pop out to window" didn't
        // produce a window in the first build — the factory was building a
        // dock-window stub but had no way to host it as an OS-level Window.
        // Keyed by nameof(IDockWindow) per the Dock 11.x convention.
        //
        // The Background + TransparencyLevelHint settings are required: by
        // default HostWindow has a transparent fill and an acrylic transparency
        // hint, so on Windows the floated panel sits on top of whatever's
        // behind it on the desktop instead of looking like a proper opaque
        // window. Forcing an opaque dark fill matches the main window.
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () =>
            {
                var w = new HostWindow
                {
                    Background           = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f)),
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.None },
                };
                return w;
            }
        };

        // Hand each Tool its WindowSettings entry so Layout-tab edits repaint
        // live (font, foreground, background, title).
        var ws       = _vm.WindowSettings;
        var gameText = new GameTextDocument(_vm.GameText,         ws.Get("game-text"));
        var vitals   = new VitalsTool      (_vm.Vitals,           ws.Get("vitals"));
        var room     = new RoomTool        (_vm.Room,             ws.Get("room"));
        var backpack = new BackpackTool    (_vm.Inventory,        ws.Get("backpack"));
        var mapper   = new MapperTool      (_vm.Mapper,           ws.Get("mapper"));
        var logons   = new StreamTool      (_vm.StreamTabs.Logons,   ws.Get("logons"));
        var talk     = new StreamTool      (_vm.StreamTabs.Talk,     ws.Get("talk"));
        var whispers = new StreamTool      (_vm.StreamTabs.Whispers, ws.Get("whispers"));
        var thoughts = new StreamTool      (_vm.StreamTabs.Thoughts, ws.Get("thoughts"));
        var combat   = new StreamTool      (_vm.StreamTabs.Combat,   ws.Get("combat"));

        // ── Default ship layout — three vertical columns ─────────────────
        //   ┌──────────┬─────────────────────┬──────────┐
        //   │ Room     │ Game                │ Backpack │
        //   │          ├─────────────────────┤          │
        //   │ Streams  │ Mapper              │          │
        //   └──────────┴─────────────────────┴──────────┘
        //   Vitals stays in the registry but is OUT of the default-visible
        //   set — duplicates the bottom Status Bar. Users can re-open via
        //   Window → Vitals. Same pattern for any other panel.

        var documentDock = new DocumentDock
        {
            Id               = "docs",
            IsCollapsable    = false,
            VisibleDockables = CreateList<IDockable>(gameText),
            ActiveDockable   = gameText
        };

        // ── Center column: Game (top) + Mapper (bottom) ─────────────────
        var mapperDock = new ToolDock
        {
            Id               = "mapper-dock",
            Alignment        = Alignment.Bottom,
            Proportion       = 0.40,
            VisibleDockables = CreateList<IDockable>(mapper),
            ActiveDockable   = mapper
        };

        var centerCol = new ProportionalDock
        {
            Id               = "center-col",
            Orientation      = Orientation.Vertical,
            IsCollapsable    = false,
            VisibleDockables = CreateList<IDockable>(
                documentDock,
                new ProportionalDockSplitter(),
                mapperDock
            )
        };

        // ── Left column: Room (top) + stream tabs (bottom) ──────────────
        var roomDock = new ToolDock
        {
            Id               = "room-dock",
            Alignment        = Alignment.Left,
            Proportion       = 0.35,
            VisibleDockables = CreateList<IDockable>(room),
            ActiveDockable   = room
        };

        var streamDock = new ToolDock
        {
            Id               = "streams",
            Alignment        = Alignment.Bottom,
            Proportion       = 0.65,
            VisibleDockables = CreateList<IDockable>(logons, talk, whispers, thoughts, combat),
            ActiveDockable   = combat   // matches screenshot default — Combat tab active
        };

        var leftCol = new ProportionalDock
        {
            Id               = "left-col",
            Orientation      = Orientation.Vertical,
            Proportion       = 0.22,
            IsCollapsable    = false,
            VisibleDockables = CreateList<IDockable>(
                roomDock,
                new ProportionalDockSplitter(),
                streamDock
            )
        };

        // ── Right column: Backpack (full height) ────────────────────────
        var backpackDock = new ToolDock
        {
            Id               = "backpack-dock",
            Alignment        = Alignment.Right,
            Proportion       = 0.22,
            VisibleDockables = CreateList<IDockable>(backpack),
            ActiveDockable   = backpack
        };

        // ── Side dock holds Vitals as a registered-but-hidden tool ──────
        // Kept so SetToolVisibility("vitals", true) re-opens it next to the
        // Backpack. Not part of the visible layout by default.
        var sideDock = new ToolDock
        {
            Id               = "side",
            Alignment        = Alignment.Right,
            Proportion       = 0.22,
            VisibleDockables = CreateList<IDockable>(),  // empty — Vitals re-attaches here when toggled on
            ActiveDockable   = null
        };

        // ── Root: three columns side-by-side ────────────────────────────
        var rootLayout = new ProportionalDock
        {
            Id               = "root-layout",
            Orientation      = Orientation.Horizontal,
            IsCollapsable    = false,
            VisibleDockables = CreateList<IDockable>(
                leftCol,
                new ProportionalDockSplitter(),
                centerCol,
                new ProportionalDockSplitter(),
                backpackDock
            )
        };

        var root = CreateRootDock();
        root.Id               = "root";
        root.IsCollapsable    = false;
        root.VisibleDockables = CreateList<IDockable>(rootLayout);
        root.ActiveDockable   = rootLayout;
        root.DefaultDockable  = rootLayout;
        _root                 = root;

        // ── Dockable registry for Window-menu visibility toggles ──────────
        // Includes the Game document so the user can re-open it from the menu
        // after clicking the X on its tab.
        // Registry maps each tool to its canonical parent dock — used by
        // SetToolVisibility to re-open a hidden tool in its natural spot.
        // After the layout rebuild the parents differ from the pre-rebuild
        // single sideDock: Room → roomDock, Backpack → backpackDock, etc.
        _tools.Clear();
        _tools[gameText.Id] = (gameText, documentDock);
        _tools[vitals.Id]   = (vitals,   sideDock);       // Vitals re-opens beside Backpack when toggled on
        _tools[room.Id]     = (room,     roomDock);
        _tools[backpack.Id] = (backpack, backpackDock);
        _tools[mapper.Id]   = (mapper,   mapperDock);
        _tools[logons.Id]   = (logons,   streamDock);
        _tools[talk.Id]     = (talk,     streamDock);
        _tools[whispers.Id] = (whispers, streamDock);
        _tools[thoughts.Id] = (thoughts, streamDock);
        _tools[combat.Id]   = (combat,   streamDock);

        return root;
    }

    // ── Window-management API ──────────────────────────────────────────────

    /// <summary>
    /// True if any dockable with this id exists anywhere in the live layout
    /// tree (including floating windows). Id-based lookup so we're not tripped
    /// up by Dock.Avalonia replacing instances during layout init.
    /// </summary>
    public bool IsToolVisible(string id)
    {
        if (_root is null) return false;
        return FindByIdInTree(_root, id) is not null;
    }

    /// <summary>
    /// Show or hide a registered dockable. Showing re-adds it to its original
    /// parent dock and activates the tab. Hiding closes whatever instance is
    /// currently in the tree (in case Dock.Avalonia replaced our stored ref).
    /// </summary>
    public void SetToolVisibility(string id, bool visible)
    {
        if (!_tools.TryGetValue(id, out var entry)) return;
        var (dockable, parent) = entry;

        var current = _root is null ? null : FindByIdInTree(_root, id);
        var currentlyVisible = current is not null;
        if (currentlyVisible == visible) return;

        if (visible)
        {
            AddDockable(parent, dockable);
            SetActiveDockable(dockable);
        }
        else
        {
            // Close the instance actually in the tree, not our stored reference
            // (which Dock.Avalonia may have replaced during init).
            CloseDockable(current!);
        }
    }

    /// <summary>
    /// Detach a tool into its own top-level floating window. Idempotent:
    /// if the tool is missing from the tree it's a no-op; if it's already
    /// in a floating window Dock.Avalonia handles re-floating gracefully
    /// (the tool's CanFloat must be true, which is the default on Tool).
    /// </summary>
    public void FloatTool(string id)
    {
        if (_root is null) return;
        var current = FindByIdInTree(_root, id);
        if (current is null) return;

        // Base Factory.FloatDockable lifts the dockable into a new HostWindow
        // anchored to the root's Windows collection. Dragging that window's
        // title bar back over the main app shows dock indicators so the user
        // can re-dock it wherever they want.
        FloatDockable(current);
    }

    /// <summary>
    /// Walk the dock tree (including floating windows under any RootDock) and
    /// return the first dockable whose Id matches, or null.
    /// </summary>
    private static IDockable? FindByIdInTree(IDockable node, string id)
    {
        if (string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase))
            return node;

        if (node is IDock dock)
        {
            if (dock.VisibleDockables is { } children)
                foreach (var child in children)
                    if (FindByIdInTree(child, id) is { } hit)
                        return hit;

            if (node is IRootDock root && root.Windows is { } windows)
                foreach (var w in windows)
                    if (w.Layout is { } layout && FindByIdInTree(layout, id) is { } hit)
                        return hit;
        }
        return null;
    }

    /// <summary>Dockable ids known to the factory — exposed so the VM can iterate the registry.</summary>
    public IReadOnlyCollection<string> ToolIds => _tools.Keys;

    // FactoryBase already exposes DockableClosed / DockableAdded events; the
    // VM subscribes to those directly in its constructor so the Window-menu
    // check marks stay aligned with the dock's actual state.
}
