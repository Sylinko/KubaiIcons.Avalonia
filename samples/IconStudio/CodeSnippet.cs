using System.Globalization;
using KubaiIcons.Avalonia;

namespace IconStudio;

/// <summary>
/// Copyable examples derived from the same settings that drive the preview.
/// </summary>
public static class CodeSnippet
{
    public static string Create(KubaiIcon icon, bool isCSharp, bool isHeld) => Create(icon, isCSharp, isHeld, null);

    internal static string Create(KubaiIcon icon, bool isCSharp, bool isHeld, double? exportSize)
    {
        var control = icon.IconStyle == KubaiIconStyle.Layered ? "KubaiLayeredIcon" : "KubaiDuotoneIcon";
        var values = new List<(string Name, string Axaml, string CSharp)>
        {
            ("Kind", icon.Kind.ToString(), $"{control}Kind.{icon.Kind}"),
        };

        void Add(string name, string axaml, string? csharp = null) => values.Add((name, axaml, csharp ?? axaml));
        // An explicit slider value keeps the same line even when it crosses the library default.
        if (exportSize.HasValue || !icon.Size.Equals(24d))
        {
            Add("Size", (exportSize ?? icon.Size).ToString("0.##", CultureInfo.InvariantCulture));
        }

        if (icon.AccentColor is { } color)
        {
            var hex = color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : color.ToString();
            Add("AccentColor", hex, $"Color.Parse(\"{hex}\")");
        }

        if (icon.ColorVariant != KubaiColorVariant.Color)
        {
            Add("ColorVariant", icon.ColorVariant.ToString(), $"KubaiColorVariant.{icon.ColorVariant}");
        }

        if (!icon.IsAnimationEnabled)
        {
            Add("IsAnimationEnabled", "False", "false");
        }
        // Hover is transient preview feedback, not an exported held-pose request.
        if (isHeld)
        {
            Add("IsEngaged", "True", "true");
        }

        if (icon.IsActive)
        {
            Add("IsActive", "True", "true");
        }

        if (icon.LoopMode != LoopMode.Once)
        {
            Add("LoopMode", icon.LoopMode.ToString(), $"LoopMode.{icon.LoopMode}");
        }

        if (!icon.SpeedRatio.Equals(1d))
        {
            Add("SpeedRatio", icon.SpeedRatio.ToString("0.##", CultureInfo.InvariantCulture));
        }

        if (icon.RepeatDelay > TimeSpan.Zero)
        {
            Add(
                "RepeatDelay",
                icon.RepeatDelay.ToString("c", CultureInfo.InvariantCulture),
                $"TimeSpan.FromMilliseconds({icon.RepeatDelay.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture)})");
        }

        if (icon.IsPlaying)
        {
            Add("IsPlaying", "True", "true");
        }

        if (isCSharp)
        {
            return (icon.RepeatDelay > TimeSpan.Zero ? "using System;\n" : "") +
                "using Avalonia.Media;\nusing KubaiIcons.Avalonia;\n\nvar icon = new " + control + "\n{\n" +
                string.Join("\n", values.Select(v => $"    {v.Name} = {v.CSharp},")) + "\n};";
        }

        return "<i:" + control + " xmlns:i=\"using:KubaiIcons.Avalonia\"\n" +
            string.Join("\n", values.Select(v => $"             {v.Name}=\"{v.Axaml}\"")) + "/>";
    }
}