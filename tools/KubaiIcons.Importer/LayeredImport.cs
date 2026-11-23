using System.Text;
using System.Diagnostics;
using System.Text.Json;

namespace KubaiIcons.Importer;

internal static class LayeredImport
{
    public static ImportOutput Create(string root, string source)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var labels = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SceneImport.Add(root, source, files, labels);
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(root, "tools/KubaiIcons.Importer/extract-layered.mjs"));
        start.ArgumentList.Add(source);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start Node.js.");
        var json = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException("Upstream Layered composition extraction failed.");
        }

        using var doc = JsonDocument.Parse(json);
        BadgeImport.Add(root, doc.RootElement, files, labels);
        using var pin = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "catalog/upstream.json")));
        if (labels.Count != pin.RootElement.GetProperty("layeredCount").GetInt32())
        {
            throw new InvalidDataException("Compiled Layered identity count differs from the pinned catalog.");
        }

        files.Add("catalog/layered-labels.json", Encoding.UTF8.GetBytes(CatalogJson.WriteMetadata(labels)));
        return new ImportOutput(root, files);
    }
}