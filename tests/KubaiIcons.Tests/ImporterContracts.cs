using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using KubaiIcons.Avalonia;
using KubaiIcons.Importer;
using NUnit.Framework;

namespace KubaiIcons.Tests;

public sealed class ImporterContracts
{
    [Test]
    public void Enum_WhenCompiled_KeepsItsOriginalDeclarationPrefix()
    {
        var names = typeof(KubaiIconKind).GetFields(BindingFlags.Public | BindingFlags.Static)
            .OrderBy(f => f.MetadataToken)
            .Select(f => (f.Name, Value: (int)(f.GetRawConstantValue() ?? -1)))
            .ToArray();
        Assert.That(names.Take(4), Is.EqualTo(new[] { ("None", 0), ("Abacus", 1), ("Accessibility", 2), ("Accordion", 3) }));
        Assert.That(names.Select(n => n.Value), Is.Ordered.Ascending);
        Assert.That(
            KubaiIconCatalog.All.Select(icon => (int)icon.Kind),
            Is.Ordered.Ascending,
            "Alphabetizing generated source must not change the public catalog's stable declaration order.");
    }

    [Test]
    public void Catalog_WhenLookingUpStableIds_ReturnsOnlyAvailableEntries()
    {
        var all = KubaiIconCatalog.All;
        var index = 0;
        foreach (var icon in all)
        {
            Assert.That(all[index++], Is.EqualTo(icon));
            Assert.That(KubaiIconCatalog.Get(icon.Kind), Is.EqualTo(icon));
        }

        Assert.That(index, Is.EqualTo(all.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = all[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = all[all.Count]);

        Assert.That(KubaiIconCatalog.Get(KubaiIconKind.None), Is.Null);
        Assert.That(KubaiIconCatalog.Get((KubaiIconKind)int.MinValue), Is.Null);
        Assert.That(KubaiIconCatalog.Get((KubaiIconKind)int.MaxValue), Is.Null);
        var alert = KubaiIconCatalog.Get(KubaiIconKind.FolderAlert);
        Assert.That(alert?.HasLayered, Is.True);
        Assert.That(alert?.HasDuotone, Is.EqualTo(PublishedInventory.Current.Duotone.Contains("folder-alert")));
    }

    [Test]
    public void Import_WhenRepeatedAcrossCultures_IsDeterministicAndOnlyAppendsNewIdentities()
    {
        using var fixture = new ImportFixture();
        fixture.Add("zeta");
        fixture.Add("folder");
        fixture.Add("alpha");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = fixture.Plan();
            Assert.That(first.NewIdentityCount, Is.EqualTo(2));
            first.Write();
            var identities = fixture.Identities();
            Assert.That(identities, Is.EqualTo(new[] { ("None", 0), ("Folder", 1), ("FolderPlus", 2), ("Bookmark", 3), ("Alpha", 4), ("Zeta", 5) }));
            var catalog = File.ReadAllLines(Path.Combine(fixture.Root, "src/KubaiIcons.Avalonia/CatalogData.g.cs"));
            var rows = catalog.Where(line => line.TrimStart().StartsWith("Add(new(KubaiIconKind.", StringComparison.Ordinal)).ToArray();
            Assert.That(rows, Has.Length.EqualTo(5));
            Assert.That(rows, Is.EqualTo(rows.Order(StringComparer.Ordinal)), "Generated source stays alphabetical independently of the ID ledger.");
            var ledger = File.ReadAllText(fixture.Ledger);
            var written = File.GetLastWriteTimeUtc(fixture.Ledger);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.That(fixture.Plan().ChangedFiles, Is.Empty);
            Assert.That(File.GetLastWriteTimeUtc(fixture.Ledger), Is.EqualTo(written), "Checking must not rewrite files.");
            var baseline = Path.Combine(fixture.Root, "previous.props");
            File.WriteAllText(baseline, ledger);
            fixture.Add("aardvark"); // Sorts before Alpha but must be appended after Zeta.
            var update = fixture.Plan(baseline);
            Assert.That(update.NewIdentityCount, Is.EqualTo(1));
            update.Write();
            Assert.That(fixture.Identities().Take(identities.Length), Is.EqualTo(identities));
            Assert.That(fixture.Identities()[^1], Is.EqualTo(("Aardvark", 6)));
            Assert.That(fixture.Plan(baseline).ChangedFiles, Is.Empty);

            var resource = Path.Combine(fixture.Root, "src/KubaiIcons.Avalonia/Art/Duotone/alpha.kbi");
            var altered = File.ReadAllBytes(resource).Concat(new byte[] { 0 }).ToArray();
            File.WriteAllBytes(resource, altered);
            var drift = fixture.Plan(baseline);
            Assert.That(drift.ChangedFiles, Is.EqualTo(new[] { "src/KubaiIcons.Avalonia/Art/Duotone/alpha.kbi" }));
            Assert.That(File.ReadAllBytes(resource), Is.EqualTo(altered), "Creating a check plan must leave drift untouched.");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Test]
    public void Import_WhenAStyleIsBackfilled_ReusesItsIdAndAppendsOnlyNewNames()
    {
        using var fixture = new ImportFixture();
        fixture.Add("folder");
        var metadata = Path.Combine(fixture.Root, "metadata");
        Directory.CreateDirectory(metadata);
        File.WriteAllText(Path.Combine(metadata, "package.json"), """{"name":"kubai-icons","version":"0.14.0"}""");

        void Inputs(int duotoneCount, params string[] extra)
        {
            var names = new[] { "folder", "folder-plus", "bookmark", "folder-alert" }.Concat(extra).ToArray();
            File.WriteAllText(
                Path.Combine(metadata, "layered-icons.json"),
                JsonSerializer.Serialize(names.Select(name => new { name })));
            File.WriteAllText(
                Path.Combine(fixture.Root, "catalog/upstream.json"),
                JsonSerializer.Serialize(new
                {
                    version = "kubai-icons@0.14.0",
                    duotoneCount,
                    layeredCount = names.Length
                }));
        }

        CatalogImport Plan() => CatalogImport.Create(fixture.Source, fixture.Root, metadata: metadata);
        Inputs(1);
        Plan().Write();
        var before = fixture.Identities();
        var original = before.Single(i => i.Item1 == "FolderAlert").Item2;
        fixture.Add("folder-alert");
        Inputs(2);
        var backfill = Plan();
        Assert.That(backfill.NewIdentityCount, Is.Zero);
        backfill.Write();
        Assert.That(fixture.Identities(), Is.EqualTo(before));
        var alert = XDocument.Load(fixture.Ledger).Descendants("KubaiId").Single(e => (string?)e.Attribute("Source") == "folder-alert");
        Assert.That((int?)alert.Attribute("Value"), Is.EqualTo(original));
        Assert.That((bool?)alert.Attribute("Duotone"), Is.True);
        fixture.Add("aardvark");
        Inputs(3, "aardvark");
        var both = Plan();
        Assert.That(both.NewIdentityCount, Is.EqualTo(1));
        both.Write();
        Assert.That(fixture.Identities()[^1], Is.EqualTo(("Aardvark", before[^1].Item2 + 1)));
    }

    [TestCase("duplicate")]
    [TestCase("renumber")]
    [TestCase("reorder")]
    [TestCase("unship")]
    [TestCase("rename")]
    [TestCase("remove")]
    public void Import_WhenAnExistingIdentityChanges_FailsBeforeWriting(string change)
    {
        using var fixture = new ImportFixture();
        fixture.Add("alpha");
        fixture.Add("zeta");
        fixture.Plan().Write();
        var baseline = Path.Combine(fixture.Root, "previous.props");
        File.Copy(fixture.Ledger, baseline);
        var doc = XDocument.Load(fixture.Ledger);
        var rows = doc.Descendants("KubaiId").ToArray();
        switch (change)
        {
            case "duplicate":
                rows[^1].SetAttributeValue("Value", 4);
                break;
            case "unship":
                rows[^1].Attribute("Duotone")?.Remove();
                break;
            case "renumber":
                rows[^1].SetAttributeValue("Value", 12);
                break;
            case "rename":
                rows[^1].SetAttributeValue("Include", "Zebra");
                break;
            case "remove":
                rows[^1].Remove();
                break;
            case "reorder":
                rows[^2].Remove();
                rows[^1].AddAfterSelf(rows[^2]);
                break;
        }

        doc.Save(fixture.Ledger);
        var before = File.ReadAllText(fixture.Ledger);
        Assert.Throws<InvalidDataException>(() => fixture.Plan(baseline));
        Assert.That(File.ReadAllText(fixture.Ledger), Is.EqualTo(before));
    }

    [Test]
    public void Import_WhenUpstreamNamesCollideOrUseUnknownFeatures_ReportsTheConflict()
    {
        using var collision = new ImportFixture();
        collision.Add("foo-1");
        collision.Add("foo1");
        var collisionError = Assert.Throws<InvalidDataException>(() => collision.Plan());
        Assert.That(collisionError?.Message, Does.Contain("collision"));
        using var schema = new ImportFixture();
        var node = schema.Add("alpha");
        var track = node["moves"]?["0"]?.AsObject() ?? throw new AssertionException("Missing track.");
        track["unsupported"] = 2;
        schema.Save("alpha", node);
        var schemaError = Assert.Throws<InvalidDataException>(() => schema.Plan());
        Assert.That(schemaError?.Message, Does.Contain("Unsupported"));
        Assert.That(Directory.Exists(Path.Combine(schema.Root, "src")), Is.False);
    }

    [Test]
    public void Import_WhenAnUpstreamIconDisappears_RequiresAnExplicitCompatibilityDecision()
    {
        using var fixture = new ImportFixture();
        fixture.Add("alpha");
        fixture.Add("zeta");
        fixture.Plan().Write();
        File.Delete(Path.Combine(fixture.Source, "icons-duotone/zeta.json"));
        fixture.UpdateCount();
        var fixtureError = Assert.Throws<InvalidDataException>(() => fixture.Plan());
        Assert.That(fixtureError?.Message, Does.Contain("Previously imported"));
        Assert.That(fixture.Identities()[^1], Is.EqualTo(("Zeta", 5)));
    }


    [Test]
    public void StudioImport_WhenGeneratedAndChecked_IsSeparateDeterministicAndReadOnly()
    {
        using var fixture = new ImportFixture();
        fixture.Add("alpha");
        fixture.Plan().Write();
        var metadata = fixture.PrepareStudio();
        var ledger = File.ReadAllText(fixture.Ledger);
        var first = StudioImport.Create(fixture.Source, metadata, fixture.Root);
        Assert.That(first.HasChanges, Is.True);
        first.Write();
        var path = Path.Combine(fixture.Root, StudioImport.OutputPath);
        var text = File.ReadAllText(path);
        using var catalog = JsonDocument.Parse(text);
        var labels = catalog.RootElement.GetProperty("icons").EnumerateArray()
            .ToDictionary(row => row.GetProperty("name").GetString()!, row => row.GetProperty("zh").GetString());
        Assert.That(labels["alpha"], Is.EqualTo("文件夹"));
        Assert.That(labels["folder-plus"], Is.EqualTo("文件夹·添加"));
        Assert.That(text, Does.Contain("文件").And.Contain("documents"));
        Assert.That(StudioImport.Create(fixture.Source, metadata, fixture.Root).HasChanges, Is.False);
        File.AppendAllText(path, "\n");
        Assert.That(StudioImport.Create(fixture.Source, metadata, fixture.Root).HasChanges, Is.True);
        Assert.That(File.ReadAllText(path), Is.EqualTo(text + "\n"));
        Assert.That(File.ReadAllText(fixture.Ledger), Is.EqualTo(ledger));
        var source = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Source, "icons-duotone/alpha.json")))?.AsObject()
            ?? throw new AssertionException("Missing source.");
        source["zh"] = "只改译名";
        source["category"] = "changed-only-in-studio";
        source["tags"] = new JsonArray("search-only");
        fixture.Save("alpha", source);
        Assert.That(fixture.Plan().ChangedFiles, Is.Empty, "Search metadata cannot change shipped artwork bytes.");
    }

    [TestCase("version")]
    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("category")]
    [TestCase("empty-tags")]
    public void StudioImport_WhenInputsAreIncompatible_FailsBeforeWriting(string change)
    {
        using var fixture = new ImportFixture();
        fixture.Add("alpha");
        fixture.Plan().Write();
        var metadata = fixture.PrepareStudio();
        var path = Path.Combine(metadata, "duotone-icons.json");
        var icons = JsonNode.Parse(File.ReadAllText(path))?.AsArray() ?? throw new AssertionException("Missing input.");
        switch (change)
        {
            case "version":
                File.WriteAllText(Path.Combine(metadata, "package.json"), """{"name":"kubai-icons","version":"0.15.0"}""");
                break;
            case "missing":
                icons.Clear();
                break;
            case "duplicate":
                icons.Add(icons[0]?.DeepClone());
                break;
            case "category":
                (icons[0]?.AsObject() ?? throw new AssertionException("No icon."))["category"] = "unknown";
                break;
            case "empty-tags":
                (icons[0]?.AsObject() ?? throw new AssertionException("No icon."))["tags"] = new JsonArray("", " ");
                break;
        }

        File.WriteAllText(path, icons.ToJsonString());
        Assert.Throws<InvalidDataException>(() => StudioImport.Create(fixture.Source, metadata, fixture.Root));
        Assert.That(File.Exists(Path.Combine(fixture.Root, StudioImport.OutputPath)), Is.False);
    }

    [Test]
    public void Package_WhenInspectingResources_ContainsOnlyBinaryArtworkAndNoStudioMetadata()
    {
        var assembly = typeof(KubaiIcon).Assembly;
        Assert.That(assembly.GetManifestResourceNames(), Does.Not.Contain("StudioCatalog"));
        var resources = assembly.GetManifestResourceNames().Where(n => n.StartsWith("KubaiArt.", StringComparison.Ordinal)).ToArray();
        var published = PublishedInventory.Current;
        var scenes = published.HasSlash ? published.Bases.Append("overlay-slash") : published.Bases;
        var expectedResources = published.Duotone.Select(name => "KubaiArt.duotone-" + name)
            .Concat(scenes.Select(name => "KubaiArt.scene-" + name))
            .Concat(published.Badges.Select(name => "KubaiArt.badge-" + name));
        Assert.That(resources, Is.EquivalentTo(expectedResources));
        Assert.That(assembly.GetReferencedAssemblies().Select(a => a.Name), Does.Not.Contain("System.Text.Json"));
        foreach (var name in resources)
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new AssertionException(name);
            using var reader = new BinaryReader(stream);
            Assert.That(reader.ReadUInt32(), Is.EqualTo(0x0149424Bu), name);
            var kind = reader.ReadByte();
            var expected = name switch
            {
                _ when name.StartsWith("KubaiArt.scene-", StringComparison.Ordinal) => 6,
                _ when name.StartsWith("KubaiArt.badge-", StringComparison.Ordinal) => 5,
                _ when name.StartsWith("KubaiArt.duotone-", StringComparison.Ordinal) => 0,
                _ => throw new AssertionException("Unexpected artwork: " + name)
            };
            Assert.That(kind, Is.EqualTo(expected), name);
        }
    }

    [Test]
    public void LayeredInventory_CoversEveryPublishedIdentityWithSharedResources()
    {
        var catalog = KubaiIconCatalog.All.Where(x => x.HasLayered).ToDictionary(x => x.Name);
        Assert.That(catalog.Keys, Is.EquivalentTo(PublishedInventory.Current.Layered));
        Assert.That(KubaiIconCatalog.All.Where(icon => icon.HasDuotone).Select(icon => icon.Name),
            Is.EquivalentTo(PublishedInventory.Current.Duotone));
        Assert.That(
            Enum.GetValues<KubaiLayeredIconKind>().Select(x => (int)x),
            Is.EquivalentTo(catalog.Values.Select(x => (int)x.Kind).Append(0)));
    }

    private sealed class ImportFixture : IDisposable
    {
        public string Root { get; }
        public string Source { get; }
        public string Ledger => Path.Combine(Root, "catalog/ids.props");

        private readonly string _parent = Path.GetFullPath(Path.GetTempPath());
        private readonly string _directory;

        public ImportFixture()
        {
            _directory = Path.Combine(_parent, "KubaiImport-" + Guid.NewGuid().ToString("N"));
            Root = Path.Combine(_directory, "port");
            Source = Path.Combine(_directory, "upstream");
            Directory.CreateDirectory(Path.Combine(Root, "catalog"));
            Directory.CreateDirectory(Path.Combine(Source, "icons-duotone"));
            File.WriteAllText(Path.Combine(Root, "catalog/layered-bases.json"), """["folder","bookmark"]""");
            File.WriteAllText(Path.Combine(Root, "catalog/layered-compositions.json"), """["folder-plus"]""");
            File.WriteAllText(
                Ledger,
                """
                <Project><ItemGroup>
                  <KubaiId Include="None" Value="0" Doc="No artwork."/>
                  <KubaiId Include="Folder" Value="1" Doc="A folder."/>
                  <KubaiId Include="FolderPlus" Value="2" Doc="A folder with a badge."/>
                  <KubaiId Include="Bookmark" Value="3" Doc="A bookmark."/>
                </ItemGroup></Project>
                """);
            File.WriteAllText(
                Path.Combine(Root, "catalog/enum-baseline.json"),
                """[{"name":"None","value":0},{"name":"Folder","value":1},{"name":"FolderPlus","value":2},{"name":"Bookmark","value":3}]""");
            File.WriteAllText(Path.Combine(Source, "LICENSE-ICONS"), "Fixture notice\n");
        }

        public JsonObject Add(string name)
        {
            var node = JsonNode.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Inputs/folder.json")))?.AsObject()
                ?? throw new AssertionException("Missing fixture.");
            node["name"] = name;
            Save(name, node);
            return node;
        }


        public string PrepareStudio()
        {
            var metadata = Path.Combine(Root, "metadata");
            Directory.CreateDirectory(metadata);
            Directory.CreateDirectory(Path.Combine(Source, "icons"));
            File.WriteAllText(Path.Combine(metadata, "package.json"), """{"name":"kubai-icons","version":"0.14.0"}""");
            File.WriteAllText(
                Path.Combine(metadata, "layered-icons.json"),
                JsonSerializer.Serialize(
                    new[] { "folder", "bookmark", "folder-plus" }.Select(name =>
                        new
                        {
                            name,
                            category = "files",
                            tags = new[] { "documents" }
                        })));
            File.WriteAllText(Path.Combine(metadata, "duotone-icons.json"), """[{"name":"alpha","category":"files","tags":["documents"]}]""");
            File.WriteAllText(
                Path.Combine(Root, "catalog/layered-labels.json"),
                """{"folder":"文件夹","bookmark":"书签","folder-plus":"文件夹·添加"}""");
            File.WriteAllText(Path.Combine(Source, "icons/categories.json"), """[{"id":"files","en":"Files","zh":"文件"}]""");
            File.WriteAllText(Path.Combine(Source, "icons/keywords.json"), """{"alpha":{"zh":["文件"],"meaning":["documents"]}}""");
            File.WriteAllText(
                Path.Combine(Root, "catalog/upstream.json"),
                """{"version":"kubai-icons@0.14.0","commit":"fixture","duotoneCount":1}""");
            return metadata;
        }

        public void Save(string name, JsonObject node)
        {
            File.WriteAllText(Path.Combine(Source, "icons-duotone", name + ".json"), node.ToJsonString());
            UpdateCount();
        }

        public void UpdateCount()
        {
            var count = Directory.GetFiles(Path.Combine(Source, "icons-duotone"), "*.json").Length;
            File.WriteAllText(Path.Combine(Root, "catalog/upstream.json"), $"{{\"duotoneCount\":{count}}}");
        }

        public CatalogImport Plan(string? baseline = null) => CatalogImport.Create(Source, Root, baseline);

        public (string, int)[] Identities()
        {
            return XDocument.Load(Ledger)
                .Descendants("KubaiId")
                .Select(e => ((string?)e.Attribute("Include") ?? "", (int?)e.Attribute("Value") ?? -1))
                .ToArray();
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(_directory);
            if (Path.GetDirectoryName(resolved) != _parent.TrimEnd(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(resolved).StartsWith("KubaiImport-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Temporary fixture path escaped its owner.");
            }

            Directory.Delete(resolved, true);
        }
    }
}