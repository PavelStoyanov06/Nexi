using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Globalization;

namespace Nexi.UI.Converters
{
    public class BoolToBackgroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isUser)
            {
                if (Application.Current?.Resources is null) return null;

                if (isUser)
                {
                    // User messages use accent color
                    return Application.Current.Resources["SystemAccentColor"];
                }
                else
                {
                    // Non-user messages use a very dark gray (almost black) for maximum contrast with white text
                    return new SolidColorBrush(Color.Parse("#1A1A1A"));
                }
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToForegroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isUser)
            {
                if (isUser)
                {
                    // User messages always have bright white text
                    return new SolidColorBrush(Color.Parse("#FFFFFF"));
                }
                else
                {
                    // Non-user messages always have bright white text for maximum contrast
                    return new SolidColorBrush(Color.Parse("#FFFFFF"));
                }
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToAlignmentConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isUser)
            {
                return isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            return HorizontalAlignment.Left;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToSystemMessageBackgroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isSystemMessage && isSystemMessage)
            {
                // System messages use a very dark blue for maximum contrast with cyan text
                return new SolidColorBrush(Color.Parse("#0D1B3E"));
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToSystemMessageForegroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isSystemMessage && isSystemMessage)
            {
                // System messages have bright cyan text for maximum visibility
                return new SolidColorBrush(Color.Parse("#00FFFF"));
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}