using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using IconStudio;
using KubaiIcons.Avalonia;
using NUnit.Framework;

namespace KubaiIcons.Tests;

public static class TestHost
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return App.CreateBuilder()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();
    }
}

public partial class IconContracts
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(TestHost), AvaloniaTestIsolationLevel.PerAssembly);

    [OneTimeTearDown]
    public void CloseSession()
    {
        _session.Dispose();
    }

    [Test]
    public void PublicIds_WhenCatalogChanges_PreserveExistingNamesAndValues()
    {
        // This consumer snapshot is deliberately independent of the build's ID manifest.
        using var baseline = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Contracts/first-release.json")));
        var values = Enum.GetValues<KubaiIconKind>().Select(x => (int)x).ToArray();
        Assert.That(values.Distinct().Count(), Is.EqualTo(values.Length), "A new icon cannot reuse an existing identity.");
        foreach (var old in baseline.RootElement.EnumerateObject())
        {
            Assert.That(Enum.TryParse<KubaiIconKind>(old.Name, out var current), Is.True, old.Name);
            Assert.That((int)current, Is.EqualTo(old.Value.GetInt32()), old.Name);
        }
    }


    [Test, Order(0)]
    public async Task Artwork_WhenChangingKindsAndPoses_LoadsOnlyDemandedBlocksAndDrawsAuthoredMotion()
    {
        await _session.Dispatch(
            () =>
            {
                using var reads = new DecodeListener();
                var icon = new KubaiIcon
                {
                    Kind = KubaiIconKind.Folder,
                    Size = 144,
                    AccentColor = Color.Parse("#7160DB"),
                    IsEngaged = false,
                    IsAnimationEnabled = false
                };
                var window = new Window
                {
                    Width = 200,
                    Height = 200,
                    Background = Brushes.White,
                    Content = icon
                };

                byte[] Pixels()
                {
                    using var frame = new RenderTargetBitmap(new PixelSize(200, 200));
                    frame.Render(window);
                    var bytes = new byte[200 * 200 * 4];
                    var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    try
                    {
                        frame.CopyPixels(new PixelRect(0, 0, 200, 200), pin.AddrOfPinnedObject(), bytes.Length, 800);
                    }
                    finally
                    {
                        pin.Free();
                    }

                    return bytes;
                }

                static int Changed(byte[] a, byte[] b)
                {
                    var count = 0;
                    for (var i = 0; i < a.Length; i += 4)
                    {
                        if (Math.Abs(a[i] - b[i]) > 2 || Math.Abs(a[i + 1] - b[i + 1]) > 2 || Math.Abs(a[i + 2] - b[i + 2]) > 2)
                        {
                            count++;
                        }
                    }

                    return count;
                }

                try
                {
                    icon.Measure(new Size(200, 200));
                    new KubaiIcon { Kind = KubaiIconKind.Bookmark }.Measure(new Size(24, 24));
                    Assert.That(reads.Names, Is.Empty, "Configuration and measurement do not decode artwork.");
                    window.Show();
                    Pump();
                    var rest = Pixels();
                    Assert.That(reads.Names, Is.EqualTo(new[] { "scene-folder" }));
                    icon.IsEngaged = true;
                    Assert.That(Changed(Pixels(), rest), Is.GreaterThan(40));
                    icon.IsEngaged = false;
                    Assert.That(Changed(Pixels(), rest), Is.Zero);

                    icon.Kind = KubaiIconKind.FolderPlus;
                    Pump();
                    Assert.That(Changed(Pixels(), rest), Is.GreaterThan(40));
                    Assert.That(reads.Names, Is.EqualTo(new[] { "scene-folder", "badge-plus" }));
                    icon.Kind = KubaiIconKind.Folder;
                    Pump();
                    Assert.That(Changed(Pixels(), rest), Is.Zero);
                    Assert.That(reads.Names.Count, Is.EqualTo(2));

                    icon.Kind = KubaiIconKind.Bookmark;
                    Pump();
                    var unsaved = Pixels();
                    icon.IsActive = true;
                    var saved = Pixels();
                    Assert.That(Changed(saved, unsaved), Is.GreaterThan(40));
                    icon.IsEngaged = true;
                    Assert.That(Changed(Pixels(), saved), Is.GreaterThan(40));
                    icon.IsEngaged = false;
                    Assert.That(Changed(Pixels(), saved), Is.Zero);
                    icon.ColorVariant = KubaiColorVariant.Soft;
                    Assert.That(Changed(Pixels(), saved), Is.GreaterThan(40));

                    icon.Kind = KubaiIconKind.Folder;
                    icon.IconStyle = KubaiIconStyle.Duotone;
                    Pump();
                    var duo = Pixels();
                    icon.IsEngaged = true;
                    var held = Pixels();
                    Assert.That(Changed(held, duo), Is.GreaterThan(40));
                    icon.IsEngaged = false;
                    icon.IsAnimationEnabled = true;
                    icon.IsEngaged = true;
                    // The rear sheet is masked by the folder body at 140 ms; sample after it emerges.
                    Step(icon, 170);
                    var mid = Pixels(); // Capture the sampled pose without advancing the headless render clock.
                    Assert.That(Changed(mid, duo), Is.GreaterThan(40));
                    if (SystemAllowsMotion())
                    {
                        Assert.That(Changed(mid, held), Is.GreaterThan(40));
                    }

                    icon.IsEngaged = false;
                    Step(icon, 420);
                    Assert.That(Changed(Pixels(), duo), Is.Zero);
                    Assert.That(reads.Names, Is.EqualTo(new[] { "scene-folder", "badge-plus", "scene-bookmark", "duotone-folder" }));
                    icon.IconStyle = KubaiIconStyle.Layered;
                    icon.Kind = KubaiIconKind.BookmarkPlus;
                    icon.IsAnimationEnabled = false;
                    icon.IsActive = false;
                    Pump();
                    var composed = Pixels();
                    icon.IsSemanticBadgeEnabled = true;
                    Assert.That(Changed(composed, Pixels()), Is.GreaterThan(40), "Semantic color changes the neutral disc.");
                    icon.BadgeColor = Color.Parse("#E71180");
                    var custom = Pixels();
                    icon.IsSemanticBadgeEnabled = false;
                    Assert.That(Changed(custom, Pixels()), Is.Zero, "An explicit badge color takes precedence.");
                    icon.BadgeColor = null;
                    icon.IsActive = true;
                    Assert.That(Changed(composed, Pixels()), Is.GreaterThan(40), "The composed bookmark retains its saved state.");
                    Assert.That(reads.Names.Count, Is.EqualTo(4), "Changing base should reuse the decoded plus badge.");
                    icon.Size = 24;
                    Pump();
                    Assert.That(icon.DesiredSize, Is.EqualTo(new Size(24, 24)));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static bool SystemAllowsMotion()
    {
        return !OperatingSystem.IsWindows() ||
        !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled != 0;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);

    private sealed class DecodeListener : EventListener
    {
        public List<string> Names { get; } = [];

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "KubaiIcons-Avalonia")
            {
                EnableEvents(source, EventLevel.Informational);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e is { EventId: 1, Payload: [string key, long] })
            {
                Names.Add(key);
            }
        }
    }
}