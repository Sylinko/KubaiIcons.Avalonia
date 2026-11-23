using System.Reflection;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions ReferenceJson = new() { PropertyNameCaseInsensitive = true };

    private sealed record ReferencePose(string Name, double From, bool To, double T, bool Soft);

    [Test, Explicit, Category("Artwork")]
    public async Task PublishedLayeredArtwork_WhenDrawn_MatchesUpstreamContactSheets()
    {
        var directory = Environment.GetEnvironmentVariable("KUBAI_REFERENCE_DIR");
        if (directory is null)
        {
            Assert.Ignore("Set KUBAI_REFERENCE_DIR to the output of layered-reference.mjs.");
            return;
        }

        var jobs = JsonSerializer.Deserialize<ReferencePose[]>(
            await File.ReadAllTextAsync(Path.Combine(directory, "poses.json")),
            ReferenceJson) ?? throw new AssertionException("Missing reference poses.");
        var catalog = KubaiIconCatalog.All.Where(x => x.HasLayered).ToDictionary(x => x.Name);
        Assert.That(jobs.Select(j => j.Name).Distinct(), Is.EquivalentTo(catalog.Keys));
        var metrics = new List<(double Mean, double Overlap, string Name, int Index)>();
        await _session.Dispatch(
            () =>
            {
                const int side = 62;
                var field = typeof(KubaiIconBase).GetField("_pose", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var between = field.FieldType.GetMethod("Between")!;
                var target = field.FieldType.GetMethod("Target")!;
                var window = new Window
                {
                    Width = 620,
                    Height = 620,
                    CanResize = false
                };
                try
                {
                    window.Show();
                    for (var start = 0; start < jobs.Length; start += 100)
                    {
                        var canvas = new Canvas
                        {
                            Width = 620,
                            Height = 620,
                            Background = new SolidColorBrush(Color.Parse("#192637"))
                        };
                        window.Content = canvas;
                        foreach (var (j, i) in jobs.Skip(start).Take(100).Select((item, i) => (item, i)))
                        {
                            var icon = new KubaiLayeredIcon
                            {
                                Kind = (KubaiLayeredIconKind)catalog[j.Name].Kind,
                                Size = side,
                                BadgeColor = Color.Parse("#F01487"),
                                AccentColor = Color.Parse("#5393FF"),
                                IsAnimationEnabled = false,
                                ColorVariant = j.Soft ? KubaiColorVariant.Soft : KubaiColorVariant.Color
                            };
                            canvas.Children.Add(icon);
                            var (row, column) = Math.DivRem(i, 10);
                            Canvas.SetLeft(icon, column * side);
                            Canvas.SetTop(icon, row * side);
                        }

                        Pump();
                        for (var i = 0; i < canvas.Children.Count; i++)
                        {
                            var j = jobs[start + i];
                            var icon = (KubaiLayeredIcon)canvas.Children[i];
                            var rest = target.Invoke(null, [false, false]);
                            field.FieldType.GetProperty("Hover")!.SetValue(rest, j.From);
                            field.SetValue(icon, between.Invoke(null, [rest, target.Invoke(null, [j.To, false]), j.T]));
                            icon.InvalidateVisual();
                        }

                        Pump();
                        using var shot = window.CaptureRenderedFrame()!;
                        var output = Path.Combine(directory, $"native-{start}.png");
                        shot.Save(output, PngBitmapEncoderOptions.Default);
                        using var actual = SKBitmap.Decode(output);
                        using var reference = SKBitmap.Decode(Path.Combine(directory, $"reference-{start}.png"));
                        for (var i = 0; i < canvas.Children.Count; i++)
                        {
                            var diff = 0d;
                            var union = 0;
                            var intersection = 0;
                            for (var y = 0; y < side; y++)
                            {
                                for (var x = 0; x < side; x++)
                                {
                                    var a = actual.GetPixel(i % 10 * side + x, i / 10 * side + y);
                                    var b = reference.GetPixel(i % 10 * side + x, i / 10 * side + y);
                                    static bool Ink(SKColor p) => Math.Abs(p.Red - 25) + Math.Abs(p.Green - 38) + Math.Abs(p.Blue - 55) > 100;
                                    if (Ink(a) || Ink(b))
                                    {
                                        union++;
                                    }

                                    if (Ink(a) && Ink(b))
                                    {
                                        intersection++;
                                    }

                                    diff += Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
                                }
                            }

                            metrics.Add((diff / (3 * side * side), intersection / (double)Math.Max(1, union), jobs[start + i].Name, start + i));
                        }
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
        var worst = metrics.OrderByDescending(x => x.Mean).ToArray();
        await File.WriteAllLinesAsync(
            Path.Combine(directory, "errors.tsv"),
            worst.Select(x => FormattableString.Invariant($"{x.Mean:F3}\t{x.Overlap:F4}\t{x.Name}\t{x.Index}")));
        Assert.That(worst[0].Mean, Is.LessThan(7.0), worst[0].ToString());
        var silhouette = metrics.MinBy(x => x.Overlap);
        Assert.That(silhouette.Overlap, Is.GreaterThan(.88), silhouette.ToString());
    }
}