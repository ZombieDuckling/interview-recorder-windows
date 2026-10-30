using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Recorder.App.Converters;

internal sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        var hasText = !string.IsNullOrWhiteSpace(text);
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            hasText = !hasText;
        }

        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
