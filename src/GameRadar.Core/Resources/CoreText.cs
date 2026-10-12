using System.Globalization;

namespace GameRadar.Core.Resources;

/// <summary>Fills a translated message of the Core with values, in the current culture.</summary>
internal static class CoreText
{
    public static string Format(string format, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, format, values);
}
