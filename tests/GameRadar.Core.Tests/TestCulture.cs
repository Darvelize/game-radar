using System.Globalization;
using System.Runtime.CompilerServices;

namespace GameRadar.Core.Tests;

/// <summary>
/// Some tests check French messages (CoreStrings): they run in French
/// whatever the language of the Windows running them.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseFrench()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
    }
}
