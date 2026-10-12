using Avalonia;
using Avalonia.Headless;
using GameRadar.App.Tests;

// Runs every [AvaloniaFact] / [AvaloniaTheory] on a headless Avalonia
// instance built by the class below.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace GameRadar.App.Tests;

public static class TestAppBuilder
{
    // The real App class, so tests see the real theme and styles. Its
    // startup code only opens the main window for a desktop lifetime,
    // which the headless platform does not have: no network call here.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            // Skia draws real pixels; the default headless drawing is a
            // no-op, fine for logic tests but useless for screenshots.
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
