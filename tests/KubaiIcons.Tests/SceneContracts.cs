using System.Reflection;
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
    [Explicit, Category("Artwork")]
    [TestCase(0d, .18, true, 99)]
    [TestCase(.180103, .12, false, 86)]
    public async Task GrowingPolyline_WhenEnteringOrReversing_LeavesTheAuthoredInkOnTheTablet(
        double startTime,
        double elapsed,
        bool soft,
        int referenceInk
    )
    {
        await _session.Dispatch(
            () =>
            {
                var icon = new KubaiLayeredIcon
                {
                    Kind = KubaiLayeredIconKind.DrawingTablet,
                    Size = 62,
                    ColorVariant = soft ? KubaiColorVariant.Soft : KubaiColorVariant.Color,
                    IsAnimationEnabled = false,
                    AccentColor = Color.Parse("#5393FF")
                };
                var window = new Window
                {
                    Width = 62,
                    Height = 62,
                    Content = icon,
                    CanResize = false,
                    Background = new SolidColorBrush(Color.Parse("#192637"))
                };
                try
                {
                    window.Show();
                    Pump();
                    SetSample(icon, startTime, elapsed);
                    Pump();
                    using var shot = window.CaptureRenderedFrame()!;
                    using var stream = new MemoryStream();
                    shot.Save(stream, PngBitmapEncoderOptions.Default);
                    stream.Position = 0;
                    using var frame = SKBitmap.Decode(stream);
                    var ink = 0;
                    for (var y = 34; y <= 42; y++)
                    {
                        for (var x = 23; x <= 40; x++)
                        {
                            var p = frame.GetPixel(x, y);
                            if (p.Red < 120 && p.Green < 160 && p.Blue > 140)
                            {
                                ink++;
                            }
                        }
                    }

                    // Count the dark ink on the white screen, excluding the pen and tablet frame.
                    Assert.That(ink, Is.EqualTo(referenceInk).Within(10));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Test, Explicit, Category("Artwork")]
    public async Task SceneBases_WhenRetargetedAndSaved_MatchUpstream()
    {
        await _session.Dispatch(
            () =>
            {
                const int size = 124;
                (bool On, bool Active, double T)[] steps =
                [
                    (true, false, .28), (false, false, .13), (true, false, .42), (true, true, .22),
                    (false, true, .37), (false, true, 1), (false, false, .16), (true, true, .33)
                ];
                var canvas = new Canvas
                {
                    Width = size * 8,
                    Height = size * 4,
                    Background = new SolidColorBrush(Color.Parse("#192637"))
                };
                var window = new Window
                {
                    Content = canvas,
                    Width = canvas.Width,
                    Height = canvas.Height,
                    CanResize = false
                };
                var field = typeof(KubaiIconBase).GetField("_pose", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var saved = typeof(KubaiIconBase).GetField("_saved", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var target = field.FieldType.GetMethod("Target")!;
                var between = field.FieldType.GetMethod("Between")!;
                var cells = new List<(KubaiLayeredIcon Icon, object Pose)>();
                try
                {
                    for (var row = 0; row < 4; row++)
                    {
                        var bookmark = row % 2 == 1;
                        var pose = target.Invoke(null, [false, false]);
                        for (var col = 0; col < steps.Length; col++)
                        {
                            var (on, active, t) = steps[col];
                            pose = between.Invoke(null, [pose, target.Invoke(null, [on, bookmark && active]), t])!;
                            var icon = new KubaiLayeredIcon
                            {
                                Kind = bookmark ? KubaiLayeredIconKind.Bookmark : KubaiLayeredIconKind.Folder,
                                Size = size,
                                IsAnimationEnabled = false,
                                ColorVariant = row < 2 ? KubaiColorVariant.Color : KubaiColorVariant.Soft,
                                AccentColor = Color.Parse("#5393FF")
                            };
                            cells.Add((icon, pose));
                            canvas.Children.Add(icon);
                            Canvas.SetLeft(icon, col * size);
                            Canvas.SetTop(icon, row * size);
                        }
                    }

                    window.Show();
                    Pump();
                    foreach (var (icon, pose) in cells)
                    {
                        field.SetValue(icon, pose);
                        saved.SetValue(icon, field.FieldType.GetProperty("Saved")!.GetValue(pose));
                        icon.InvalidateVisual();
                    }

                    Pump();
                    using var sheet = window.CaptureRenderedFrame()!;
                    if (Environment.GetEnvironmentVariable("KUBAI_CAPTURE_DIR") is { } dir)
                    {
                        sheet.Save(Path.Combine(dir, "scene-gate-native.png"), PngBitmapEncoderOptions.Default);
                    }

                    using var stream = new MemoryStream();
                    sheet.Save(stream, PngBitmapEncoderOptions.Default);
                    stream.Position = 0;
                    using var actual = SKBitmap.Decode(stream);
                    using var reference = SKBitmap.Decode(Path.Combine(TestContext.CurrentContext.TestDirectory, "Contracts/scene-gate.png"));
                    for (var row = 0; row < 4; row++)
                    {
                        for (var col = 0; col < 8; col++)
                        {
                            var diff = 0d;
                            var core = 0d;
                            var count = 0;
                            for (var y = 0; y < size; y++)
                            {
                                for (var x = 0; x < size; x++)
                                {
                                    var a = actual.GetPixel(col * size + x, row * size + y);
                                    var b = reference.GetPixel(col * size + x, row * size + y);
                                    var err = (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue)) / 3.0;
                                    diff += err;
                                    if (x is >= 40 and < 74 && y is >= 19 and < 78)
                                    {
                                        core += err;
                                        count++;
                                    }
                                }
                            }

                            Assert.That(diff / (size * size), Is.LessThan(6.0), $"scene {row},{col}");
                            Assert.That(core / count, Is.LessThan(4.5), $"moving paper or ribbon {row},{col}");
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
}