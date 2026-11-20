using System.Globalization;
using System.Windows.Data;

namespace TelDiskCleaner.App.Converters;

/// <summary>Maps a 0-100 percentage to a pixel width for the proportional-size bar in the tree view.</summary>
public sealed class PercentToWidthConverter : IValueConverter
{
    private const double MaxWidth = 100.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double percent ? Math.Max(0.0, Math.Min(MaxWidth, percent / 100.0 * MaxWidth)) : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
