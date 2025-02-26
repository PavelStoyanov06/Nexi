using Avalonia.Data.Converters;
using Material.Icons;
using System;
using System.Globalization;

namespace Nexi.UI.Converters
{
    public class BoolToVisibilityIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool showPassword)
            {
                return showPassword ? MaterialIconKind.VisibilityOff : MaterialIconKind.Visibility;
            }
            return MaterialIconKind.Visibility;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToErrorBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isError)
            {
                return isError ?
                    Avalonia.Media.Brushes.Red :
                    Avalonia.Media.Brushes.Green;
            }
            return Avalonia.Media.Brushes.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}