using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Nexi.UI.Converters
{
    public class PasswordCharConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // If ShowPassword is true, return null (show password)
            // If ShowPassword is false, return • (hide password)
            if (value is bool showPassword)
            {
                return showPassword ? null : '•';
            }

            // Default: hide password
            return '•';
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}