using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace NotePadClone.ValueConverters;

/// <summary>
/// Converts a number to a string, given an optional format string and culture.
/// </summary>
public class IntToStringConverter : IValueConverter
{
    /// <summary>
    /// Converts a number to a string, given an optional format string and culture. Returns null if the value is null.
    /// </summary>
    /// <param name="value">Int to convert.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Format string to use.</param>
    /// <param name="culture">Culture to use.</param>
    /// <returns>A string of the value formatted according to the format string and culture provided.</returns>
    /// <exception cref="ArgumentException"></exception>
    public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is null)
            return null;

        if (value is not int number) throw new ArgumentException("Value must be a number.");

        if (parameter is not string formatString)
        {
            return number.ToString(culture);
        }

        return number.ToString(formatString, culture);
    }

    /// <summary>
    /// Converts a string back to an int. Returns null if the string is null.
    /// </summary>
    /// <param name="value">String to convert.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns>The int corresponding to the number in the string.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a string.</exception>
    /// <exception cref="InvalidOperationException">Thrown if value cannot be parsed to an int.</exception>
    public object? ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is null) return null;

        if (value is not string numberString) throw new ArgumentException("Value must be a string.");

        if (int.TryParse(numberString, out int number))
        {
            return number;
        }

        throw new InvalidOperationException("Value must be a number.");
    }
}