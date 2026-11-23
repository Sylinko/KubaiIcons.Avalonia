using System.Text.Encodings.Web;
using System.Text.Json;

namespace KubaiIcons.Importer;

internal static class CatalogJson
{
    private static readonly JsonSerializerOptions Names = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions Metadata = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string WriteNames(IEnumerable<string> names) => Write(names, Names);

    public static string WriteMetadata<T>(T value) => Write(value, Metadata);

    private static string Write<T>(T value, JsonSerializerOptions options)
    {
        return JsonSerializer.Serialize(value, options).ReplaceLineEndings("\n") + "\n";
    }
}