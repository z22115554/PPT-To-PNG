using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PptPngExporter.Core.Models;

namespace PptPngExporter.App.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (parameter as string == "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not bool b || !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not bool b || !b;
}

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is string s ? !string.IsNullOrWhiteSpace(s) : value is not null;
        if (parameter as string == "invert") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>依轉檔狀態決定狀態文字的顏色。</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is ExportStatus status ? status switch
        {
            ExportStatus.Success => "SuccessBrush",
            ExportStatus.Failed => "DangerBrush",
            ExportStatus.Running => "AccentBrush",
            ExportStatus.Cancelled => "MutedBrush",
            _ => "MutedBrush"
        } : "MutedBrush";

        return Application.Current?.TryFindResource(key) as Brush
               ?? new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>狀態對應的小圓點顏色（同上，但總是有值）。</summary>
public sealed class StatusToDotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is ExportStatus status ? status switch
        {
            ExportStatus.Success => "SuccessBrush",
            ExportStatus.Failed => "DangerBrush",
            ExportStatus.Running => "AccentBrush",
            ExportStatus.Cancelled => "MutedBrush",
            _ => "TrackBrush"
        } : "TrackBrush";

        return Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
