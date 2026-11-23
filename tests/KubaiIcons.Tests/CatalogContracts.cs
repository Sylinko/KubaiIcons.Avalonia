using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KubaiIcons.Avalonia;
using NUnit.Framework;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    [Test, Order(-1)] // Check first use before the render tests populate the shared artwork cache.
    public async Task Duotone_WhenPlayingBeforeFirstRender_UsesUpstreamDurationsWithoutDecodingArt()
    {
        await _session.Dispatch(
            () =>
            {
                if (!SystemAllowsMotion())
                {
                    Assert.Ignore("The system has disabled animation.");
                }

                using var expected = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Contracts/duotone-motion.json")));
                var durations = expected.RootElement.GetProperty("icons").EnumerateArray()
                    .ToDictionary(sample => sample.GetProperty("name").GetString()!, sample => sample.GetProperty("duration").GetDouble());

                var icon = new KubaiIcon { IconStyle = KubaiIconStyle.Duotone };
                var window = new Window { Content = icon };
                try
                {
                    window.Show();
                    using var reads = new DecodeListener();
                    foreach (var info in KubaiIconCatalog.All.Where(info => info.HasDuotone))
                    {
                        icon.Kind = info.Kind;
                        icon.Play();
                        Assert.That(Field<double>(icon, "_duration"), Is.EqualTo(durations[info.Name]), info.Name);
                    }

                    Assert.That(reads.Names, Is.Empty);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Test, Order(6)]
    public async Task Duotone_WhenSamplingEveryTrack_MatchesThePinnedUpstreamMotion()
    {
        await _session.Dispatch(
            () =>
            {
                using var expected = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Contracts/duotone-motion.json")));
                var icon = new KubaiIcon
                {
                    IconStyle = KubaiIconStyle.Duotone,
                    Size = 64,
                    IsEngaged = false,
                    IsAnimationEnabled = false
                };
                var window = new Window
                {
                    Content = icon,
                    Width = 100,
                    Height = 100
                };
                try
                {
                    window.Show();
                    foreach (var sample in expected.RootElement.GetProperty("icons").EnumerateArray())
                    {
                        var name = sample.GetProperty("name").GetString();
                        var info = KubaiIconCatalog.All.Single(i => i.Name == name);
                        icon.Kind = info.Kind;
                        Pump(); // Initializes the public control through its actual render path.
                        var state = Field<object>(icon, "_duotone");
                        var art = ReadField<object>(state, "_art");
                        var data = art.GetType().GetProperty("Data")?.GetValue(art) ?? throw new AssertionException("Missing data.");
                        var duration = (double)(data.GetType().GetProperty("Duration")?.GetValue(data) ?? 0d);
                        Assert.That(duration, Is.EqualTo(sample.GetProperty("duration").GetDouble()), name);
                        var tracks = (Array)(data.GetType().GetProperty("Tracks")?.GetValue(data) ?? throw new AssertionException("No tracks."));
                        Assert.That(tracks.Length, Is.EqualTo(sample.GetProperty("frames")[0].GetProperty("tracks").EnumerateObject().Count()));
                        Invoke(state, "Snap", false);
                        Invoke(state, "Begin", true);
                        foreach (var frame in sample.GetProperty("frames").EnumerateArray())
                        {
                            Invoke(state, "Sample", frame.GetProperty("t").GetDouble());
                            var index = 0;
                            foreach (var referenceTrack in frame.GetProperty("tracks").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                            {
                                var key = referenceTrack.Name;
                                var reference = frame.GetProperty("tracks").GetProperty(key);
                                var matrix = (Matrix)(Invoke(state, "MatrixFor", index) ?? throw new AssertionException("No matrix."));
                                var actual = new[] { matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32 };
                                var wanted = reference.GetProperty("matrix").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                                // SVG rounds attributes to 0.001 before composition. A scale
                                // rounding difference is amplified by its pivot in the translation.
                                for (var j = 0; j < 6; j++)
                                {
                                    Assert.That(
                                        actual[j],
                                        Is.EqualTo(wanted[j]).Within(j < 4 ? 0.003 : 0.025),
                                        $"{name}/{key}/{frame.GetProperty("t")}/matrix{j}");
                                }

                                Assert.That(
                                    Invoke(state, "Value", index, 6),
                                    Is.EqualTo(reference.GetProperty("opacity").GetDouble()).Within(0.0015),
                                    $"{name}/{key}/opacity");
                                Assert.That(
                                    Invoke(state, "Value", index, 7),
                                    Is.EqualTo(reference.GetProperty("stroke").GetDouble()).Within(0.0015),
                                    $"{name}/{key}/stroke");
                                index++;
                            }
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

    [Test, Order(7)]
    public async Task Duotone_WhenRenderingTheWholeCatalog_PreservesVisibleEndpointsAndRendersTravel()
    {
        await _session.Dispatch(
            () =>
            {
                var canvas = new Canvas();
                var window = new Window
                {
                    Content = canvas,
                    Width = 1000,
                    Height = 900,
                    Background = Brushes.White
                };
                var catalog = KubaiIconCatalog.All.Where(i => i.HasDuotone).OrderBy(i => i.Name, StringComparer.Ordinal).ToArray();
                try
                {
                    window.Show();
                    for (var page = 0; page * 90 < catalog.Length; page++)
                    {
                        canvas.Children.Clear();
                        var icons = new List<KubaiIcon>();
                        foreach (var info in catalog.Skip(page * 90).Take(90))
                        {
                            var x = icons.Count % 10 * 100;
                            var y = icons.Count / 10 * 100;
                            var icon = new KubaiIcon
                            {
                                Kind = info.Kind,
                                IconStyle = KubaiIconStyle.Duotone,
                                Size = 64,
                                IsAnimationEnabled = false,
                                IsEngaged = false,
                                AccentColor = Color.Parse("#5393FF"),
                            };
                            Canvas.SetLeft(icon, x + 18);
                            Canvas.SetTop(icon, y + 6);
                            canvas.Children.Add(icon);
                            var label = new TextBlock
                            {
                                Foreground = Brushes.Black,
                                Text = info.Name,
                                FontSize = 10,
                                Width = 98,
                                TextAlignment = TextAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            };
                            Canvas.SetLeft(label, x + 1);
                            Canvas.SetTop(label, y + 78);
                            canvas.Children.Add(label);
                            icons.Add(icon);
                        }

                        Pump();
                        for (var pose = 0; pose < 3; pose++)
                        {
                            foreach (var icon in icons)
                            {
                                if (pose == 1)
                                {
                                    var state = Field<object>(icon, "_duotone");
                                    Invoke(state, "Snap", false);
                                    Invoke(state, "Begin", true);
                                    Invoke(state, "Sample", 0.37);
                                    icon.InvalidateVisual();
                                }

                                if (pose == 2)
                                {
                                    icon.IsEngaged = true;
                                }
                            }

                            Pump();
                            using var frame = window.CaptureRenderedFrame() ?? throw new AssertionException("No frame.");
                            using var buffer = frame.Lock();
                            var row = new byte[64 * 4];
                            foreach (var icon in icons)
                            {
                                var point = icon.TranslatePoint(default, window) ?? throw new AssertionException("No bounds.");
                                var colored = 0;
                                for (var y = 0; y < 64; y++)
                                {
                                    Marshal.Copy(buffer.Address + ((int)point.Y + y) * buffer.RowBytes + (int)point.X * 4, row, 0, row.Length);
                                    for (var x = 0; x < row.Length; x += 4)
                                    {
                                        if (row[x] < 240 || row[x + 1] < 240 || row[x + 2] < 240)
                                        {
                                            colored++;
                                        }
                                    }
                                }

                                // Some authored motions intentionally disappear mid-cycle (e.g. git-commit).
                                if (pose != 1)
                                {
                                    Assert.That(colored, Is.GreaterThan(8), $"{icon.Kind}, endpoint {pose} must draw real artwork.");
                                }
                            }

                            var directory = Environment.GetEnvironmentVariable("KUBAI_CAPTURE_DIR");
                            if (directory is not null)
                            {
                                frame.Save(Path.Combine(directory, $"catalog-{page}-{pose}.png"), PngBitmapEncoderOptions.Default);
                            }
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

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("KUBAI_CAPTURE_DIR");
        if (directory is null)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame() ?? throw new AssertionException("No frame.");
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static T ReadField<T>(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target) is T result
            ? result
            : throw new AssertionException($"Missing {name}.");
    }

    private static object? Invoke(object target, string name, params object[] values)
    {
        return (target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new AssertionException($"Missing {name}.")).Invoke(target, values);
    }
}