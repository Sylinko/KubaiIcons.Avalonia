using System.Text.Json;
using NUnit.Framework;

namespace KubaiIcons.Tests;

// A snapshot of the published npm manifests, not the generated Avalonia ID ledger.
internal sealed record PublishedInventory(
    string[] Bases,
    string[] Compositions,
    string[] Duotone,
    string[] Badges,
    bool HasSlash,
    string[] Categories)
{
    public static PublishedInventory Current { get; } = Load();

    public IEnumerable<string> Layered => Bases.Concat(Compositions);

    private static PublishedInventory Load()
    {
        var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Contracts/published-inventory.json");
        return JsonSerializer.Deserialize<PublishedInventory>(File.ReadAllText(file), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Missing published inventory.");
    }
}
