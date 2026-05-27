using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Genie.Core.Mapper;

namespace Genie.App.Controls;

/// <summary>
/// Lightweight custom-drawn map renderer for an <see cref="MapZone"/>. Paints
/// rooms as filled rectangles at their grid coordinates and draws lines between
/// rooms whose <see cref="MapExit.DestinationId"/> resolves. The room matching
/// <see cref="CurrentNode"/> is outlined in a hot colour so the player can see
/// where they are at a glance.
///
/// This is the MVP renderer — no pan, no zoom, no hit-testing, no labels. It
/// matches the Genie 4 AutoMapper's visual style in broad strokes (small
/// coloured squares connected by white edges) and is a stepping stone toward
/// the full UI in the screenshot the user referenced.
///
/// Re-renders when any of:
///  - <see cref="Zone"/> reference changes
///  - <see cref="CurrentNode"/> reference changes
///  - <see cref="Level"/> changes (Z-level filter)
///  - <see cref="RenderTick"/> bumps — used to force a redraw when the zone's
///    Nodes dictionary mutates but the zone reference itself hasn't.
/// </summary>
public class MapCanvas : Control
{
    // ── Visual constants (base — multiplied by Zoom at render time) ────────
    private const double BaseGridSize = 22.0;   // px per grid unit at zoom=1
    private const double BaseNodeSize = 14.0;   // node side length at zoom=1
    private const double BasePadding  = 32.0;   // canvas padding at zoom=1
    private const double EdgeWidth    = 1.0;
    private const double MinZoom      = 0.4;
    private const double MaxZoom      = 4.0;
    private const double ZoomStep     = 1.20;   // multiplicative step per wheel notch

    private double GridSize => BaseGridSize * Zoom;
    private double NodeSize => BaseNodeSize * Zoom;
    private double Padding  => BasePadding  * Zoom;

    private static readonly IBrush  BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));
    private static readonly IBrush  DefaultNodeFill = new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd));
    private static readonly IBrush  NodeStroke      = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly IBrush  CurrentStroke   = new SolidColorBrush(Color.FromRgb(0xff, 0x40, 0x40));
    private static readonly Pen     NodePen         = new(NodeStroke, 1.0);
    private static readonly Pen     CurrentPen      = new(CurrentStroke, 2.5);
    private static readonly IBrush  EmptyMessageBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

    // Edge pens — colour-coded by exit type so the player can see at a glance
    // which arcs are reachable via compass vs need a specific command.
    //   Compass (N/NE/E/SE/S/SW/W/NW) → light grey, standard
    //   Up / Down (same-level, rare)  → cyan, marks stairwell rooms
    //   Non-compass (Direction.None,  → green, "go arched building" /
    //     i.e. go-doors, climb-walls,    "climb trellis" / "swim river" etc.
    //     swim-rivers, etc.)
    private static readonly Pen     EdgePenCompass  = new(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), EdgeWidth);
    private static readonly Pen     EdgePenVertical = new(new SolidColorBrush(Color.FromRgb(0x40, 0xc8, 0xe0)), EdgeWidth);
    private static readonly Pen     EdgePenSpecial  = new(new SolidColorBrush(Color.FromRgb(0x60, 0xc0, 0x60)), EdgeWidth);

    // Room label paint — drawn next to nodes whose Notes attribute is set
    // (e.g. "Spell Library", "Pawnshop"). The Genie 4 AutoMapper uses these
    // labels as the primary way to orient the player in dense city zones.
    private static readonly IBrush  RoomLabelBrush  = new SolidColorBrush(Color.FromRgb(0xc8, 0xc8, 0xc8));

    // Hover badge — translucent panel + bright text drawn near the cursor.
    private static readonly IBrush  HoverBackgroundBrush = new SolidColorBrush(Color.FromArgb(0xee, 0x22, 0x22, 0x22));
    private static readonly Pen     HoverBorderPen       = new(new SolidColorBrush(Color.FromRgb(0x66, 0x88, 0xaa)), 1.0);
    private static readonly IBrush  HoverTitleBrush      = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
    private static readonly IBrush  HoverSubBrush        = new SolidColorBrush(Color.FromRgb(0x9b, 0xb8, 0xcc));

    // ── Styled properties ─────────────────────────────────────────────────

    public static readonly StyledProperty<MapZone?> ZoneProperty =
        AvaloniaProperty.Register<MapCanvas, MapZone?>(nameof(Zone));

    public static readonly StyledProperty<MapNode?> CurrentNodeProperty =
        AvaloniaProperty.Register<MapCanvas, MapNode?>(nameof(CurrentNode));

    public static readonly StyledProperty<int> LevelProperty =
        AvaloniaProperty.Register<MapCanvas, int>(nameof(Level));

    /// <summary>
    /// Monotonic counter the view-model increments to signal "the zone's Nodes
    /// changed in place, please repaint". Required because StyledProperty
    /// equality is reference-based — re-assigning the same zone reference
    /// would not fire <c>OnPropertyChanged</c>.
    /// </summary>
    public static readonly StyledProperty<int> RenderTickProperty =
        AvaloniaProperty.Register<MapCanvas, int>(nameof(RenderTick));

    /// <summary>
    /// Fired with the clicked <see cref="MapNode"/> as parameter when the user
    /// clicks a room rectangle. The Mapper VM's GotoNodeCommand handles
    /// pathfinding + walking.
    /// </summary>
    public static readonly StyledProperty<ICommand?> NodeClickedCommandProperty =
        AvaloniaProperty.Register<MapCanvas, ICommand?>(nameof(NodeClickedCommand));

    /// <summary>
    /// Scale factor for the whole map render. 1.0 = native size, clamped to
    /// [0.4, 4.0]. Mouse wheel and toolbar buttons drive this. AffectsMeasure
    /// so the ScrollViewer's scrollbars resize with the content.
    /// </summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<MapCanvas, double>(nameof(Zoom), defaultValue: 1.0,
            coerce: (_, v) => Math.Clamp(v, MinZoom, MaxZoom));

    /// <summary>
    /// Optional override for the "you are here" outline colour around the
    /// current room. Null = default red. Bound from the Mapper VM so users
    /// can pick their own colour via View → Highlight Color.
    /// </summary>
    public static readonly StyledProperty<IBrush?> CurrentRoomBrushProperty =
        AvaloniaProperty.Register<MapCanvas, IBrush?>(nameof(CurrentRoomBrush));

    /// <summary>
    /// Command invoked with a (node, exit) tuple when the user picks
    /// "Edit Exit ▶ {verb}" from the right-click context menu. Bound
    /// from MapperViewModel; on null, the Edit Exit submenu still
    /// appears but the items are disabled.
    /// </summary>
    public static readonly StyledProperty<ICommand?> EditExitCommandProperty =
        AvaloniaProperty.Register<MapCanvas, ICommand?>(nameof(EditExitCommand));

    /// <summary>
    /// User-chosen canvas background brush. Bound to
    /// <see cref="ViewModels.MapperViewModel.MapBackgroundBrush"/>. Null falls
    /// back to the default dark fill, so layout tests / designer previews
    /// without a DataContext still render sensibly.
    /// </summary>
    public static readonly StyledProperty<IBrush?> MapBackgroundBrushProperty =
        AvaloniaProperty.Register<MapCanvas, IBrush?>(nameof(MapBackgroundBrush));

    public MapZone?  Zone               { get => GetValue(ZoneProperty);               set => SetValue(ZoneProperty, value); }
    public MapNode?  CurrentNode        { get => GetValue(CurrentNodeProperty);        set => SetValue(CurrentNodeProperty, value); }
    public int       Level              { get => GetValue(LevelProperty);              set => SetValue(LevelProperty, value); }
    public int       RenderTick         { get => GetValue(RenderTickProperty);         set => SetValue(RenderTickProperty, value); }
    public ICommand? NodeClickedCommand { get => GetValue(NodeClickedCommandProperty); set => SetValue(NodeClickedCommandProperty, value); }
    public double    Zoom               { get => GetValue(ZoomProperty);               set => SetValue(ZoomProperty, value); }
    public IBrush?   CurrentRoomBrush   { get => GetValue(CurrentRoomBrushProperty);   set => SetValue(CurrentRoomBrushProperty, value); }
    public IBrush?   MapBackgroundBrush { get => GetValue(MapBackgroundBrushProperty); set => SetValue(MapBackgroundBrushProperty, value); }
    public ICommand? EditExitCommand    { get => GetValue(EditExitCommandProperty);    set => SetValue(EditExitCommandProperty, value); }

    // ── Hover state (internal, drives the tooltip paint) ──────────────────
    private MapNode? _hoveredNode;
    private Point    _cursor;

    static MapCanvas()
    {
        // Any of these changing means we need to repaint AND recompute size.
        AffectsRender<MapCanvas>(ZoneProperty, CurrentNodeProperty, LevelProperty, RenderTickProperty,
                                 ZoomProperty, CurrentRoomBrushProperty, MapBackgroundBrushProperty);
        AffectsMeasure<MapCanvas>(ZoneProperty, LevelProperty, RenderTickProperty, ZoomProperty);

        // Auto-center on the active room whenever it changes. Walking into a
        // new room shouldn't require the player to hunt for themselves in a
        // large zone — fire CenterOnCurrent so the surrounding ScrollViewer
        // pans to put the active node in the middle of the viewport.
        //
        // Manual user panning between room changes is preserved because we
        // only fire on CurrentNode-changes, not on every render. Zoom + zone
        // changes also re-center (different node coordinates → different
        // viewport offsets needed).
        CurrentNodeProperty.Changed.AddClassHandler<MapCanvas>((c, _) => c.CenterOnCurrent());
        ZoneProperty.Changed.AddClassHandler<MapCanvas>((c, _) => c.CenterOnCurrent());
        ZoomProperty.Changed.AddClassHandler<MapCanvas>((c, _) => c.CenterOnCurrent());
    }

    /// <summary>
    /// Scroll the surrounding <see cref="ScrollViewer"/> so the current
    /// room sits at the viewport center. Dispatched on background priority
    /// so the canvas has a chance to remeasure first — if the zone just
    /// loaded, <see cref="Bounds"/> may still be at the previous size when
    /// the CurrentNode property change fires.
    /// </summary>
    public void CenterOnCurrent()
    {
        if (CurrentNode is null || Zone is null) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (CurrentNode is null || Zone is null) return;

            // Find the bounds for the current Z-level (matches Render).
            int minX = int.MaxValue, minY = int.MaxValue;
            bool any = false;
            foreach (var n in Zone.Nodes.Values)
            {
                if (n.Z != Level) continue;
                if (n.X < minX) minX = n.X;
                if (n.Y < minY) minY = n.Y;
                any = true;
            }
            if (!any) return;

            var center = NodeCenter(CurrentNode, minX, minY);

            // Walk up the visual tree to find the ScrollViewer we live in
            // and scroll its Offset so the node center lands at the
            // viewport center. Clamp to [0, scrollable-extent] so we don't
            // try to set a negative offset (Avalonia clamps anyway, but
            // doing it explicitly keeps the math honest).
            var sv = this.FindAncestorOfType<ScrollViewer>();
            if (sv is null) return;

            var targetX = center.X - sv.Viewport.Width  / 2;
            var targetY = center.Y - sv.Viewport.Height / 2;
            var maxX    = Math.Max(0, Bounds.Width  - sv.Viewport.Width);
            var maxY    = Math.Max(0, Bounds.Height - sv.Viewport.Height);
            sv.Offset   = new Avalonia.Vector(
                Math.Clamp(targetX, 0, maxX),
                Math.Clamp(targetY, 0, maxY));
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    // ── Layout ────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Zone is null || Zone.Nodes.Count == 0)
            return new Size(200, 200);

        // Bounds of all nodes on the active Z-level. Avoid LINQ allocations
        // in the hot path — this runs on every layout pass.
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        bool any = false;
        foreach (var node in Zone.Nodes.Values)
        {
            if (node.Z != Level) continue;
            any = true;
            if (node.X < minX) minX = node.X;
            if (node.X > maxX) maxX = node.X;
            if (node.Y < minY) minY = node.Y;
            if (node.Y > maxY) maxY = node.Y;
        }
        if (!any) return new Size(200, 200);

        var w = (maxX - minX + 1) * GridSize + Padding * 2;
        var h = (maxY - minY + 1) * GridSize + Padding * 2;
        return new Size(Math.Max(200, w), Math.Max(200, h));
    }

    // ── Render ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        // Background — paint the whole control so empty areas don't show
        // whatever was behind the canvas. Honours the user-chosen brush from
        // the Mapper VM (ColorPickerButton in Details); falls back to the
        // dark default if no brush bound (designer / no-DataContext).
        var bg = MapBackgroundBrush ?? BackgroundBrush;
        context.FillRectangle(bg, new Rect(Bounds.Size));

        if (Zone is null || Zone.Nodes.Count == 0)
        {
            DrawCenteredMessage(context, "No zone loaded.\nConnect to DragonRealms or run File → Update Maps.");
            return;
        }

        // Compute level-filtered bounds (matches MeasureOverride).
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        bool any = false;
        foreach (var node in Zone.Nodes.Values)
        {
            if (node.Z != Level) continue;
            any = true;
            if (node.X < minX) minX = node.X;
            if (node.X > maxX) maxX = node.X;
            if (node.Y < minY) minY = node.Y;
            if (node.Y > maxY) maxY = node.Y;
        }
        if (!any)
        {
            DrawCenteredMessage(context, $"No rooms on level {Level}.");
            return;
        }

        // ── Pass 1: edges (under the nodes so they don't paint over the squares) ──
        foreach (var node in Zone.Nodes.Values)
        {
            if (node.Z != Level) continue;
            var fromCenter = NodeCenter(node, minX, minY);

            foreach (var exit in node.Exits)
            {
                if (!exit.DestinationId.HasValue) continue;
                if (!Zone.Nodes.TryGetValue(exit.DestinationId.Value, out var dest)) continue;
                if (dest.Z != Level) continue;

                // Draw each edge only once — pick the side where source.Id < dest.Id.
                if (node.Id > dest.Id) continue;

                // Pen by exit type so the user can tell at a glance which arcs
                // need a non-compass command vs vertical/standard.
                var pen = exit.Direction switch
                {
                    Direction.None                       => EdgePenSpecial,   // go / climb / swim
                    Direction.Up or Direction.Down       => EdgePenVertical,  // rare same-level vertical
                    _                                    => EdgePenCompass,
                };
                var toCenter = NodeCenter(dest, minX, minY);
                context.DrawLine(pen, fromCenter, toCenter);
            }
        }

        // ── Pass 2: nodes ──────────────────────────────────────────────────
        foreach (var node in Zone.Nodes.Values)
        {
            if (node.Z != Level) continue;

            var rect = NodeRect(node, minX, minY);
            var fill = ParseColor(node.Color) ?? DefaultNodeFill;

            context.FillRectangle(fill, rect);
            context.DrawRectangle(NodePen, rect);

            if (CurrentNode is not null && node.Id == CurrentNode.Id)
            {
                // Slight outset so the highlight stroke doesn't overlap the fill.
                var hi = rect.Inflate(2.0);
                // User-chosen "here I am" colour wins over the default red.
                var pen = CurrentRoomBrush is null
                    ? CurrentPen
                    : new Pen(CurrentRoomBrush, 2.5);
                context.DrawRectangle(pen, hi);
            }
        }

        // ── Pass 3: room labels (Notes attribute) ────────────────────────
        // Genie 4's AutoMapper draws the "note" attribute as text next to the
        // node rectangle — that's how Spell Library / Bank / Pawnshop etc.
        // are tagged on the canvas. We render the same thing here. Multi-line
        // notes (rare; usually a single short phrase) wrap naturally via
        // FormattedText. Overlap with adjacent labels is accepted for MVP —
        // a smarter placement pass can come later.
        var labelTypeface = Typeface.Default;
        var labelSize     = Math.Max(9.0, 10.0 * Zoom);   // grows slightly with zoom
        foreach (var node in Zone.Nodes.Values)
        {
            if (node.Z != Level) continue;
            if (string.IsNullOrEmpty(node.Notes)) continue;

            var ft = new FormattedText(
                node.Notes,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                labelTypeface,
                labelSize,
                RoomLabelBrush);

            var rect = NodeRect(node, minX, minY);
            // Position to the right of the node, vertically centered.
            var pos  = new Point(rect.Right + 4, rect.Top + (NodeSize - ft.Height) / 2);
            context.DrawText(ft, pos);
        }

        // ── Pass 4: hover badge ───────────────────────────────────────────
        if (_hoveredNode is { } hovered)
            DrawHoverBadge(context, hovered);
    }

    // ── Input ─────────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Right-click on a node = open a context menu with Go Here / Copy
        // Room ID / Show Details. We previously walked-immediately on right
        // click which made it too easy to start a long auto-walk by mis-
        // clicking; a menu adds one confirmation step. Genie 4's mapper
        // works the same way.
        //
        // Building the menu per-click rather than via a static ContextMenu
        // property because the items depend on which node was hit — the
        // menu needs the MapNode reference captured at click time.
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            var pt   = e.GetPosition(this);
            var node = HitTest(pt);
            if (node is null) return;

            ShowNodeContextMenu(node);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Builds and opens a fresh <see cref="ContextMenu"/> for the given
    /// node. Items: Go Here (walks the path; the same command used by the
    /// pre-context-menu right-click), Copy Room ID (writes
    /// <c>#&lt;serverId&gt;</c> to the clipboard so the user can paste it
    /// into scripts or aliases), and a non-clickable header that confirms
    /// which room the menu is acting on.
    /// </summary>
    private void ShowNodeContextMenu(MapNode node)
    {
        // Header: shows the room title + ID so the user can sanity-check
        // which node they hit. IsHitTestVisible=false prevents it being
        // selectable; FontWeight=Bold visually marks it as a header.
        var title = string.IsNullOrWhiteSpace(node.Title) ? "(unnamed room)" : node.Title;
        var serverId = !string.IsNullOrEmpty(node.ServerRoomId) ? $"  #{node.ServerRoomId}" : "";
        var header = new MenuItem
        {
            Header           = title + serverId,
            IsHitTestVisible = false,
            FontWeight       = FontWeight.Bold
        };

        var goHere = new MenuItem { Header = "Go Here" };
        goHere.Click += (_, _) =>
        {
            if (NodeClickedCommand?.CanExecute(node) == true)
                NodeClickedCommand.Execute(node);
        };

        var copyId = new MenuItem { Header = "Copy Room ID" };
        copyId.Click += async (_, _) =>
        {
            var idText = !string.IsNullOrEmpty(node.ServerRoomId)
                ? $"#{node.ServerRoomId}"
                : node.Id.ToString();
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(idText);
        };

        // "Set Waypoint" / "Show Path" are deferred — they need pathfinding
        // surface area on MapperViewModel that isn't shipped yet. Leaving
        // them here as commented-out scaffolds so it's obvious where the
        // next iteration extends the menu.
        // var setWaypoint = new MenuItem { Header = "Set as Waypoint" };
        // var showPath    = new MenuItem { Header = "Show Path"      };

        // Edit Exit ▶ {verb} submenu — one item per exit on this node.
        // The exit verb is shown so the user can pick which arc to edit
        // (rooms with multiple exits like a junction get distinct items).
        // Disabled when no EditExitCommand is wired (e.g. designer preview).
        var editExitMenu = BuildEditExitSubmenu(node);

        var items = new List<Control>
        {
            header,
            new Separator(),
            goHere,
            copyId,
        };
        if (editExitMenu is not null) items.Add(editExitMenu);

        var menu = new ContextMenu { ItemsSource = items };

        // Show the menu programmatically. Avalonia's ContextMenu.Open()
        // opens it next to the placement target's cursor position by
        // default; passing `this` anchors it to the canvas.
        menu.Open(this);
    }

    /// <summary>
    /// Build the "Edit Exit ▶" submenu for a node — one item per exit.
    /// Returns null when there are no exits or no <see cref="EditExitCommand"/>
    /// is wired (designer preview / standalone canvas).
    /// </summary>
    private MenuItem? BuildEditExitSubmenu(MapNode node)
    {
        if (EditExitCommand is null) return null;
        if (node.Exits.Count == 0) return null;

        var submenu = new MenuItem { Header = "Edit Exit" };
        var children = new List<MenuItem>();
        foreach (var exit in node.Exits)
        {
            var verb = !string.IsNullOrEmpty(exit.MoveCommand)
                ? exit.MoveCommand
                : exit.Direction.ToString().ToLowerInvariant();

            // Annotate the menu text if the exit already has requirements
            // or wait times set — quick at-a-glance signal of "this arc
            // already has community data."
            var hasMeta = !string.IsNullOrEmpty(exit.Requires)
                       || exit.RtCost.HasValue
                       || exit.WaitMin.HasValue
                       || !string.IsNullOrEmpty(exit.Notes);
            var label = hasMeta ? $"{verb}  ●" : verb;

            var item = new MenuItem { Header = label };
            // Capture node + exit by value so each click invokes against
            // the right pair. (NodeClickedCommand uses Avalonia's `Tag`
            // pattern; for two-arg commands we wrap as a tuple and let
            // the consuming MapperViewModel destructure.)
            var localNode = node;
            var localExit = exit;
            item.Click += (_, _) =>
            {
                if (EditExitCommand?.CanExecute((localNode, localExit)) == true)
                    EditExitCommand.Execute((localNode, localExit));
            };
            children.Add(item);
        }
        submenu.ItemsSource = children;
        return submenu;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _cursor = e.GetPosition(this);
        var hit = HitTest(_cursor);
        if (!ReferenceEquals(hit, _hoveredNode))
        {
            _hoveredNode = hit;
            InvalidateVisual();
        }
        else if (_hoveredNode is not null)
        {
            // Same node but cursor moved within it — repaint so the badge
            // tracks the cursor position.
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredNode is not null)
        {
            _hoveredNode = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // Vertical wheel: +1 = up = zoom in, -1 = down = zoom out.
        // Multiplicative step keeps the perceived speed constant across zoom
        // levels (small steps near 1x, big steps near 4x).
        var factor = e.Delta.Y > 0 ? ZoomStep : 1.0 / ZoomStep;
        Zoom = Zoom * factor;
        // Mark handled so the surrounding ScrollViewer doesn't also consume
        // the wheel event and try to scroll.
        e.Handled = true;
    }

    /// <summary>
    /// Hit-test against the visible (current-level) nodes. Returns the first
    /// node whose rectangle contains <paramref name="point"/>, or null.
    /// </summary>
    private MapNode? HitTest(Point point)
    {
        if (Zone is null || Zone.Nodes.Count == 0) return null;

        // Same bounds calculation as Render — required to map node X/Y to
        // canvas pixels consistently.
        int minX = int.MaxValue, minY = int.MaxValue;
        bool any = false;
        foreach (var node in Zone.Nodes.Values)
        {
            if (node.Z != Level) continue;
            any = true;
            if (node.X < minX) minX = node.X;
            if (node.Y < minY) minY = node.Y;
        }
        if (!any) return null;

        foreach (var node in Zone.Nodes.Values)
        {
            if (node.Z != Level) continue;
            if (NodeRect(node, minX, minY).Contains(point))
                return node;
        }
        return null;
    }

    private void DrawHoverBadge(DrawingContext context, MapNode node)
    {
        var title = string.IsNullOrEmpty(node.Title) ? "(no title)" : node.Title;
        var line2Parts = new List<string> { $"id {node.Id}" };
        if (!string.IsNullOrEmpty(node.ServerRoomId)) line2Parts.Add($"server {node.ServerRoomId}");
        if (!string.IsNullOrEmpty(node.Notes))        line2Parts.Add(node.Notes);
        var subText = string.Join("  ·  ", line2Parts);

        var titleText = new FormattedText(title,   System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 12, HoverTitleBrush);
        var subTextFt = new FormattedText(subText, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 10, HoverSubBrush);

        const double pad = 6.0;
        var w = Math.Max(titleText.Width, subTextFt.Width) + pad * 2;
        var h = titleText.Height + subTextFt.Height + pad * 2 + 2;

        // Offset from cursor so the badge doesn't sit under the mouse pointer.
        var x = _cursor.X + 12;
        var y = _cursor.Y + 12;
        // Keep the badge inside the control bounds so it doesn't get clipped.
        if (x + w > Bounds.Width)  x = _cursor.X - w - 12;
        if (y + h > Bounds.Height) y = _cursor.Y - h - 12;
        if (x < 0) x = 0;
        if (y < 0) y = 0;

        var rect = new Rect(x, y, w, h);
        context.FillRectangle(HoverBackgroundBrush, rect, 4);
        context.DrawRectangle(HoverBorderPen, rect, 4);

        context.DrawText(titleText, new Point(x + pad, y + pad));
        context.DrawText(subTextFt, new Point(x + pad, y + pad + titleText.Height + 2));
    }

    // ── Geometry helpers (instance so they pick up live Zoom) ─────────────

    private Point NodeCenter(MapNode node, int minX, int minY)
    {
        var x = Padding + (node.X - minX) * GridSize + GridSize / 2;
        var y = Padding + (node.Y - minY) * GridSize + GridSize / 2;
        return new Point(x, y);
    }

    private Rect NodeRect(MapNode node, int minX, int minY)
    {
        var cx = Padding + (node.X - minX) * GridSize + GridSize / 2;
        var cy = Padding + (node.Y - minY) * GridSize + GridSize / 2;
        return new Rect(cx - NodeSize / 2, cy - NodeSize / 2, NodeSize, NodeSize);
    }

    private static IBrush? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        if (Color.TryParse(hex, out var c)) return new SolidColorBrush(c);
        return null;
    }

    private void DrawCenteredMessage(DrawingContext context, string text)
    {
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12,
            EmptyMessageBrush);
        ft.TextAlignment = TextAlignment.Center;
        var origin = new Point(
            (Bounds.Width  - ft.Width)  / 2,
            (Bounds.Height - ft.Height) / 2);
        context.DrawText(ft, origin);
    }
}
