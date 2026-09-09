using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NotePadClone.ValueConverters;

/// <summary>
/// Mod addition: inverted Boolean→Visibility converter (true→Collapsed, false→Visible).
/// Used by browse-mode switching: the edit TextBox is hidden when IsBrowseMode=true.
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}