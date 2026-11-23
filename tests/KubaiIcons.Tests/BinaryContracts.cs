using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KubaiIcons.Avalonia;
using KubaiIcons.Importer;
using NUnit.Framework;

namespace KubaiIcons.Tests;

public partial class IconContracts
{
    [Test]
    public async Task CompiledPaths_WhenReplayingSvgCommands_PreservePixelsAndContourLengths()
    {
        await _session.Dispatch(
            () =>
            {
                var paths = new[]
                {
                    "M1 2 h8 v9 h-8 z m14 1 l8 0 0 8 -8 0 z",
                    "M2 20 Q8 1 14 20 T26 20 M2 30 C6 22 8 38 14 30 S22 22 28 30",
                    "M2 8 A6 4 25 0 1 14 8 a6 4 25 1 0 12 0",
                    "M2 2 L30 2 30 30 2 30 Z M8 8 L24 8 24 24 8 24 Z",
                    "M0.123456789123 1.987654321 L19.123456789123 31.123456789 Z",
                    "M-0 0 L12.3456 12.3456 L-1.25 20 Z",
                };
                foreach (var rule in new[] { FillRule.EvenOdd, FillRule.NonZero })
                {
                    foreach (var path in paths)
                    {
                        var bytes = CompilePath(path, rule);
                        var arguments = new object?[] { true, 0d };
                        var compiled = NewCompiledPath(bytes);
                        var actual = (Geometry)Call(
                            compiled.GetType().GetMethod("Build", [typeof(bool), typeof(double).MakeByRefType()])
                            ?? throw new AssertionException("Missing Build."),
                            compiled,
                            arguments);
                        var expected = Geometry.Parse((rule == FillRule.NonZero ? "F1 " : "F0 ") + path);
                        Assert.That(DrawPath(actual), Is.EqualTo(DrawPath(expected)), path + "/" + rule);
                        var parsed = PathGeometry.Parse(path);
                        var length = parsed.Figures?.Sum(figure =>
                            new PathGeometry { Figures = new PathFigures { figure } }.ContourLength) ?? 0;
                        Assert.That(arguments[1], Is.EqualTo(length).Within(0.000001), path);
                        Assert.That(CompilePath(path, rule), Is.EqualTo(bytes));
                    }
                }

                Assert.That(CompilePath(paths[0], FillRule.NonZero)[0], Is.EqualTo(0));
                Assert.That(
                    CompilePath(paths[4], FillRule.NonZero)[0],
                    Is.EqualTo(255),
                    "Coordinates that cannot round-trip exactly must retain Float64 precision.");
                Assert.That(
                    CompilePath(paths[5], FillRule.NonZero)[0],
                    Is.EqualTo(255),
                    "Integer encoding cannot preserve the sign of negative zero.");
            },
            CancellationToken.None);
    }

    [Test]
    public async Task BinaryArtwork_WhenDataIsCorrupt_RejectsInvalidBlocksAndPathCommands()
    {
        await _session.Dispatch(
            () =>
            {
                var assembly = typeof(KubaiIcon).Assembly;
                using var resource = assembly.GetManifestResourceStream("KubaiArt.duotone-folder")
                    ?? throw new AssertionException("Missing Folder.");
                using var buffer = new MemoryStream();
                resource.CopyTo(buffer);
                var valid = buffer.ToArray();
                ReadBlock(valid);
                var version = (byte[])valid.Clone();
                version[3] = 2;
                var kind = (byte[])valid.Clone();
                kind[4] = 1;
                var count = (byte[])valid.Clone();
                count[21] = 255;
                count[22] = 127;
                var nonFinite = (byte[])valid.Clone();
                BitConverter.GetBytes(double.NaN).CopyTo(nonFinite, 5);
                foreach (var bytes in new[] { version, kind, count, nonFinite, valid[..^1], valid.Concat(new byte[] { 0 }).ToArray() })
                {
                    Assert.That(() => ReadBlock(bytes), Throws.InstanceOf<IOException>().Or.InstanceOf<InvalidDataException>());
                }

                var path = CompilePath("M1 2 L3 4 Z", FillRule.NonZero);
                var opcode = (byte[])path.Clone();
                opcode[2] = 255;
                var encoding = (byte[])path.Clone();
                encoding[0] = 7;
                foreach (var bytes in new[] { opcode, encoding, path[..^1] })
                {
                    var compiled = NewCompiledPath(bytes);
                    Assert.That(
                        () => Call(
                            compiled.GetType().GetMethod("Build", Type.EmptyTypes)
                            ?? throw new AssertionException("Missing Build."),
                            compiled,
                            []),
                        Throws.InstanceOf<IOException>().Or.InstanceOf<InvalidDataException>());
                }
            },
            CancellationToken.None);
    }

    private static byte[] CompilePath(string source, FillRule rule)
    {
        var type = typeof(CatalogImport).Assembly.GetType("KubaiIcons.Importer.PathCompiler", true)
            ?? throw new AssertionException("Missing compiler.");
        return (byte[])Call(type.GetMethod("Compile") ?? throw new AssertionException("Missing Compile."), null, [source, rule]);
    }

    private static object NewCompiledPath(byte[] bytes)
    {
        var type = typeof(KubaiIcon).Assembly.GetType("KubaiIcons.Avalonia.Rendering.CompiledPath", true)
            ?? throw new AssertionException("Missing path.");
        return Activator.CreateInstance(type, [bytes]) ?? throw new AssertionException("Missing path instance.");
    }

    private static void ReadBlock(byte[] bytes)
    {
        var assembly = typeof(KubaiIcon).Assembly;
        var readerType = assembly.GetType("KubaiIcons.Avalonia.Rendering.ArtReader", true)
            ?? throw new AssertionException("Missing reader.");
        var kindType = assembly.GetType("KubaiIcons.Avalonia.Rendering.ArtKind", true)
            ?? throw new AssertionException("Missing kind.");
        var dataType = assembly.GetType("KubaiIcons.Avalonia.Rendering.DuotoneData", true)
            ?? throw new AssertionException("Missing data.");
        using var stream = new MemoryStream(bytes, false);
        using var reader = (IDisposable)Call(readerType.GetConstructors().Single(), null, [stream, Enum.ToObject(kindType, 0)]);
        Call(dataType.GetMethod("Read") ?? throw new AssertionException("Missing Read."), null, [reader]);
        Call(readerType.GetMethod("Complete") ?? throw new AssertionException("Missing Complete."), reader, []);
    }

    private static object Call(MethodBase method, object? target, object?[] arguments)
    {
        try
        {
            return (method is ConstructorInfo constructor ? constructor.Invoke(arguments) : method.Invoke(target, arguments)) ?? new object();
        }
        catch (TargetInvocationException e) when (e.InnerException is { } cause)
        {
            ExceptionDispatchInfo.Capture(cause).Throw();
            throw;
        }
    }

    private static byte[] DrawPath(Geometry geometry)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        using (context.PushTransform(Matrix.CreateScale(3, 3) * Matrix.CreateTranslation(12, 12)))
        {
            context.DrawGeometry(Brushes.CornflowerBlue, new Pen(Brushes.DarkSlateBlue, 0.75), geometry);
        }

        var image = new global::Avalonia.Controls.Image
        {
            Source = new DrawingImage(drawing),
            Width = 120,
            Height = 120,
            Stretch = Stretch.None
        };
        image.Measure(new Size(120, 120));
        image.Arrange(new Rect(0, 0, 120, 120));
        using var bitmap = new RenderTargetBitmap(new PixelSize(120, 120));
        bitmap.Render(image);
        var bytes = new byte[120 * 120 * 4];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), bytes.Length, 120 * 4);
        }
        finally
        {
            pin.Free();
        }

        return bytes;
    }
}