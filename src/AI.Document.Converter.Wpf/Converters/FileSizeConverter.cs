using System.Globalization;
using System.Windows.Data;

namespace AI.Document.Converter.Wpf.Converters;

// UI-only formatting (docs/08-APPLICATION-ARCHITECTURE.md Section 3's own example:
// bytes -> "2.3 MB") - never used outside a XAML binding.
public sealed class FileSizeConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
        {
            return string.Empty;
        }

        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < Units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {Units[unitIndex]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
