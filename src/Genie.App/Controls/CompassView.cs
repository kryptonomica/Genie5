using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Genie.App.ViewModels;

namespace Genie.App.Controls;

/// <summary>
/// Authentic Genie 4 compass: a base rose (<c>compass.png</c>) with one
/// per-direction overlay layered on top for each active exit. Images are
/// drawn pixel-perfect (nearest-neighbor scaling), matching the original
/// client's look.
///
/// <para>
/// Adapted from <c>dylb0t/Genie5</c>'s <c>CompassView</c> with the state
/// binding retargeted to our <see cref="VitalsViewModel"/>'s
/// <c>CompassExits</c> set, which is fed by <c>CompassEvent</c> from the
/// parser.
/// </para>
/// </summary>
public sealed class CompassView : Control
{
    private static readonly Bitmap BaseBmp = IconLoader.LoadAvares("compass.png");

    // Direction keys match what CompassEvent puts into VitalsViewModel.CompassExits
    // (lowercase, single-token: "n" / "ne" / "e" / "se" / "s" / "sw" / "w" /
    // "nw" / "up" / "down" / "out"). The parser splits the space-separated
    // <compass><dir value='X'/>…</compass> payload — keep the keys aligned
    // when adding new directions on either side.
    private static readonly (string Key, Bitmap Img)[] Overlays =
    {
        ("n",    IconLoader.LoadAvares("compass_north.png")),
        ("ne",   IconLoader.LoadAvares("compass_northeast.png")),
        ("e",    IconLoader.LoadAvares("compass_east.png")),
        ("se",   IconLoader.LoadAvares("compass_southeast.png")),
        ("s",    IconLoader.LoadAvares("compass_south.png")),
        ("sw",   IconLoader.LoadAvares("compass_southwest.png")),
        ("w",    IconLoader.LoadAvares("compass_west.png")),
        ("nw",   IconLoader.LoadAvares("compass_northwest.png")),
        ("up",   IconLoader.LoadAvares("compass_up.png")),
        ("down", IconLoader.LoadAvares("compass_down.png")),
        ("out",  IconLoader.LoadAvares("compass_out.png")),
    };

    public static readonly StyledProperty<VitalsViewModel?> VitalsProperty =
        AvaloniaProperty.Register<CompassView, VitalsViewModel?>(nameof(Vitals));

    public VitalsViewModel? Vitals
    {
        get => GetValue(VitalsProperty);
        set => SetValue(VitalsProperty, value);
    }

    private HashSet<string> _exits = new(StringComparer.OrdinalIgnoreCase);
    private VitalsViewModel? _attached;

    public CompassView()
    {
        // Match the authentic 28×32 source art at 1:1 to preserve pixel clarity.
        Width  = 28;
        Height = 32;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == VitalsProperty)
        {
            if (_attached is not null)
                _attached.PropertyChanged -= OnVitalsPropertyChanged;
            _attached = Vitals;
            if (_attached is not null)
            {
                _attached.PropertyChanged += OnVitalsPropertyChanged;
                SyncFromVitals();
                InvalidateVisual();
            }
        }
    }

    private void OnVitalsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VitalsViewModel.CompassExits))
        {
            SyncFromVitals();
            InvalidateVisual();
        }
    }

    private void SyncFromVitals()
    {
        if (_attached?.CompassExits is null)
        {
            _exits.Clear();
            return;
        }
        _exits = new HashSet<string>(_attached.CompassExits, StringComparer.OrdinalIgnoreCase);
    }

    public override void Render(DrawingContext ctx)
    {
        var bw = BaseBmp.PixelSize.Width;
        var bh = BaseBmp.PixelSize.Height;
        var scale = Math.Min(Bounds.Width / bw, Bounds.Height / bh);
        var dw = bw * scale;
        var dh = bh * scale;
        var dx = (Bounds.Width  - dw) / 2;
        var dy = (Bounds.Height - dh) / 2;
        var dst = new Rect(dx, dy, dw, dh);

        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            ctx.DrawImage(BaseBmp, dst);
            foreach (var (key, img) in Overlays)
                if (_exits.Contains(key))
                    ctx.DrawImage(img, dst);
        }
    }
}
