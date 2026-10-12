using System.Globalization;
using System.Runtime.CompilerServices;

namespace GameRadar.App.Tests;

/// <summary>
/// The tests check French texts: they run in French whatever the language
/// of the Windows running them (a developer's English Windows, a CI server).
/// English is checked where a test switches the culture on purpose.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseFrench()
    {
        // Default of every thread that has not chosen its own culture,
        // including the Avalonia UI thread of the headless tests.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
    }

    /// <summary>Runs <paramref name="action"/> in another language, then comes back.</summary>
    public static void In(string culture, Action action)
    {
        var (previous, previousUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            action();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (previous, previousUi);
        }
    }
}
