using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ScreenUtility.Theming;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool boolean ? !boolean : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool boolean ? !boolean : false;
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool boolean && boolean ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility visibility && visibility != Visibility.Visible;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter) ||
        (parameter is string text && string.Equals(
            System.Convert.ToString(value, CultureInfo.InvariantCulture),
            text,
            StringComparison.Ordinal));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool boolean && boolean ? parameter ?? Binding.DoNothing : Binding.DoNothing;
}

