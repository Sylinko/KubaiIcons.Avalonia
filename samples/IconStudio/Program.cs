using Avalonia;

namespace IconStudio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.CreateBuilder().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}