using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KubaiIcons.Avalonia;
using NUnit.Framework;
using SkiaSharp;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    // Chromium reference rendered from the unmodified pinned withBadge + renderSVG.
    // Row 1: entry; row 2: held and return; row 3: reverse before entry finishes.
    [Test]
    public async Task FolderAlert_MatchesUpstreamDiscGlyphKnockoutAndBaseDuringMotion()
    {
        await _session.Dispatch(
            () =>
            {
                const int side = 155;
                var timeline = new (double Start, double Time)[]
                {
                    (0, 0), (0, .16), (0, .34), (0, .54), (0, .75), (0, 1),
                    (1, .2), (1, .55), (0, .3), (.3, .1), (.3, .25), (.3, .8)
                };
                var icons = new List<KubaiLayeredIcon>();
                var canvas = new Canvas
                {
                    Width = side * 4,
                    Height = side * 3
                };
                var window = new Window
                {
                    Width = canvas.Width,
                    Height = canvas.Height,
                    Content = canvas,
                    CanResize = false
                };
                try
                {
                    for (var i = 0; i < timeline.Length; i++)
                    {
                        var icon = new KubaiLayeredIcon
                        {
                            Kind = KubaiLayeredIconKind.FolderAlert,
                            Size = side,
                            IsAnimationEnabled = false,
                            AccentColor = Color.Parse("#5393FF"),
                            BadgeColor = Color.Parse("#F01487")
                        };
                        var cell = new Grid
                        {
                            Width = side,
                            Height = side,
                            Children = { new Checkerboard(), icon }
                        };
                        icons.Add(icon);
                        canvas.Children.Add(cell);
                        var (row, column) = Math.DivRem(i, 4);
                        Canvas.SetLeft(cell, column * side);
                        Canvas.SetTop(cell, row * side);
                    }

                    window.Show();
                    Pump();
                    for (var i = 0; i < timeline.Length; i++)
                    {
                        var (start, time) = timeline[i];
                        SetSample(icons[i], start, time);
                    }

                    Pump();
                    using var shot = window.CaptureRenderedFrame()!;
                    var directory = Environment.GetEnvironmentVariable("KUBAI_CAPTURE_DIR");
                    if (directory is not null)
                    {
                        shot.Save(Path.Combine(directory, "folder-alert-native.png"), PngBitmapEncoderOptions.Default);
                    }

                    using var stream = new MemoryStream();
                    shot.Save(stream, PngBitmapEncoderOptions.Default);
                    stream.Position = 0;
                    using var actual = SKBitmap.Decode(stream);
                    using var reference = SKBitmap.Decode(
                        Path.Combine(TestContext.CurrentContext.TestDirectory, "Contracts", "folder-alert-reference.png"));
                    for (var i = 0; i < timeline.Length; i++)
                    {
                        SKColor Pixel(SKBitmap b, int x, int y) => b.GetPixel(i % 4 * side + x, i / 4 * side + y);

                        double Overlap(Func<int, int, SKColor, bool> segment)
                        {
                            var both = 0;
                            var either = 0;
                            for (var y = 0; y < side; y++)
                            {
                                for (var x = 0; x < side; x++)
                                {
                                    var a = segment(x, y, Pixel(actual, x, y));
                                    var b = segment(x, y, Pixel(reference, x, y));
                                    if (a || b)
                                    {
                                        either++;
                                    }

                                    if (a && b)
                                    {
                                        both++;
                                    }
                                }
                            }

                            return both / (double)either;
                        }

                        var disc = Overlap((_, _, p) => p.Red > 180 && p.Green < 100 && p.Blue > 95);
                        var glyph = Overlap((x, y, p) => Math.Pow(x - 113, 2) + Math.Pow(y - 107, 2) < 22 * 22 && p.Green > 220 && p.Red > 220);
                        var body = Overlap((_, _, p) => p.Blue > 175 && p.Blue - p.Red > 20);
                        var paper = Overlap((x, y, p) => x is > 28 and < 100 && y < 85 && p.Red > 238 && p.Green > 238);
                        // Left semicircle: the unmasked folder spans both sides of this ring.
                        // A painted ring cannot reproduce both colors of the checkerboard.
                        var gap = Overlap((x, y, p) => x is > 69 and < 115 && y is > 79 and < 122 &&
                            ((x / 10 + y / 10) % 2 == 0 ? p == new SKColor(64, 49, 73) : p == new SKColor(25, 38, 55)));
                        TestContext.Out.WriteLine($"frame {i}: disc {disc:F3}, glyph {glyph:F3}, gap {gap:F3}, body {body:F3}, paper {paper:F3}");
                        Assert.That(disc, Is.GreaterThan(.975), $"disc frame {i}");
                        Assert.That(glyph, Is.GreaterThan(.92), $"glyph frame {i}");
                        Assert.That(gap, Is.GreaterThan(.91), $"knockout frame {i}");
                        Assert.That(body, Is.GreaterThan(.955), $"folder frame {i}");
                        if (!double.IsNaN(paper))
                        {
                            Assert.That(paper, Is.GreaterThan(.84), $"paper frame {i}");
                        }
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Test]
    public async Task Compositions_MatchPublishedGlyphsInMotionOnTheirNativeBases()
    {
        await _session.Dispatch(
            () =>
            {
                const int side = 124;
                var names = System.Text.Json.JsonSerializer.Deserialize<string[]>(
                    File.ReadAllText(
                        Path.Combine(TestContext.CurrentContext.TestDirectory, "Contracts/compositions-reference.json")))!;
                var catalog = KubaiIconCatalog.All.ToDictionary(info => info.Name);
                Assert.That(names.Length, Is.EqualTo(36));
                var canvas = new Canvas
                {
                    Width = 6 * side,
                    Height = 6 * side,
                    Background = new SolidColorBrush(Color.Parse("#192637"))
                };
                var window = new Window
                {
                    Width = canvas.Width,
                    Height = canvas.Height,
                    Content = canvas,
                    CanResize = false
                };
                var icons = new List<KubaiLayeredIcon>();
                try
                {
                    foreach (var name in names)
                    {
                        var info = catalog[name];
                        Assert.That(info.HasLayered, Is.True);
                        var icon = new KubaiLayeredIcon
                        {
                            Kind = (KubaiLayeredIconKind)info.Kind,
                            Size = side,
                            IsAnimationEnabled = false,
                            IsActive = true,
                            BadgeColor = Color.Parse("#F01487"),
                            AccentColor = Color.Parse("#5393FF")
                        };
                        var (row, column) = Math.DivRem(icons.Count, 6);
                        Canvas.SetLeft(icon, column * side);
                        Canvas.SetTop(icon, row * side);
                        icons.Add(icon);
                        canvas.Children.Add(icon);
                    }

                    window.Show();
                    Pump();
                    double[] times = [.07, .26, .54];
                    for (var i = 0; i < icons.Count; i++)
                    {
                        SetSample(icons[i], 0, times[i % 3]);
                    }

                    Pump();
                    using var shot = window.CaptureRenderedFrame()!;
                    var directory = Environment.GetEnvironmentVariable("KUBAI_CAPTURE_DIR");
                    if (directory is not null)
                    {
                        shot.Save(Path.Combine(directory, "compositions-native.png"), PngBitmapEncoderOptions.Default);
                    }

                    using var stream = new MemoryStream();
                    shot.Save(stream, PngBitmapEncoderOptions.Default);
                    stream.Position = 0;
                    using var actual = SKBitmap.Decode(stream);
                    using var reference = SKBitmap.Decode(
                        Path.Combine(TestContext.CurrentContext.TestDirectory, "Contracts", "compositions-reference.png"));
                    for (var i = 0; i < names.Length; i++)
                    {
                        var intersections = new int[3];
                        var unions = new int[3];
                        for (var y = 0; y < side; y++)
                        {
                            for (var x = 0; x < side; x++)
                            {
                                int Classify(SKColor p)
                                {
                                    if (p.Red > 180 && p.Green < 100 && p.Blue > 95)
                                    {
                                        return 0;
                                    }

                                    if (p.Red > 230 && p.Green > 230 && Math.Pow(x - 90.4, 2) + Math.Pow(y - 85.6, 2) < 20 * 20)
                                    {
                                        return 1;
                                    }

                                    return p.Blue > 175 && p.Blue - p.Red > 20 ? 2 : -1;
                                }

                                var px = i % 6 * side + x;
                                var py = i / 6 * side + y;
                                var a = Classify(actual.GetPixel(px, py));
                                var b = Classify(reference.GetPixel(px, py));
                                if (a >= 0)
                                {
                                    unions[a]++;
                                    if (a == b)
                                    {
                                        intersections[a]++;
                                    }
                                }

                                if (b >= 0 && a != b)
                                {
                                    unions[b]++;
                                }
                            }
                        }

                        for (var k = 0; k < unions.Length; k++)
                        {
                            Assert.That(
                                intersections[k] / (double)unions[k],
                                Is.GreaterThan(k switch
                                {
                                    0 => .96,
                                    1 => .87,
                                    _ => .94
                                }),
                                $"{names[i]} {(k switch { 0 => "disc", 1 => "glyph", _ => "body" })}");
                        }
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static void SetSample(KubaiIconBase icon, double start, double time)
    {
        var field = typeof(KubaiIconBase).GetField("_pose", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var between = field.FieldType.GetMethod("Between")!;
        var target = field.FieldType.GetMethod("Target")!;
        var rest = target.Invoke(null, [false, false])!;
        var held = target.Invoke(null, [true, false])!;
        var from = between.Invoke(null, [rest, held, start]);
        field.SetValue(icon, between.Invoke(null, [from, start > 0 ? rest : held, time]));
        icon.InvalidateVisual();
    }

    private sealed class Checkerboard : Control
    {
        private static readonly IBrush Light = new SolidColorBrush(Color.Parse("#403149"));
        private static readonly IBrush Dark = new SolidColorBrush(Color.Parse("#192637"));

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Dark, null, new Rect(Bounds.Size));
            for (var y = 0; y < Bounds.Height; y += 10)
            {
                for (var x = (y / 10 % 2) * 10; x < Bounds.Width; x += 20)
                {
                    context.DrawRectangle(Light, null, new Rect(x, y, Math.Min(10, Bounds.Width - x), Math.Min(10, Bounds.Height - y)));
                }
            }
        }
    }
}