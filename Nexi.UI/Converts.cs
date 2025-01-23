using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Nexi.UI.Converters
{
    public class MessageBackgroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isUser)
            {
                return isUser ? new SolidColorBrush(Color.Parse("#B388FF")) : new SolidColorBrush(Color.Parse("#424242"));
            }
            return new SolidColorBrush(Color.Parse("#424242"));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MessageAlignmentConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isUser)
            {
                return isUser ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
            }
            return Avalonia.Layout.HorizontalAlignment.Left;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}