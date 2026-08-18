using System.Text;

namespace TeamsSync.Infrastructure.Files;

/// <summary>CSVファイルの文字コード(UTF-8 / UTF-8 BOM付き / Shift-JIS)をBOMまたは内容から判定する</summary>
internal static class CsvEncodingDetector
{
    // StreamReaderでUTF-8として妥当かを検証する際の読み取りバッファサイズ
    private const int DetectionBufferSize = 8192;

    /// <summary>
    ///     UTF-8のBOMがあればUTF-8として扱う。BOMがない場合はUTF-8として妥当かを検証し、
    ///     妥当でなければShift-JIS(コードページ932)にフォールバックする
    /// </summary>
    public static Encoding Detect(string path)
    {
        // BOMの判定は先頭数バイトだけで足りるため、File.ReadAllBytesでファイル全体を読み込まない
        Span<byte> preamble = stackalloc byte[3];
        int read;
        using (FileStream stream = SharedFileAccess.Open(path))
        {
            read = stream.Read(preamble);
        }

        if (preamble[..read].StartsWith(Encoding.UTF8.Preamble))
        {
            return new UTF8Encoding(true, true);
        }

        // BOMがない場合はUTF-8として妥当かをStreamReaderでチャンク単位に検証する。
        // byte[]とstringの両方を全体分保持しないため、ReadAllBytes+GetStringよりピークメモリが小さい
        try
        {
            using FileStream stream = SharedFileAccess.Open(path);
            using StreamReader reader = new(stream, new UTF8Encoding(false, true), false);
            char[] buffer = new char[DetectionBufferSize];
            while (reader.Read(buffer, 0, buffer.Length) > 0)
            {
            }

            return new UTF8Encoding(false, true);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
    }
}