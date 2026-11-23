using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using KubaiIcons.Avalonia;
using NUnit.Framework;

namespace KubaiIcons.Tests;

public static class TestHost
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<Application>()
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
    public Task Artwork_WhenRendered_LoadsOnlyTheRequestedFamilyAndReusesSharedBlocks()
    {
        return _session.Dispatch(
            () =>
            {
                using var reads = new DecodeListener();
                var icon = new KubaiIcon { Kind = KubaiIconKind.FolderPlus, Size = 32, IsAnimationEnabled = false };
                var bounds = new Rect(0, 0, 32, 32);
                icon.Measure(bounds.Size);
                icon.Arrange(bounds);
                Assert.That(reads.Names, Is.Empty, "Selection and layout must not open artwork.");

                void Draw()
                {
                    using var context = new DrawingGroup().Open();
                    icon.Render(context);
                }

                Draw();
                Assert.That(reads.Names, Is.EquivalentTo(new[] { "scene-folder", "badge-plus" }));
                icon.Kind = KubaiIconKind.Folder;
                Draw();
                icon.IsEngaged = true;
                Draw();
                Assert.That(reads.Names, Has.Count.EqualTo(2));

                icon.IconStyle = KubaiIconStyle.Duotone;
                Draw();
                Assert.That(reads.Names.Skip(2), Is.EqualTo(new[] { "duotone-folder" }));

                var missing = KubaiIconCatalog.All.FirstOrDefault(info => info.HasLayered && !info.HasDuotone);
                icon.Kind = missing.Kind == KubaiIconKind.None ? (KubaiIconKind)int.MaxValue : missing.Kind;
                Draw();
                Assert.That(reads.Names, Has.Count.EqualTo(3), "An unavailable style must not load a fallback.");
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