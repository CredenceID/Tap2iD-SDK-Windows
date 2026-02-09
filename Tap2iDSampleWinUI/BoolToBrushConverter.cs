using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace Tap2iDSampleWinUI
{
    public class BoolToBrushConverter : IValueConverter
    {
        // Reusable brushes to avoid memory allocation on every scroll
        private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(Colors.Green);
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(Colors.Red);

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isValid && isValid)
            {
                return GreenBrush;
            }
            return RedBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
