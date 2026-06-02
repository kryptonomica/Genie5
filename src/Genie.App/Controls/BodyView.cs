using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Genie.App.ViewModels;

namespace Genie.App.Controls;

/// <summary>
/// Draws the authentic Genie 4 posture sprite (standing / sitting / kneeling
/// / prone / dead) from <c>Assets/Icons/&lt;state&gt;.png</c>. Black pixels
/// are made transparent at load time so the sprite blends cleanly onto any
/// background.
///
/// <para>
/// Adapted from <c>dylb0t/Genie5</c>'s <c>BodyView</c> with the state binding
/// retargeted to our <see cref="VitalsViewModel"/>. The original code
/// subscribed to a flat <c>GslGameState.StateChanged</c> event; ours
/// subscribes to per-property notifications on the reactive view model.
/// </para>
/// </summary>
public sealed class BodyView : Control
{
    private enum Posture { Standing, Sitting, Kneeling, Prone, Dead }

    private static readonly Bitmap StandingBmp = IconLoader.LoadAvares("standing.png");
    private static readonly Bitmap SittingBmp  = IconLoader.LoadAvares("sitting.png");
    private static readonly Bitmap KneelingBmp = IconLoader.LoadAvares("kneeling.png");
    private static readonly Bitmap ProneBmp    = IconLoader.LoadAvares("prone.png");
    private static readonly Bitmap DeadBmp     = IconLoader.LoadAvares("dead.png");

    /// <summary>Source of truth — pose flips on PropertyChanged for the four state bools.</summary>
    public static readonly StyledProperty<VitalsViewModel?> VitalsProperty =
        AvaloniaProperty.Register<BodyView, VitalsViewModel?>(nameof(Vitals));

    public VitalsViewModel? Vitals
    {
        get => GetValue(VitalsProperty);
        set => SetValue(VitalsProperty, value);
    }

    private Posture _posture = Posture.Standing;
    private VitalsViewModel? _attached;

    public BodyView()
    {
        // Match the authentic 28×32 sprite at 1:1 to preserve pixel clarity.
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
                RecomputeAndInvalidate();
            }
        }
    }

    private void OnVitalsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VitalsViewModel.IsDead)
                            or nameof(VitalsViewModel.IsProne)
                            or nameof(VitalsViewModel.IsKneeling)
                            or nameof(VitalsViewModel.IsSitting))
        {
            RecomputeAndInvalidate();
        }
    }

    private void RecomputeAndInvalidate()
    {
        if (_attached is null) return;
        _posture =
            _attached.IsDead     ? Posture.Dead     :
            _attached.IsProne    ? Posture.Prone    :
            _attached.IsKneeling ? Posture.Kneeling :
            _attached.IsSitting  ? Posture.Sitting  :
                                   Posture.Standing;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var bmp = _posture switch
        {
            Posture.Dead     => DeadBmp,
            Posture.Prone    => ProneBmp,
            Posture.Kneeling => KneelingBmp,
            Posture.Sitting  => SittingBmp,
            _                => StandingBmp,
        };

        var bw = bmp.PixelSize.Width;
        var bh = bmp.PixelSize.Height;
        var scale = Math.Min(Bounds.Width / bw, Bounds.Height / bh);
        var dw = bw * scale;
        var dh = bh * scale;
        var dx = (Bounds.Width  - dw) / 2;
        var dy = (Bounds.Height - dh) / 2;
        var dst = new Rect(dx, dy, dw, dh);

        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            ctx.DrawImage(bmp, dst);
    }
}
