using Avalonia.Data.Converters;
using Avalonia.Media;
using Nexi.Data.Models;
using Nexi.UI.ViewModels;
using System;
using System.Globalization;

namespace Nexi.UI.Converters
{
    public class ModelStatusToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModelStatus status)
            {
                return status switch
                {
                    ModelStatus.NotDownloaded => new SolidColorBrush(Color.Parse("#666666")),
                    ModelStatus.Downloading => new SolidColorBrush(Color.Parse("#2196F3")),
                    ModelStatus.Downloaded => new SolidColorBrush(Color.Parse("#4CAF50")),
                    ModelStatus.Error => new SolidColorBrush(Color.Parse("#F44336")),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    public class ModelStatusToDownloadVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModelStatus status)
            {
                return status == ModelStatus.NotDownloaded || status == ModelStatus.Error;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    public class ModelStatusToDeleteVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModelStatus status)
            {
                return status == ModelStatus.Downloaded;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    public class StringEqualityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string valueStr && parameter is string paramStr)
            {
                return valueStr == paramStr;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isChecked && isChecked && parameter is string paramStr)
            {
                return paramStr;
            }
            return null;
        }
    }
    
    public class EqualityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value != null && parameter != null)
            {
                return value.Equals(parameter);
            }
            return value == parameter;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isEqual && isEqual && parameter != null)
            {
                return parameter;
            }
            return null;
        }
    }
}