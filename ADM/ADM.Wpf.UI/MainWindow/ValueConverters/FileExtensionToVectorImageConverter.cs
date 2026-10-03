using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ADM.Core.UI;

namespace ADM.Wpf.UI
{
    [ValueConversion(typeof(string), typeof(Geometry))]
    internal class FileExtensionToVectorImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Application.Current.TryFindResource(IconMap.GetVectorNameForFileType(value as string));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
