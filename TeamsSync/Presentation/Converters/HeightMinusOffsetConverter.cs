using System.Globalization;
using System.Windows.Data;

namespace TeamsSync.Presentation.Converters;

// ウィンドウ内の上段スクロール領域(手順1〜3)にMaxHeightを動的に与えるためのマルチバインディング用コンバータ。
// 固定オフセットでは、SyncActionBarView(進捗バー・完了時の結果パネル)の実際の高さが
// 状況によって数十pxから結果パネル表示時は200px超まで変動することを考慮できず、
// 同期失敗が複数件あるときに下段の同期差分カードやアクションバーが画面からはみ出るおそれがある。
// values[0]=ContentGridのActualHeight、values[1]=SyncActionBarViewのActualHeightを受け取り、
// そこからConverterParameter(下段カードの最低保証分など、変動しない固定の予約分)を差し引いた
// 残りを上段ScrollViewerの上限として使う。
/// <summary>
///     コンテナの高さからアクションバーの実高さと固定予約分を差し引き、上段スクロール領域の
///     MaxHeightを動的に算出するマルチバインディング用コンバータ。
/// </summary>
public sealed class HeightMinusOffsetConverter : IMultiValueConverter
{
    /// <summary>
    ///     values[0]=コンテナのActualHeight、values[1]=アクションバーのActualHeightから、
    ///     ConverterParameterで指定した固定予約分を差し引いた残り高さ(0以上)を返す。
    /// </summary>
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        double containerHeight = values.Length > 0 && values[0] is double d0 ? d0 : 0;
        double actionBarHeight = values.Length > 1 && values[1] is double d1 ? d1 : 0;
        double reservedHeight = parameter is string s && double.TryParse(s, out double p) ? p : 0;
        return Math.Max(0, containerHeight - actionBarHeight - reservedHeight);
    }

    /// <summary>このコンバータは片方向専用のためサポートしない。</summary>
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}