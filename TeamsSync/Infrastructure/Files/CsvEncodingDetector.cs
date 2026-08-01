using System.Text;

namespace TeamsSync.Infrastructure.Files;

/// <summary>CSVファイルの文字コードをBOMまたは内容から判定する。</summary>
internal static class CsvEncodingDetector
{
    /// <summary>
    /// BOMからCSVファイルの文字コードを判定する。BOMがない場合はUTF-8として妥当かを検証し、
    /// 妥当でなければShift-JIS(コードページ932)にフォールバックする。
    /// </summary>
    public static Encoding Detect(string path)
    {
        // BOMの判定は先頭数バイトだけで足りるため、File.ReadAllBytesでファイル全体を読み込まない
        Span<byte> preamble = stackalloc byte[4];
        int read;
        using (var stream = OpenShared(path))
        {
            read = stream.Read(preamble);
        }

        var header = preamble[..read];
        if (header.StartsWith(Encoding.UTF8.Preamble))
            return new UTF8Encoding(true, true);
        if (header.StartsWith(Encoding.UTF32.Preamble))
            return new UTF32Encoding(false, true, true);
        if (header.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
            return new UTF32Encoding(true, true, true);
        if (header.StartsWith(Encoding.Unicode.Preamble))
            return new UnicodeEncoding(false, true, true);
        if (header.StartsWith(Encoding.BigEndianUnicode.Preamble))
            return new UnicodeEncoding(true, true, true);

        // BOMがない場合はUTF-8として妥当かをStreamReaderでチャンク単位に検証する。
        // byte[]とstringの両方を全体分保持しないため、ReadAllBytes+GetStringよりピークメモリが小さい。
        try
        {
            using var stream = OpenShared(path);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
            var buffer = new char[8192];
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

    // Excelなどが書込み用に開いたまま読み取り共有は許可しているケースを読めるようにするため、
    // File.OpenRead既定のFileShare.ReadWriteへ緩め、他プロセスの読み書きを妨げないようにする。
    private static FileStream OpenShared(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }
}
