namespace KubaiIcons.Importer;

// Byte comparison is the check contract for text and binary output alike. Nothing is
// written until the whole plan has been validated; checks never touch timestamps.
internal sealed class ImportOutput
{
    public IReadOnlyList<string> ChangedFiles { get; }
    private readonly string _root;
    private readonly Dictionary<string, byte[]> _files;

    public ImportOutput(string root, Dictionary<string, byte[]> files)
    {
        _root = root;
        _files = files;
        ChangedFiles = files.Where(pair => !File.Exists(Path.Combine(root, pair.Key)) ||
                !File.ReadAllBytes(Path.Combine(root, pair.Key)).AsSpan().SequenceEqual(pair.Value))
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public void Write()
    {
        foreach (var name in ChangedFiles)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _root);
            File.WriteAllBytes(path, _files[name]);
        }
    }

    public static void CheckInventory(string root, string directory, IEnumerable<string> expected)
    {
        var full = Path.Combine(root, directory);
        if (!Directory.Exists(full))
        {
            return;
        }

        var names = expected.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(full))
        {
            if (!names.Contains(Path.GetFileName(file)))
            {
                throw new InvalidDataException("Unexpected generated resource: " + file);
            }
        }
    }
}