using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.VisualTree;
using Noctis.Helpers;
using SkiaSharp;

namespace Noctis.Controls;

/// <summary>
/// A surface that frosts whatever the app has already drawn beneath it when Liquid
/// Glass is on, and paints a plain rounded <see cref="Background"/> when it is off.
///
/// Off is byte-identical to a Border with the same Background/CornerRadius, so hosts
/// that leave the toggle off see no change. On, the panel draws three layers: a
/// backdrop blur of the surface pixels under its bounds (a Skia snapshot re-drawn
/// through a Gaussian blur, clipped to the rounded rect), a tint of the theme surface
/// colour, and a 1px light inner edge. Popups and separate windows cannot be frosted
/// this way — the snapshot only sees the surface this control renders into.
///
/// Follows <see cref="AppGlass"/> by default; set <see cref="UseAppGlass"/> false to
/// drive <see cref="IsGlassActive"/> directly (tests, previews).
/// </summary>
public class GlassPanel : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<GlassPanel>();

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<GlassPanel>();

    public static readonly StyledProperty<bool> UseAppGlassProperty =
        AvaloniaProperty.Register<GlassPanel, bool>(nameof(UseAppGlass), true);

    public static readonly StyledProperty<bool> IsGlassActiveProperty =
        AvaloniaProperty.Register<GlassPanel, bool>(nameof(IsGlassActive));

    /// <summary>Tint colour over the blur. Null: the Background's colour when it is a
    /// solid brush, else the theme surface tint.</summary>
    public static readonly StyledProperty<Color?> GlassTintProperty =
        AvaloniaProperty.Register<GlassPanel, Color?>(nameof(GlassTint));

    /// <summary>Tint opacity over the blur. Null: the Background brush's own opacity
    /// (so a user opacity slider bound to the brush keeps working).</summary>
    public static readonly StyledProperty<double?> GlassTintOpacityProperty =
        AvaloniaProperty.Register<GlassPanel, double?>(nameof(GlassTintOpacity));

    /// <summary>Blur radius in logical pixels; scaled to device pixels at render time.
    /// 0 (or less) skips the backdrop snapshot entirely: the panel is then a plain
    /// translucent tint over whatever is beneath — no per-frame surface copy, so cheap
    /// enough for many small hosts (the far Cover Flow cards).</summary>
    public static readonly StyledProperty<double> BlurRadiusProperty =
        AvaloniaProperty.Register<GlassPanel, double>(nameof(BlurRadius), 18);

    public static readonly StyledProperty<IBrush?> EdgeBrushProperty =
        AvaloniaProperty.Register<GlassPanel, IBrush?>(nameof(EdgeBrush),
            new ImmutableSolidColorBrush(Colors.White, 0.22));

    public static readonly StyledProperty<double> EdgeThicknessProperty =
        AvaloniaProperty.Register<GlassPanel, double>(nameof(EdgeThickness), 1);

    /// <summary>
    /// 0..1 fade applied INSIDE the panel's own drawing (blur paint alpha, tint and edge
    /// opacity, plain fill opacity). Use this to animate a glass surface in or out instead
    /// of Opacity on the panel or an ancestor: the GPU backend does not apply an opacity
    /// layer to the custom Skia blur, so an Opacity fade leaves the frost at full strength
    /// while everything around it disappears.
    /// </summary>
    public static readonly StyledProperty<double> FadeProperty =
        AvaloniaProperty.Register<GlassPanel, double>(nameof(Fade), 1.0);

    static GlassPanel()
    {
        AffectsRender<GlassPanel>(BackgroundProperty, CornerRadiusProperty, UseAppGlassProperty,
            IsGlassActiveProperty, GlassTintProperty, GlassTintOpacityProperty, BlurRadiusProperty,
            EdgeBrushProperty, EdgeThicknessProperty, FadeProperty);
    }

    public double Fade { get => GetValue(FadeProperty); set => SetValue(FadeProperty, value); }

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public bool UseAppGlass { get => GetValue(UseAppGlassProperty); set => SetValue(UseAppGlassProperty, value); }
    public bool IsGlassActive { get => GetValue(IsGlassActiveProperty); set => SetValue(IsGlassActiveProperty, value); }
    public Color? GlassTint { get => GetValue(GlassTintProperty); set => SetValue(GlassTintProperty, value); }
    public double? GlassTintOpacity { get => GetValue(GlassTintOpacityProperty); set => SetValue(GlassTintOpacityProperty, value); }
    public double BlurRadius { get => GetValue(BlurRadiusProperty); set => SetValue(BlurRadiusProperty, value); }
    public IBrush? EdgeBrush { get => GetValue(EdgeBrushProperty); set => SetValue(EdgeBrushProperty, value); }
    public double EdgeThickness { get => GetValue(EdgeThicknessProperty); set => SetValue(EdgeThicknessProperty, value); }

    /// <summary>The state this panel renders with right now.</summary>
    public bool EffectiveGlassActive => UseAppGlass ? AppGlass.IsActive : IsGlassActive;

    private EventHandler? _glassChanged;
    private bool _attached, _pumping;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _glassChanged = (_, _) => { InvalidateVisual(); EnsurePump(); };
        AppGlass.Changed += _glassChanged;
        EnsurePump();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        if (_glassChanged != null) AppGlass.Changed -= _glassChanged;
        _glassChanged = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UseAppGlassProperty || change.Property == IsGlassActiveProperty || change.Property == BlurRadiusProperty)
            EnsurePump();
    }

    /// <summary>
    /// While frosting, redraw the whole panel every frame. The compositor otherwise
    /// repaints only the dirty rect of a hovered child, and the backdrop snapshot inside
    /// that rect then contains last frame's already-frosted pixels: blurred and tinted a
    /// second time, the rect shows up as a lighter box. A full redraw each frame keeps
    /// every pixel under the panel fresh when the snapshot is taken. Stops itself when
    /// glass turns off or the panel leaves the tree, so the off state costs nothing.
    /// </summary>
    private void EnsurePump()
    {
        // Tint-only panels (BlurRadius 0) never snapshot, so a partial redraw cannot
        // double-frost them: no pump needed.
        if (_pumping || !_attached || !EffectiveGlassActive || BlurRadius <= 0) return;
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        _pumping = true;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan _)
    {
        if (!_attached || !EffectiveGlassActive || BlurRadius <= 0 || TopLevel.GetTopLevel(this) is not { } top)
        {
            _pumping = false;
            InvalidateVisual();
            return;
        }
        InvalidateVisual();
        top.RequestAnimationFrame(OnFrame);
    }

    /// <summary>Resolves the tint colour and opacity the on-state paints.</summary>
    public (Color Color, double Opacity) ResolveTint()
    {
        var solid = Background as ISolidColorBrush;
        var color = GlassTint ?? solid?.Color ?? AppGlass.SurfaceTint;
        var opacity = GlassTintOpacity ?? (solid is null ? 0.5 : solid.Opacity * (solid.Color.A / 255.0));
        // The tint colour's own alpha is folded into the opacity above.
        return (Color.FromRgb(color.R, color.G, color.B), Math.Clamp(opacity, 0, 1));
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var rrect = new RoundedRect(rect, CornerRadius);

        var fade = Math.Clamp(Fade, 0, 1);
        if (fade <= 0) return;

        if (!EffectiveGlassActive)
        {
            if (Background == null) return;
            using (fade < 1 ? context.PushOpacity(fade) : default)
                context.DrawRectangle(Background, null, rrect);
            return;
        }

        // The fade is folded into every layer here rather than applied as Opacity: see Fade.
        if (BlurRadius > 0)
            context.Custom(new GlassBackdropOp(rect, CornerRadius, BlurRadius, fade));

        var (tint, opacity) = ResolveTint();
        opacity *= fade;
        if (opacity > 0)
            context.DrawRectangle(new ImmutableSolidColorBrush(tint, opacity), null, rrect);

        if (EdgeBrush != null && EdgeThickness > 0)
        {
            var pen = new ImmutablePen(EdgeBrush.ToImmutable(), EdgeThickness);
            var half = EdgeThickness / 2;
            using (fade < 1 ? context.PushOpacity(fade) : default)
                context.DrawRectangle(null, pen, rrect.Deflate(half, half));
        }
    }
}

/// <summary>
/// The Skia half of the frost, kept free of Avalonia so a raster SKSurface can test it.
/// </summary>
public static class GlassBlur
{
    /// <summary>Hard cap on the blur sigma in device pixels: bounds the per-frame cost on
    /// large panels (a 1000×720 sheet at 125% scaling).</summary>
    public const float MaxSigma = 30f;

    /// <summary>
    /// Re-draws the pixels of <paramref name="surface"/> under <paramref name="deviceRect"/>
    /// back onto its canvas through a Gaussian blur, clipped to <paramref name="clip"/>.
    /// Coordinates are device pixels. Returns false when the surface cannot be snapshotted.
    /// </summary>
    public static bool Draw(SKCanvas canvas, SKSurface surface, SKRect deviceRect, SKRoundRect clip, float sigma, float alpha = 1f)
    {
        using var path = new SKPath();
        path.AddRoundRect(clip);
        return Draw(canvas, surface, deviceRect, path, sigma, alpha);
    }

    /// <summary>
    /// Same as the rounded-rect overload, clipping to an arbitrary device-space
    /// <paramref name="clip"/> path — the host's rounded rect run through its full render
    /// matrix, so a panel under a rotate/perspective transform (a tilted Cover Flow card)
    /// frosts exactly its own tilted outline instead of an axis-aligned box around it.
    /// </summary>
    public static bool Draw(SKCanvas canvas, SKSurface surface, SKRect deviceRect, SKPath clip, float sigma, float alpha = 1f)
    {
        sigma = Math.Clamp(sigma, 0.5f, MaxSigma);
        alpha = Math.Clamp(alpha, 0f, 1f);
        if (alpha <= 0f) return true;
        using var snapshot = surface.Snapshot();
        if (snapshot is null) return false;

        // Bleed: a blur sampled only from the exact rect collapses toward its edges (the
        // frost-band lesson), so feed it 3σ of surrounding pixels and let the clip trim.
        var bleed = (float)Math.Ceiling(sigma * 3);
        var src = SKRect.Inflate(deviceRect, bleed, bleed);
        src.Intersect(new SKRect(0, 0, snapshot.Width, snapshot.Height));
        if (src.IsEmpty) return false;

        canvas.Save();
        canvas.SetMatrix(SKMatrix.Identity);
        canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        using var paint = new SKPaint
        {
            ImageFilter = filter,
            FilterQuality = SKFilterQuality.Low,
            Color = new SKColor(255, 255, 255, (byte)Math.Round(alpha * 255)),
        };
        canvas.DrawImage(snapshot, src, src, paint);
        canvas.Restore();
        return true;
    }
}

/// <summary>Render-thread op: everything it needs is captured by value at construction.</summary>
internal sealed class GlassBackdropOp : ICustomDrawOperation
{
    private readonly CornerRadius _corners;
    private readonly double _blurRadius, _fade;

    public GlassBackdropOp(Rect bounds, CornerRadius corners, double blurRadius, double fade = 1)
    {
        Bounds = bounds;
        _corners = corners;
        _blurRadius = blurRadius;
        _fade = fade;
    }

    public Rect Bounds { get; }
    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) =>
        other is GlassBackdropOp o && o.Bounds == Bounds && o._corners == _corners && o._blurRadius == _blurRadius && o._fade == _fade;
    public void Dispose() { }

    public void Render(ImmediateDrawingContext context)
    {
        var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (lease is null) return;
        using var api = lease.Lease();
        var canvas = api.SkCanvas;
        var surface = api.SkSurface;
        // No surface (rendering into an intermediate layer): the tint alone carries the look.
        if (surface is null) return;

        // Build the rounded rect in LOCAL space and push it through the full matrix
        // (DPI, scale, and any rotate/perspective the host sits under) so the clip is
        // the panel's true outline on screen; the blur source is that path's bounds.
        var m = canvas.TotalMatrix;
        var local = new SKRoundRect();
        local.SetRectRadii(new SKRect((float)Bounds.X, (float)Bounds.Y, (float)Bounds.Right, (float)Bounds.Bottom), new[]
        {
            new SKPoint((float)_corners.TopLeft, (float)_corners.TopLeft),
            new SKPoint((float)_corners.TopRight, (float)_corners.TopRight),
            new SKPoint((float)_corners.BottomRight, (float)_corners.BottomRight),
            new SKPoint((float)_corners.BottomLeft, (float)_corners.BottomLeft),
        });
        using var path = new SKPath();
        path.AddRoundRect(local);
        path.Transform(m);
        var dev = path.Bounds;
        var scale = Math.Max(Math.Abs(m.ScaleX), Math.Abs(m.ScaleY));
        if (scale < 0.01f) scale = 1f;

        GlassBlur.Draw(canvas, surface, dev, path, (float)(_blurRadius * scale), (float)_fade);
    }
}
