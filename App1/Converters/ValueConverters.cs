using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;
using App1.Helpers;

namespace App1.Converters
{
    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool flag = value is bool && (bool)value;
            if (Invert)
            {
                flag = !flag;
            }

            var text = parameter as string;
            if (!string.IsNullOrEmpty(text) && text.Equals("invert", StringComparison.OrdinalIgnoreCase))
            {
                flag = !flag;
            }

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var text = value as string;
            return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class UrlToImageConverter : IValueConverter
    {
        public int DecodeWidth { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var url = ImageUrl.Normalize(value as string, DecodeWidth > 0 ? DecodeWidth : 88);
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            try
            {
                return ImageCache.Get(url, DecodeWidth > 0 ? DecodeWidth : 88);
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class WidthToHeightConverter : IValueConverter
    {
        public double Factor { get; set; }
        public double Fallback { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var width = value is double ? (double)value : 0;
            if (width <= 0)
            {
                return Fallback > 0 ? Fallback : 202.0;
            }

            var factor = Factor;
            if (parameter != null)
            {
                double parsed;
                if (double.TryParse(parameter.ToString(), out parsed))
                {
                    factor = parsed;
                }
            }

            return width * factor;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
