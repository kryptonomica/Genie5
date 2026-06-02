using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Genie.App.ViewModels;

namespace Genie.App.Controls;

/// <summary>
/// Horizontal row of status-effect icons. Order and fixed-slot layout match
/// Genie 4's <c>ComponentIconBar</c> (TabIndex 2..7): stunned, bleeding,
/// invisible, hidden, joined, webbed. Each icon is drawn in its own slot
/// only while the corresponding flag on the bound <see cref="VitalsViewModel"/>
/// is active; inactive slots stay blank.
///
/// <para>
/// Adapted from <c>dylb0t/Genie5</c>'s <c>StatusIconBar</c> — the data source
/// switches from his flat <c>GslGameState</c> bools to our reactive VM bools,
/// but the icon ordering, slot widths, and pixel-perfect render path are
/// preserved.
/// </para>
/// </summary>
public sealed class StatusIconBar : Control
{
    private static readonly (string File, string PropName, Func<VitalsViewModel, bool> Active)[] Icons =
    {
        ("stunned.png",   nameof(VitalsViewModel.IsStunned),   v => v.IsStunned),
        ("bleeding.png",  nameof(VitalsViewModel.IsBleeding),  v => v.IsBleeding),
        ("invisible.png", nameof(VitalsViewModel.IsInvisible), v => v.IsInvisible),
        ("hidden.png",    nameof(VitalsViewModel.IsHidden),    v => v.IsHidden),
        ("joined.png",    nameof(VitalsViewModel.IsJoined),    v => v.IsJoined),
        ("webbed.png",    nameof(VitalsViewModel.IsWebbed),    v => v.IsWebbed),
    };

    private const double IconW = 28;
    private const double IconH = 32;
    private const double Gap   = 4;

    private static readonly Bitmap[] Sprites = LoadSprites();

    public static readonly StyledProperty<VitalsViewModel?> VitalsProperty =
        AvaloniaProperty.Register<StatusIconBar, VitalsViewModel?>(nameof(Vitals));

    public VitalsViewModel? Vitals
    {
        get => GetValue(VitalsProperty);
        set => SetValue(VitalsProperty, value);
    }

    private VitalsViewModel? _attached;

    public StatusIconBar()
    {
        Width  = Icons.Length * IconW + (Icons.Length - 1) * Gap;
        Height = IconH;
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
                InvalidateVisual();
            }
        }
    }

    private void OnVitalsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only redraw on flips that affect our slot set; ignore unrelated
        // VM properties (vitals percentages, hand contents, etc.) so this
        // control isn't repainting on every health-bar tick.
        foreach (var icon in Icons)
        {
            if (e.PropertyName == icon.PropName)
            {
                InvalidateVisual();
                return;
            }
        }
    }

    public override void Render(DrawingContext ctx)
    {
        if (_attached is null) return;

        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            for (int i = 0; i < Icons.Length; i++)
            {
                if (!Icons[i].Active(_attached)) continue;
                var x = i * (IconW + Gap);
                ctx.DrawImage(Sprites[i], new Rect(x, 0, IconW, IconH));
            }
        }
    }

    private static Bitmap[] LoadSprites()
    {
        var arr = new Bitmap[Icons.Length];
        for (int i = 0; i < Icons.Length; i++)
            arr[i] = IconLoader.LoadAvares(Icons[i].File);
        return arr;
    }
}
