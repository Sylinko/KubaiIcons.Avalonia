using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KubaiIcons.Importer;

/// <summary>
/// A validated import plan shared by generation and read-only CI checks.
/// File discovery order never assigns identities.
/// </summary>
public sealed partial class CatalogImport
{
    /// <summary>
    /// The number of source Duotone definitions.
    /// </summary>
    public int IconCount { get; }

    /// <summary>
    /// The number of identities appended by this import.
    /// </summary>
    public int NewIdentityCount { get; }

    /// <summary>
    /// Repository-relative output paths whose content differs.
    /// </summary>
    public IReadOnlyList<string> ChangedFiles { get; }

    private readonly ImportOutput _output;

    private CatalogImport(string root, Dictionary<string, byte[]> files, int count, int added)
    {
        _output = new ImportOutput(root, files);
        IconCount = count;
        NewIdentityCount = added;
        ChangedFiles = _output.ChangedFiles;
    }

    /// <summary>
    /// Validates source data and identity compatibility, then computes all outputs without writing.
    /// </summary>
    public static CatalogImport Create(string source, string root, string? baseline = null, string? metadata = null)
    {
        using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "catalog/upstream.json")));
        var expectedCount = pin.RootElement.GetProperty("duotoneCount").GetInt32();
        var ledgerPath = Path.Combine(root, "catalog/ids.props");
        var ledger = XDocument.Load(ledgerPath);
        var ids = ReadIds(ledger);
        if (metadata is null && ledger.Root?.Element("PropertyGroup")?.Element("KubaiUpstreamVersion")?.Value is { } current &&
            pin.RootElement.GetProperty("version").GetString() != "kubai-icons@" + current)
        {
            throw new InvalidDataException("A version update requires --metadata with the pinned Layered manifest.");
        }

        if (ids.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count ||
            ids.Select(x => x.Value).Distinct().Count() != ids.Count)
        {
            throw new InvalidDataException("Duplicate enum name or ID.");
        }

        if (ids.Count == 0 || ids[0] is not { Name: "None", Value: 0 } ||
            ids.Zip(ids.Skip(1)).Any(p => p.First.Value >= p.Second.Value))
        {
            throw new InvalidDataException("IDs must begin with None = 0 and remain in increasing declaration order.");
        }

        using (var frozen = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "catalog/enum-baseline.json"))))
        {
            var old = frozen.RootElement.EnumerateArray()
                .Select(e => new Identity(
                    e.GetProperty("name").GetString() ?? "",
                    e.GetProperty("value").GetInt32()))
                .ToArray();
            CheckPrefix(old, ids);
        }

        if (baseline is not null)
        {
            var previous = XDocument.Load(baseline);
            CheckPrefix(ReadIds(previous), ids);
            var families = new[] { "Duotone", "Layered" };
            foreach (var (oldRow, newRow) in previous.Descendants("KubaiId").Zip(ledger.Descendants("KubaiId")))
            {
                foreach (var family in families)
                {
                    if ((bool?)oldRow.Attribute(family) == true && (bool?)newRow.Attribute(family) != true)
                    {
                        throw new InvalidDataException($"Previously shipped {family} member '{oldRow.Attribute("Include")?.Value}' was removed.");
                    }
                }
            }
        }

        var definitions = new Dictionary<string, DuotoneSource>(StringComparer.Ordinal);
        var input = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "icons-duotone"), "*.json").Order(StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var data = DuotoneSource.Read(doc.RootElement);
            if (!SlugPattern().IsMatch(data.Name) ||
                Path.GetFileNameWithoutExtension(file) != data.Name)
            {
                throw new InvalidDataException($"Invalid or mismatched source name: {file}");
            }

            if (!definitions.TryAdd(data.Name, data))
            {
                throw new InvalidDataException($"Duplicate icon: {data.Name}");
            }

            input.Add(data.Name, DuotoneCompiler.Compile(data));
        }

        if (definitions.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"Expected {expectedCount} Duotone icons, found {definitions.Count}. Update the upstream pin deliberately.");
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var data in definitions.Values)
        {
            var name = EnumName(data.Name);
            if (name == "None")
            {
                throw new InvalidDataException("The None identity is reserved for empty artwork.");
            }

            if (!names.TryAdd(name, data.Name))
            {
                throw new InvalidDataException($"C# name collision: {data.Name} and {names[name]} become {name}.");
            }
        }

        var layered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { "layered-bases.json", "layered-compositions.json" })
        {
            foreach (var slug in JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(Path.Combine(root, "catalog", file)))!)
            {
                if (!layered.Add(slug))
                {
                    throw new InvalidDataException($"Duplicate Layered identity: {slug}");
                }

                var name = EnumName(slug);
                if (names.TryGetValue(name, out var previous) && previous != slug)
                {
                    throw new InvalidDataException($"C# name collision: {slug} and {previous}");
                }

                names[name] = slug;
            }
        }

        HashSet<string>? upstreamLayered = null;
        if (metadata is not null)
        {
            using var package = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(metadata, "package.json")));
            if (package.RootElement.GetProperty("name").GetString() != "kubai-icons" ||
                "kubai-icons@" + package.RootElement.GetProperty("version").GetString() != pin.RootElement.GetProperty("version").GetString())
            {
                throw new InvalidDataException("Layered metadata version mismatch.");
            }

            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(metadata, "layered-icons.json")));
            var published = new HashSet<string>(StringComparer.Ordinal);
            upstreamLayered = published;
            foreach (var icon in manifest.RootElement.EnumerateArray())
            {
                var slug = icon.GetProperty("name").GetString() ?? "";
                if (!SlugPattern().IsMatch(slug) || !published.Add(slug))
                {
                    throw new InvalidDataException($"Invalid Layered identity: {slug}");
                }

                var name = EnumName(slug);
                if (name == "None" || names.TryGetValue(name, out var other) && other != slug)
                {
                    throw new InvalidDataException($"C# name collision: {slug}");
                }

                names[name] = slug;
            }

            if (published.Count != pin.RootElement.GetProperty("layeredCount").GetInt32() || !layered.IsSubsetOf(published))
            {
                throw new InvalidDataException("Layered manifest does not match the pin or supported artwork.");
            }

            foreach (var item in ledger.Descendants("KubaiId").Where(e => (bool?)e.Attribute("UpstreamLayered") == true))
            {
                if (!published.Contains((string?)item.Attribute("Source") ?? ""))
                {
                    throw new InvalidDataException($"Removed Layered identity: {item}");
                }
            }
        }

        var elements = ledger.Descendants("KubaiId").ToList();
        foreach (var item in elements)
        {
            var sourceName = (string?)item.Attribute("Source");
            if (sourceName is not null && (bool?)item.Attribute("Layered") == true && !layered.Contains(sourceName))
            {
                throw new InvalidDataException($"Previously shipped Layered artwork '{sourceName}' is missing.");
            }

            if (sourceName is not null && !definitions.ContainsKey(sourceName) && (bool?)item.Attribute("Duotone") == true)
            {
                throw new InvalidDataException(
                    $"Previously imported '{sourceName}' is missing. Preserve its identity and resolve the upstream removal explicitly.");
            }

            if (sourceName is not null && definitions.ContainsKey(sourceName) && EnumName(sourceName) != (string?)item.Attribute("Include"))
            {
                throw new InvalidDataException($"Source identity changed for {sourceName}.");
            }
        }

        var byName = elements.ToDictionary(e => (string?)e.Attribute("Include") ?? "", StringComparer.Ordinal);
        var additions = names.Keys.Where(n => !byName.ContainsKey(n)).Order(StringComparer.Ordinal).ToArray();
        var maximum = ids.Max(x => x.Value);
        var group = ledger.Root?.Element("ItemGroup") ?? throw new InvalidDataException("Missing ID ItemGroup.");
        foreach (var name in additions)
        {
            if (ids.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"C# name collides with existing identity: {name}");
            }

            var item = new XElement(
                "KubaiId",
                new XAttribute("Include", name),
                new XAttribute("Value", checked(++maximum)),
                new XAttribute("Doc", $"The {names[name]} icon."));
            group.Add(item);
            byName.Add(name, item);
        }

        foreach (var pair in names)
        {
            var item = byName[pair.Key];
            if (item.Attribute("Source") is { } previous && previous.Value != pair.Value)
            {
                throw new InvalidDataException($"Source name collision: {pair.Value}");
            }

            item.SetAttributeValue("Source", pair.Value);
            if (upstreamLayered?.Contains(pair.Value) == true)
            {
                item.SetAttributeValue("UpstreamLayered", true);
            }

            if (layered.Contains(pair.Value))
            {
                item.SetAttributeValue("Layered", true);
            }
        }

        foreach (var data in definitions.Values)
        {
            var item = byName[EnumName(data.Name)];
            var oldSource = (string?)item.Attribute("Source");
            if (oldSource is not null && oldSource != data.Name)
            {
                throw new InvalidDataException($"Source name collision: {data.Name}");
            }

            item.SetAttributeValue("Source", data.Name);
            item.SetAttributeValue("Duotone", true);
        }

        if (pin.RootElement.TryGetProperty("version", out var release))
        {
            var version = release.GetString()?.Split('@').Last();
            if (!Version.TryParse(version, out _))
            {
                throw new InvalidDataException("Invalid upstream version.");
            }

            var properties = ledger.Root?.Element("PropertyGroup");
            if (properties is null)
            {
                properties = new XElement("PropertyGroup");
                ledger.Root?.AddFirst(properties);
            }

            properties.SetElementValue("KubaiUpstreamVersion", version);
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["catalog/ids.props"] = Encoding.UTF8.GetBytes(ledger.ToString().Replace("\r\n", "\n") + "\n"),
            ["licenses/LICENSE-ICONS"] =
                Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(source, "LICENSE-ICONS")).Replace("\r\n", "\n").TrimEnd() + "\n"),
        };
        const string resourceDirectory = "src/KubaiIcons.Avalonia/Art/Duotone";
        foreach (var pair in input)
        {
            files.Add($"{resourceDirectory}/{pair.Key}.kbi", pair.Value);
        }

        ImportOutput.CheckInventory(root, resourceDirectory, input.Keys.Select(name => name + ".kbi"));
        var slots = new List<int>();
        var rows = new StringBuilder(
            "// <auto-generated />\n" +
            "namespace KubaiIcons.Avalonia;\n\n" +
            "internal static class CatalogData\n{\n" +
            "    internal static KubaiIconInfo[] Create()\n    {\n");
        rows.Append(CultureInfo.InvariantCulture, $"        var byId = new KubaiIconInfo[{maximum}];\n");
        foreach (var item in group.Elements("KubaiId").OrderBy(e => (string?)e.Attribute("Include"), StringComparer.Ordinal))
        {
            var name = (string?)item.Attribute("Include") ?? "";
            if (name == "None")
            {
                continue;
            }

            definitions.TryGetValue((string?)item.Attribute("Source") ?? "", out var data);
            var isLayered = (bool?)item.Attribute("Layered") == true;
            if (data is null && !isLayered)
            {
                continue;
            }

            var slug = data?.Name ?? (string?)item.Attribute("Source") ?? throw new InvalidDataException($"Missing source for {name}.");
            var styles = (isLayered, data is not null) switch
            {
                (true, true) => "KubaiIconStyles.Layered | KubaiIconStyles.Duotone",
                (true, false) => "KubaiIconStyles.Layered",
                _ => "KubaiIconStyles.Duotone",
            };
            var id = (int?)item.Attribute("Value") ?? throw new InvalidDataException($"Missing ID for {name}.");
            slots.Add(id - 1);
            rows.Append("        Add(new(KubaiIconKind.")
                .Append(name)
                .Append(", ")
                .Append(JsonSerializer.Serialize(slug))
                .Append(", ")
                .Append(styles)
                .Append("));\n");
        }

        rows.Append("        return byId;\n\n")
            .Append("        void Add(KubaiIconInfo icon) => byId[(int)icon.Kind - 1] = icon;\n    }\n\n")
            .Append(CultureInfo.InvariantCulture, $"    internal const int Count = {slots.Count};\n\n");
        AppendViewIndex(rows, slots);
        AppendDuotoneDurations(rows, definitions.Values);
        rows.Append("}\n");
        files.Add("src/KubaiIcons.Avalonia/CatalogData.g.cs", Encoding.UTF8.GetBytes(rows.ToString()));
        return new CatalogImport(root, files, definitions.Count, additions.Length);
    }

    /// <summary>
    /// Writes only the changed, validated outputs.
    /// </summary>
    public void Write() => _output.Write();

    private static void AppendViewIndex(StringBuilder rows, List<int> slots)
    {
        slots.Sort();
        if (slots.Count == 0 || slots[^1] == slots.Count - 1)
        {
            rows.Append("    internal static int SlotAt(int index) => index;\n");
            return;
        }

        // Only a catalog with an interior gap needs a mapping. Constant span data lives in the assembly.
        rows.Append("    internal static int SlotAt(int index) => AvailableSlots[index];\n\n")
            .Append("    private static System.ReadOnlySpan<int> AvailableSlots =>\n    [\n");
        foreach (var chunk in slots.Chunk(24))
        {
            rows.Append("        ")
                .AppendJoin(", ", chunk.Select(slot => slot.ToString(CultureInfo.InvariantCulture)))
                .Append(",\n");
        }

        rows.Append("    ];\n");
    }

    private static void AppendDuotoneDurations(StringBuilder rows, IEnumerable<DuotoneSource> icons)
    {
        rows.Append("\n    internal static double GetDuotoneDurationMs(KubaiIconKind kind) => kind switch\n    {\n");
        foreach (var group in icons.GroupBy(icon => icon.Duration).OrderBy(group => group.Key))
        {
            var kinds = group.Select(icon => "KubaiIconKind." + EnumName(icon.Name)).Order(StringComparer.Ordinal);
            rows.Append("        ")
                .AppendJoin(" or\n        ", kinds)
                .Append(CultureInfo.InvariantCulture, $" => {group.Key:R},\n");
        }

        rows.Append("        _ => 0,\n    };\n");
    }

    private static List<Identity> ReadIds(XDocument doc)
    {
        return doc.Descendants("KubaiId")
            .Select(e => new Identity(
                (string?)e.Attribute("Include") ?? throw new InvalidDataException("Missing enum name."),
                (int?)e.Attribute("Value") ?? throw new InvalidDataException("Missing enum value.")))
            .ToList();
    }

    private static void CheckPrefix(IReadOnlyList<Identity> baseline, IReadOnlyList<Identity> current)
    {
        if (current.Count < baseline.Count || baseline.Where((t, i) => current[i] != t).Any())
        {
            throw new InvalidDataException("Existing enum names, values and declaration order must remain an unchanged prefix.");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    internal static string EnumName(string name)
    {
        var result = string.Concat(name.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return char.IsDigit(result[0]) ? "Icon" + result : result;
    }

    private sealed record Identity(string Name, int Value);
}