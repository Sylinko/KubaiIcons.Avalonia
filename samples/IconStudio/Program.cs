using Avalonia;

namespace IconStudio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.CreateBuilder()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { UseCompositionSwapChain = true })
            .StartWithClassicDesktopLifetime(args);
    }
}