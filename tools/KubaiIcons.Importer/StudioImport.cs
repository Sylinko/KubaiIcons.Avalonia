using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace KubaiIcons.Importer;

/// <summary>
/// Generates browsing metadata for IconStudio only. Never assigns or writes enum identities.
/// </summary>
public sealed class StudioImport
{
    /// <summary>
    /// Repository-relative destination, outside the NuGet library.
    /// </summary>
    public const string OutputPath = "samples/IconStudio/Catalog/catalog.json";

    /// <summary>
    /// Whether committed Studio metadata differs from the validated input.
    /// </summary>
    public bool HasChanges { get; }

    /// <summary>
    /// The number of supported identities represented by the generated catalog.
    /// </summary>
    public int IconCount { get; }

    private readonly string _path;
    private readonly string _content;

    private StudioImport(string root, string content, int count)
    {
        _path = Path.Combine(root, OutputPath);
        _content = content;
        IconCount = count;
        HasChanges = !File.Exists(_path) || File.ReadAllText(_path) != content;
    }

    /// <summary>
    /// Joins fixed-version package metadata to the existing identity ledger and MIT source keywords.
    /// Validation finishes before any output is written.
    /// </summary>
    public static StudioImport Create(string source, string metadata, string root)
    {
        using var pin = Read(Path.Combine(root, "catalog/upstream.json"));
        var version = pin.RootElement.GetProperty("version").GetString() ?? "";
        using var package = Read(Path.Combine(metadata, "package.json"));
        if (package.RootElement.GetProperty("name").GetString() != "kubai-icons" ||
            "kubai-icons@" + package.RootElement.GetProperty("version").GetString() != version)
        {
            throw new InvalidDataException("Studio metadata package does not match the pinned version.");
        }

        using var categories = Read(Path.Combine(source, "icons/categories.json"));
        var categoryRows = categories.RootElement.EnumerateArray()
            .Select(c => new
            {
                id = Required(c, "id"),
                en = Required(c, "en"),
                zh = Required(c, "zh"),
            })
            .ToArray();
        var categoryIds = categoryRows.Select(c => c.id).ToHashSet(StringComparer.Ordinal);
        if (categoryIds.Count != categoryRows.Length)
        {
            throw new InvalidDataException("Duplicate category ID.");
        }

        using var published = Read(Path.Combine(metadata, "duotone-icons.json"));
        var byName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var icon in published.RootElement.EnumerateArray())
        {
            var name = Required(icon, "name");
            if (!byName.TryAdd(name, icon))
            {
                throw new InvalidDataException($"Duplicate Studio icon: {name}");
            }

            if (!categoryIds.Contains(Required(icon, "category")))
            {
                throw new InvalidDataException($"Unknown category for {name}.");
            }
        }

        var ledger = XDocument.Load(Path.Combine(root, "catalog/ids.props"))
            .Descendants("KubaiId")
            .Where(e => (bool?)e.Attribute("Duotone") == true)
            .ToArray();
        var sourceNames = ledger.Select(e => (string?)e.Attribute("Source") ?? "").ToHashSet(StringComparer.Ordinal);
        if (sourceNames.Count != ledger.Length || ledger.Length != pin.RootElement.GetProperty("duotoneCount").GetInt32() ||
            !sourceNames.SetEquals(byName.Keys))
        {
            throw new InvalidDataException("Studio metadata must cover exactly the imported Duotone identities.");
        }

        // The published manifests have tags and categories but no translated names.
        var chineseNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "icons-duotone"), "*.json"))
        {
            using var definition = Read(file);
            chineseNames.Add(Path.GetFileNameWithoutExtension(file), Required(definition.RootElement, "zh"));
        }

        var layeredOnly = XDocument.Load(Path.Combine(root, "catalog/ids.props"))
            .Descendants("KubaiId")
            .Where(e => (bool?)e.Attribute("Layered") == true && (bool?)e.Attribute("Duotone") != true)
            .ToArray();
        if (layeredOnly.Length != 0)
        {
            using var layeredLabels = Read(Path.Combine(root, "catalog/layered-labels.json"));
            using var publishedLayered = Read(Path.Combine(metadata, "layered-icons.json"));
            var layeredRows = publishedLayered.RootElement.EnumerateArray().ToDictionary(e => Required(e, "name"), StringComparer.Ordinal);
            foreach (var entry in layeredOnly)
            {
                var name = (string?)entry.Attribute("Source") ?? "";
                if (!layeredRows.TryGetValue(name, out var row) || !categoryIds.Contains(Required(row, "category")))
                {
                    throw new InvalidDataException($"Missing Layered Studio metadata for {name}.");
                }

                byName.Add(name, row.Clone());
                chineseNames.Add(name, Required(layeredLabels.RootElement, name));
                sourceNames.Add(name);
            }

            ledger = [.. ledger, .. layeredOnly];
        }

        using var keywords = Read(Path.Combine(source, "icons/keywords.json"));
        var rows = ledger.Select(e =>
            {
                var name = (string?)e.Attribute("Source") ?? "";
                var meta = byName[name];
                var baseName = meta.TryGetProperty("base", out var b) ? b.GetString() ?? name : name;
                if (!sourceNames.Contains(baseName))
                {
                    throw new InvalidDataException($"Unknown base icon: {baseName}");
                }

                var words = meta.GetProperty("tags")
                    .EnumerateArray()
                    .Select(t => t.GetString() ?? "")
                    .Where(w => !string.IsNullOrWhiteSpace(w))
                    .ToList();
                if (words.Count == 0)
                {
                    throw new InvalidDataException($"Missing search tags for {name}.");
                }

                if (keywords.RootElement.TryGetProperty(baseName, out var groups))
                {
                    foreach (var group in groups.EnumerateObject())
                    {
                        words.AddRange(group.Value.EnumerateArray().Select(t => t.GetString() ?? ""));
                    }
                }

                return new
                {
                    kind = (string?)e.Attribute("Include") ?? "",
                    name,
                    zh = chineseNames[name],
                    category = Required(meta, "category"),
                    keywords = words.Where(w => !string.IsNullOrWhiteSpace(w))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                };
            })
            .ToArray();
        var content = CatalogJson.WriteMetadata(new
        {
            version,
            commit = pin.RootElement.GetProperty("commit").GetString(),
            categories = categoryRows,
            icons = rows
        });
        return new StudioImport(root, content, rows.Length);
    }

    /// <summary>
    /// Writes the validated Studio catalog only when its content changed.
    /// </summary>
    public void Write()
    {
        if (!HasChanges)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        File.WriteAllText(_path, _content, new UTF8Encoding(false));
    }

    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllText(path));

    private static string Required(JsonElement e, string field)
    {
        var value = e.GetProperty(field).GetString();
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"Missing {field}.") : value;
    }
}