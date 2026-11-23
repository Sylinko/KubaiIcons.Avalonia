using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using KubaiIcons.Avalonia;
using NUnit.Framework;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    [Test]
    public void TypedKinds_MatchShippedFamilies_AndConvertOnlySharedMembers()
    {
        var catalog = KubaiIconCatalog.All;
        Assert.That(
            Enum.GetValues<KubaiLayeredIconKind>().Skip(1).Select(k => (int)k),
            Is.EqualTo(catalog.Where(i => i.HasLayered).Select(i => (int)i.Kind)));
        Assert.That(
            Enum.GetValues<KubaiDuotoneIconKind>().Skip(1).Select(k => (int)k),
            Is.EqualTo(catalog.Where(i => i.HasDuotone).Select(i => (int)i.Kind)));
        foreach (var icon in catalog)
        {
            var expectedStyles = (icon.HasLayered ? KubaiIconStyles.Layered : KubaiIconStyles.None)
                | (icon.HasDuotone ? KubaiIconStyles.Duotone : KubaiIconStyles.None);
            Assert.That(icon.SupportedStyles, Is.EqualTo(expectedStyles));
            var isShared = icon.HasDuotone && icon.HasLayered;
            Assert.That(((KubaiLayeredIconKind)icon.Kind).TryToDuotone(out var duotone), Is.EqualTo(isShared));
            Assert.That(((KubaiDuotoneIconKind)icon.Kind).TryToLayered(out var layered), Is.EqualTo(isShared));
            if (isShared)
            {
                Assert.That((int)duotone, Is.EqualTo((int)layered).And.EqualTo((int)icon.Kind));
            }
        }

        Assert.That(default(KubaiIconInfo).SupportedStyles, Is.EqualTo(KubaiIconStyles.None));
        Assert.That(KubaiDuotoneIconKind.None.TryToLayered(out var empty) && empty == KubaiLayeredIconKind.None, Is.True);
    }

    [Test]
    public async Task SampledLayered_WhenRendering_ReproducesTravelAndLeavesUnsupportedValuesBlank()
    {
        await _session.Dispatch(
            () =>
            {
                const int size = 96;
                var canvas = new Canvas
                {
                    Width = size * 13,
                    Height = size * 4,
                    Background = Brushes.White
                };
                var window = new Window
                {
                    Content = canvas,
                    Width = canvas.Width,
                    Height = canvas.Height,
                    CanResize = false
                };
                KubaiLayeredIconKind[] kinds =
                [
                    KubaiLayeredIconKind.Activity, KubaiLayeredIconKind.AlignCenter,
                    KubaiLayeredIconKind.AlignJustify, KubaiLayeredIconKind.AlignLeft, KubaiLayeredIconKind.AlignRight,
                    KubaiLayeredIconKind.ChartLine, KubaiLayeredIconKind.Eject, KubaiLayeredIconKind.FastForward,
                    KubaiLayeredIconKind.Pause, KubaiLayeredIconKind.Play, KubaiLayeredIconKind.Rewind,
                    KubaiLayeredIconKind.TrendingDown, KubaiLayeredIconKind.TrendingUp
                ];
                var middle = new List<KubaiLayeredIcon>();
                for (var column = 0; column < kinds.Length; column++)
                {
                    for (var row = 0; row < 4; row++)
                    {
                        var icon = new KubaiLayeredIcon
                        {
                            Kind = kinds[column],
                            Size = size,
                            IsAnimationEnabled = false,
                            IsEngaged = row == 2,
                            ColorVariant = row == 3 ? KubaiColorVariant.Soft : KubaiColorVariant.Color
                        };
                        icon.Classes.Add("sample");
                        canvas.Children.Add(icon);
                        Canvas.SetLeft(icon, column * size);
                        Canvas.SetTop(icon, row * size);
                        if (row == 1)
                        {
                            middle.Add(icon);
                        }
                    }
                }

                var green = Color.Parse("#22AA77");
                canvas.Styles.Add(
                    new Style(x => x.OfType<KubaiLayeredIcon>().Class("sample"))
                    {
                        Setters = { new Setter(KubaiIconBase.AccentColorProperty, green) }
                    });
                try
                {
                    window.Show();
                    Pump();
                    var pose = typeof(KubaiIconBase).GetField("_pose", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new AssertionException("Missing player pose.");
                    foreach (var icon in middle)
                    {
                        Assert.That(icon.AccentColor, Is.EqualTo(green));
                        pose.SetValue(icon, Activator.CreateInstance(pose.FieldType, 0.5, 0d, 0d));
                        icon.InvalidateVisual();
                    }

                    Pump();
                    using var sheet = window.CaptureRenderedFrame() ?? throw new AssertionException("Missing native render.");
                    var directory = Environment.GetEnvironmentVariable("KUBAI_CAPTURE_DIR");
                    if (directory is not null)
                    {
                        sheet.Save(Path.Combine(directory, "sampled-layered.png"), PngBitmapEncoderOptions.Default);
                    }

                    byte[] Tile(int col, int row)
                    {
                        using var buffer = sheet.Lock();
                        var pixels = new byte[size * size * 4];
                        for (var y = 0; y < size; y++)
                        {
                            Marshal.Copy(
                                buffer.Address + (row * size + y) * buffer.RowBytes + col * size * 4,
                                pixels,
                                y * size * 4,
                                size * 4);
                        }

                        return pixels;
                    }

                    for (var col = 0; col < kinds.Length; col++)
                    {
                        Assert.That(WhiteMarks(Tile(col, 0)), Is.Not.EqualTo(WhiteMarks(Tile(col, 1))), kinds[col] + " travel");
                        Assert.That(Tile(col, 0), Is.Not.EqualTo(Tile(col, 3)), kinds[col] + " palette");
                    }

                    // Justify converges to the very same shape at rest and held, but reflows in between.
                    static bool[] WhiteMarks(byte[] pixels)
                    {
                        return (from y in Enumerable.Range(22, 52)
                                from x in Enumerable.Range(20, 56)
                                let i = 4 * (y * size + x)
                                select Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2])) >= 245).ToArray();
                    }

                    Assert.That(WhiteMarks(Tile(2, 0)), Is.EqualTo(WhiteMarks(Tile(2, 2))));
                    var numeric = KubaiIconCatalog.All.Where(icon => !icon.HasDuotone && icon.HasLayered)
                        .Select(icon => (KubaiDuotoneIconKind)(int)icon.Kind)
                        .DefaultIfEmpty((KubaiDuotoneIconKind)int.MaxValue)
                        .First();
                    var missing = new KubaiDuotoneIcon
                    {
                        Kind = numeric,
                        Size = size
                    };
                    var empty = new KubaiDuotoneIcon
                    {
                        Kind = KubaiDuotoneIconKind.None,
                        Size = size
                    };
                    canvas.Children.Add(missing);
                    canvas.Children.Add(empty);
                    Pump();

                    static byte[] Draw(Control control)
                    {
                        using var target = new RenderTargetBitmap(new PixelSize(size, size));
                        target.Render(control);
                        using var stream = new MemoryStream();
                        target.Save(stream, PngBitmapEncoderOptions.Default);
                        return stream.ToArray();
                    }

                    Assert.That(missing.Kind, Is.EqualTo(numeric));
                    Assert.That(Draw(missing), Is.EqualTo(Draw(empty)));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }
}