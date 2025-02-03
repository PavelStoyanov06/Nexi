using Avalonia.Data.Converters;
using Avalonia.Media;
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
}