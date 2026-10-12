using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace GameRadar.App;

/// <summary>
/// For the selection of a ComboBox bound two-way to the view model: the
/// ComboBox may write null into it (its line recycled by a virtualizing list,
/// its items replaced), which the view model never wants. The null stops here,
/// and the view model keeps its choice: no guard to remember in each hook.
/// </summary>
public sealed class KeepChoice : IValueConverter
{
    public static readonly KeepChoice Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value ?? BindingOperations.DoNothing;
}
