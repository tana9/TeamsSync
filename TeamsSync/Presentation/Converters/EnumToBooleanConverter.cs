using System.Globalization;
using System.Windows.Data;

namespace TeamsSync.Presentation.Converters;

/// <summary>enum値をRadioButton.IsChecked用のboolへ変換する。ConverterParameterに比較対象のenum値を指定する</summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    /// <summary>bindingされたenum値がConverterParameterと一致すればtrueを返す</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null && parameter is not null && value.Equals(parameter);
    }

    /// <summary>チェックされた場合にConverterParameterのenum値を返す。チェック解除時は何もしない</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? parameter : Binding.DoNothing;
    }
}
