using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace NotePadClone.ValueConverters;

/// <summary>
/// Converts a file size in bytes to a human readable string and vice versa.
/// The file size must be smaller than 1 TB.
/// </summary>
public class FileSizeConverter : IValueConverter
{
    private const int _kiloByte = 1024;
    private const int _megaByte = 1024 * _kiloByte;
    private const int _gigaByte = 1024 * _megaByte;

    /// <summary>
    /// Converts a file size in bytes to a human readable string.
    /// </summary>
    /// <param name="value">File size in bytes, must be int.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns>The file size with the biggest approriate Byte unit (B, KB, ...).</returns>
    /// <exception cref="ArgumentException">Thrown if value is not of type int.</exception>
    public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is null) return "0 B";

        if (value is not int fileSizeInBytes) throw new ArgumentException("Value must be an integer.");

        switch (fileSizeInBytes)
        {
            case < _kiloByte:
                return $"{fileSizeInBytes} B";

            case < _megaByte:
                return $"{fileSizeInBytes / (double)_kiloByte:0} KB";

            case < _gigaByte:
                return $"{fileSizeInBytes / (double)_megaByte:0} MB";

            default:
                 return $"{fileSizeInBytes / (double)_gigaByte:0} GB";
        }
    }

    /// <summary>
    /// Converts a human readable file size string to an integer.
    /// </summary>
    /// <param name="value">String with file size in a unit of Bytes (B, KB, ...).</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <returns>File size in Bytes as an int.</returns>
    /// <exception cref="ArgumentException">Thrown if value is not a string.</exception>
    /// <exception cref="InvalidOperationException">Thrown if file size is not a positive int or cannot be parsed.</exception>
    public object? ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is null) return null;

        if (value is not string fileSizeString) throw new ArgumentException("Value must be a string.");

        if (!int.TryParse(fileSizeString[..^3], out int fileSize) || fileSize < 0)
        {
            throw new InvalidOperationException("Value must be a positive integer and smaller that 1 TB.");
        }

        if (fileSizeString.EndsWith('B'))
        {
            return fileSize;
        }
        else if (fileSizeString.EndsWith("KB"))
        {
            return fileSize * _kiloByte;
        }
        else if (fileSizeString.EndsWith("MB"))
        {
            return fileSize * _megaByte;
        }
        else if (fileSizeString.EndsWith("GB"))
        {
            return fileSize * _gigaByte;
        }
        else
        {
            throw new InvalidOperationException($"Invalid file size string: {fileSizeString}");
        }
    }
}
