using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace IconStudio;

/// <summary>
/// Blurs the already drawn gallery under the docked header; the template paints the tint above it.
/// </summary>
/// <remarks>
/// Arc 1.3.0 GlassCard blurs a VisualBrush copy. On this virtualized gallery the fixed viewport
/// kept stale rows; mirroring the moving presenter fixed alignment but slowed desktop scrolling.
/// Sampling the existing render target avoids both a second gallery draw and an idle refresh loop.
/// </remarks>
internal sealed class StudioGlass : TemplatedControl
{
    public static readonly StyledProperty<bool> IsOpaqueProperty =
        AvaloniaProperty.Register<StudioGlass, bool>(nameof(IsOpaque));

    public bool IsOpaque
    {
        get => GetValue(IsOpaqueProperty);
        set => SetValue(IsOpaqueProperty, value);
    }

    static StudioGlass() => AffectsRender<StudioGlass>(IsOpaqueProperty, CornerRadiusProperty);

    public override void Render(DrawingContext context)
    {
        if (IsOpaque)
        {
            return;
        }

        var rect = new Rect(Bounds.Size);
        using (context.PushClip(new RoundedRect(rect, CornerRadius)))
        {
            context.Custom(new Backdrop(rect));
        }
    }

    // Filter the existing render target in place. No screenshots, readback, duplicate
    // gallery visuals or animation timer; the compositor redraws it with the scrolling tiles.
    private sealed class Backdrop(Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;
        private readonly SKImageFilter _blur = SKImageFilter.CreateBlur(6, 6);

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => other is Backdrop backdrop && Bounds == backdrop.Bounds;

        public void Dispose() => _blur.Dispose();

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            {
                return;
            }

            using var lease = feature.Lease();
            var layer = new SKCanvasSaveLayerRec { Backdrop = _blur };
            lease.SkCanvas.SaveLayer(in layer);
            lease.SkCanvas.Restore();
        }
    }
}