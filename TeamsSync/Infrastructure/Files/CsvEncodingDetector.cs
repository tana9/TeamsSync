using System.Text;

namespace TeamsSync.Infrastructure.Files;

/// <summary>CSVファイルの文字コードをBOMまたは内容から判定する</summary>
internal static class CsvEncodingDetector
{
    // StreamReaderでUTF-8として妥当かを検証する際の読み取りバッファサイズ
    private const int DetectionBufferSize = 8192;

    /// <summary>
    ///     BOMからCSVファイルの文字コードを判定する。BOMがない場合はUTF-8として妥当かを検証し、
    ///     妥当でなければShift-JIS(コードページ932)にフォールバックする
    /// </summary>
    public static Encoding Detect(string path)
    {
        // BOMの判定は先頭数バイトだけで足りるため、File.ReadAllBytesでファイル全体を読み込まない
        Span<byte> preamble = stackalloc byte[4];
        int read;
        using (FileStream stream = SharedFileAccess.Open(path))
        {
            read = stream.Read(preamble);
        }

        Span<byte> header = preamble[..read];
        if (header.StartsWith(Encoding.UTF8.Preamble))
        {
            return new UTF8Encoding(true, true);
        }

        if (header.StartsWith(Encoding.UTF32.Preamble))
        {
            return new UTF32Encoding(false, true, true);
        }

        if (header.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            return new UTF32Encoding(true, true, true);
        }

        if (header.StartsWith(Encoding.Unicode.Preamble))
        {
            return new UnicodeEncoding(false, true, true);
        }

        if (header.StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            return new UnicodeEncoding(true, true, true);
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