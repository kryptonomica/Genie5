using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform;
using Dock.Avalonia.Controls;

namespace Genie.App.Docking;

/// <summary>
/// Dock floating-panel window (used when a Tool/Document is floated out to its own
/// OS window) with <b>double-click-to-maximize</b> on its title bar — the gesture
/// Dock's stock <see cref="HostWindow"/> chrome doesn't wire. A floated panel (e.g.
/// the Mapper) now maximizes / restores on a title-bar double-click, matching normal
/// window behavior, in addition to the chrome's maximize button.
/// </summary>
public sealed class GenieHostWindow : HostWindow
{
    /// <summary>Dock's HostWindow chrome title-bar height is ~30px; allow slack for
    /// the window-level fallback band when the named part can't be bound.</summary>
    private const double TitleBarBandHeight = 34;

    private Control? _titleBar;

    public GenieHostWindow()
    {
        // Window-level fallback (bubbles from any child). Only acts when we couldn't
        // bind PART_TitleBar directly (see OnApplyTemplate); gated on the top band so
        // a double-tap in the panel content never maximizes.
        DoubleTapped += OnWindowDoubleTapped;

        // Issue #3: a floated panel that was maximized then closed can reopen
        // off-screen (saved restore-bounds land beyond the visible desktop on a
        // multi-monitor setup — seen as a sliver at a monitor's far edge). On open,
        // pull it back fully onto a visible screen so it can never vanish.
        Opened += (_, _) => EnsureOnVisibleScreen();
    }

    /// <summary>Clamp a normal-state floating window's bounds onto the working area
    /// of whichever screen it most overlaps (else the primary), so a stale/off-screen
    /// restore position can't leave it invisible. Maximized windows already fill a
    /// screen, so they're left alone.</summary>
    private void EnsureOnVisibleScreen()
    {
        var screens = Screens;
        if (screens?.All is not { Count: > 0 } all) return;
        if (WindowState == WindowState.Maximized) return;

        var scale = RenderScaling <= 0 ? 1.0 : RenderScaling;
        var w = Math.Max(200, (int)(Bounds.Width  * scale));
        var h = Math.Max(120, (int)(Bounds.Height * scale));
        var rect = new PixelRect(Position.X, Position.Y, w, h);

        // Best-overlapping screen (handles the "sliver at the edge" case), else primary.
        Screen? best = null;
        long bestArea = -1;
        foreach (var s in all)
        {
            var i = s.WorkingArea.Intersect(rect);
            long a = (long)i.Width * i.Height;
            if (a > bestArea) { bestArea = a; best = s; }
        }
        var wa = (best ?? screens.Primary ?? all[0]).WorkingArea;

        var x = Math.Clamp(Position.X, wa.X, Math.Max(wa.X, wa.X + wa.Width  - w));
        var y = Math.Clamp(Position.Y, wa.Y, Math.Max(wa.Y, wa.Y + wa.Height - h));
        if (x != Position.X || y != Position.Y)
            Position = new PixelPoint(x, y);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_titleBar is not null)
            _titleBar.DoubleTapped -= OnTitleBarDoubleTapped;

        // Dock's HostWindow chrome names its title bar PART_TitleBar. Bind the
        // double-tap there precisely so a double-tap in the content area is ignored.
        _titleBar = e.NameScope.Find<Control>("PART_TitleBar");
        if (_titleBar is not null)
            _titleBar.DoubleTapped += OnTitleBarDoubleTapped;
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximize(e);

    private void OnWindowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled || _titleBar is not null) return;            // precise handler covers it
        if (e.GetPosition(this).Y <= TitleBarBandHeight)
            ToggleMaximize(e);
    }

    private void ToggleMaximize(TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        e.Handled = true;
    }
}
