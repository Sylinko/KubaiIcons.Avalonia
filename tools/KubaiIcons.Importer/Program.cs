using System.Diagnostics;
using System.Text.Json;
using KubaiIcons.Importer;

try
{
    if (args.Length == 0 || args[0] is "--help" or "-h")
    {
        Console.WriteLine(
            """
            Usage: dotnet run --project tools/KubaiIcons.Importer -- <command> [options]
              generate | check                 --source <upstream-checkout> [--baseline <ids.props>] [--metadata <package-json-directory>]
              studio-generate | studio-check   --source <upstream-checkout> --metadata <package-json-directory>
              layered-generate | layered-check   [--source <upstream-checkout>, default: artifacts/upstream]
              sampled-generate | sampled-check   aliases for layered-generate | layered-check
            All commands accept --root <repository> (default: current directory).
            """);
        return 0;
    }

    if (args[0] is not ("generate" or "check" or "studio-generate" or "studio-check" or "layered-generate" or "layered-check" or "sampled-generate" or
        "sampled-check"))
    {
        throw new ArgumentException("Unknown import command.");
    }

    var isStudio = args[0].StartsWith("studio-", StringComparison.Ordinal);
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i += 2)
    {
        if (args[i] is not ("--source" or "--root" or "--baseline" or "--metadata") || i + 1 >= args.Length || !options.TryAdd(args[i], args[i + 1]))
        {
            throw new ArgumentException($"Invalid or duplicate option: {args[i]}");
        }
    }

    var root = Path.GetFullPath(options.GetValueOrDefault("--root", "."));
    var isLayered = args[0].StartsWith("layered-", StringComparison.Ordinal) || args[0].StartsWith("sampled-", StringComparison.Ordinal);
    if (!options.TryGetValue("--source", out var source))
    {
        if (!isLayered)
        {
            throw new ArgumentException("--source is required.");
        }

        source = Path.Combine(root, "artifacts/upstream");
    }

    source = Path.GetFullPath(source);
    using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "catalog/upstream.json")));
    var commit = pin.RootElement.GetProperty("commit").GetString();
    if (Git("rev-parse", "HEAD") != commit)
    {
        throw new InvalidDataException("The source checkout does not match catalog/upstream.json.");
    }

    if (Git("status", "--porcelain", "--", "icons-duotone", "LICENSE-ICONS").Length > 0)
    {
        throw new InvalidDataException("Upstream icon definitions or their notice have local modifications.");
    }

    if (isLayered)
    {
        if (Git("status", "--porcelain", "--", "icons", "badges", "packages/core", "package.json").Length > 0)
        {
            throw new InvalidDataException("Layered inputs have local modifications.");
        }

        var layered = LayeredImport.Create(root, source);
        foreach (var file in layered.ChangedFiles)
        {
            Console.WriteLine($"Layered: {file}");
        }

        if (args[0].EndsWith("-generate", StringComparison.Ordinal))
        {
            layered.Write();
        }

        return args[0].EndsWith("-check", StringComparison.Ordinal) && layered.ChangedFiles.Count > 0 ? 1 : 0;
    }

    if (isStudio)
    {
        if (!options.TryGetValue("--metadata", out var metadata))
        {
            throw new ArgumentException("--metadata is required for Studio commands.");
        }

        if (Git("status", "--porcelain", "--", "icons/categories.json", "icons/keywords.json").Length > 0)
        {
            throw new InvalidDataException("Studio source metadata has local modifications.");
        }

        var studio = StudioImport.Create(source, Path.GetFullPath(metadata), root);
        if (studio.HasChanges)
        {
            Console.WriteLine($"Studio catalog: {StudioImport.OutputPath}");
        }

        if (args[0] == "studio-generate")
        {
            studio.Write();
        }

        Console.WriteLine($"{studio.IconCount} Studio icons; {(studio.HasChanges ? 1 : 0)} changed files.");
        return args[0] == "studio-check" && studio.HasChanges ? 1 : 0;
    }

    var plan = CatalogImport.Create(source, root, options.GetValueOrDefault("--baseline"), options.GetValueOrDefault("--metadata"));
    var changes = plan.ChangedFiles;
    foreach (var file in changes)
    {
        Console.WriteLine($"{(args[0] == "check" ? "Outdated" : "Generate")}: {file}");
    }

    if (args[0] == "generate")
    {
        plan.Write();
    }

    Console.WriteLine($"{plan.IconCount} Duotone definitions; {plan.NewIdentityCount} new identities; {changes.Count} changed files.");
    return args[0] == "check" && changes.Count > 0 ? 1 : 0;

    string Git(params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = source,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in arguments)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new IOException("Cannot start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException(error);
        }

        return output.Trim();
    }
}
catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException or JsonException or InvalidOperationException or
                              FormatException or OverflowException or KeyNotFoundException or UnauthorizedAccessException or
                              System.Xml.XmlException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine($"Import failed: {e.Message}");
    return 2;
}