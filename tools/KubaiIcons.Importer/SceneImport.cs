using System.Text;
using System.Diagnostics;
using System.Text.Json;

namespace KubaiIcons.Importer;

internal static class SceneImport
{
    internal static void Add(string root, string source, Dictionary<string, byte[]> files, SortedDictionary<string, string> labels)
    {
        const string directory = "src/KubaiIcons.Avalonia/Art/Scenes";
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(root, "tools/KubaiIcons.Importer/scene/extract.mjs"));
        start.ArgumentList.Add(source);

        using var process = Process.Start(start) ?? throw new IOException("Cannot start Node.js.");
        var names = new List<string>();
        while (process.StandardOutput.ReadLine() is { } line)
        {
            using var row = JsonDocument.Parse(line);
            var e = row.RootElement;
            var name = $"{directory}/{e[0].GetString()}.kbi";
            files.Add(name, e[2].GetBytesFromBase64());
            names.Add(name);
            var slug = e[0].GetString()!;
            if (slug != "overlay-slash")
            {
                labels.Add(slug, e[1].GetString()!);
            }
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException("Scene compilation failed.");
        }

        files.Add("catalog/layered-bases.json", Encoding.UTF8.GetBytes(CatalogJson.WriteNames(labels.Keys)));
        ImportOutput.CheckInventory(root, directory, names);
    }
}