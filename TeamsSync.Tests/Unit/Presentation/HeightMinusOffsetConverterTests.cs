using System.Globalization;

using TeamsSync.Presentation.Converters;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class HeightMinusOffsetConverterTests
{
    private readonly HeightMinusOffsetConverter _converter = new();

    [Fact]
    public void Convert_コンテナ高さからアクションバー高さと予約分を差し引いた値を返す()
    {
        object result = _converter.Convert([300d, 50d], typeof(double), "20", CultureInfo.InvariantCulture);

        Assert.Equal(230d, result);
    }

    [Fact]
    public void Convert_結果が負になる場合は0にクランプする()
    {
        object result = _converter.Convert([10d, 50d], typeof(double), "20", CultureInfo.InvariantCulture);

        Assert.Equal(0d, result);
    }

    [Fact]
    public void Convert_valuesが空配列の場合は0として扱う()
    {
        object result = _converter.Convert([], typeof(double), null, CultureInfo.InvariantCulture);

        Assert.Equal(0d, result);
    }

    [Fact]
    public void Convert_要素がdouble型でない場合は0として扱う()
    {
        object result = _converter.Convert(["not-a-double", "also-not-a-double"], typeof(double), "10",
            CultureInfo.InvariantCulture);

        Assert.Equal(0d, result);
    }

    [Fact]
    public void Convert_parameterがnullの場合は予約分を0として扱う()
    {
        object result = _converter.Convert([300d, 50d], typeof(double), null, CultureInfo.InvariantCulture);

        Assert.Equal(250d, result);
    }

    [Fact]
    public void Convert_parameterが数値に変換できない場合は予約分を0として扱う()
    {
        object result = _converter.Convert([300d, 50d], typeof(double), "非数値", CultureInfo.InvariantCulture);

        Assert.Equal(250d, result);
    }

    [Fact]
    public void ConvertBack_呼び出すとNotSupportedExceptionを投げる()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(0d, [typeof(double)], null, CultureInfo.InvariantCulture));
    }
}