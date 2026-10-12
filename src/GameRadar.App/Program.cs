using Avalonia;

namespace GameRadar.App;

internal static class Program
{
    // Entry point. Avalonia is not initialized yet here, so no UI type
    // or SynchronizationContext-dependent code must run before Start...().
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the XAML previewer, which is why it is a separate public method.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
