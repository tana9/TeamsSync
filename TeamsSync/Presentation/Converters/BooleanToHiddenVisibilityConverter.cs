using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TeamsSync.Presentation.Converters;

/// <summary>
///     boolをVisibilityへ変換する。標準のBooleanToVisibilityConverterと異なりfalseをHiddenへ
///     変換するため、WrapPanel内で表示/非表示を切り替えても占有幅が変わらず、他の要素が
///     折り返し位置を変えて再配置されるのを防げる
/// </summary>
public sealed class BooleanToHiddenVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Hidden;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
