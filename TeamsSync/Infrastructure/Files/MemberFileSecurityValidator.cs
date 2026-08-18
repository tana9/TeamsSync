using System.Security.Cryptography;

namespace TeamsSync.Infrastructure.Files;

/// <summary>メンバーリストファイルの読込に関するファイルサイズ上限検証と内容ハッシュ計算を担当する</summary>
internal static class MemberFileSecurityValidator
{
    // 想定外に大きい／壊れたファイルを早期に拒否するための上限。
    // ファイルサイズはテキスト貼り付け(MemberTextParser.MaximumTextLength=500,000文字)より大きくてよいが、
    // 無制限だとFile.ReadAllBytes等で容易にメモリを枯渇させられるため常識的な値に制限する
    public const long MaximumFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>ファイルサイズが上限内であることを検証する</summary>
    public static void EnsureFileSizeWithinLimit(long lengthBytes)
    {
        if (lengthBytes > MaximumFileSizeBytes)
        {
            throw new InvalidDataException(
                $"ファイルサイズは{MaximumFileSizeBytes / 1024 / 1024:N0}MBまでです（{lengthBytes / 1024 / 1024.0:N1}MB）。");
        }
    }

    /// <summary>ファイル内容のSHA-256ハッシュを16進文字列で計算する</summary>
    public static string ComputeSha256(Stream stream)
    {
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>バイト列のSHA-256ハッシュを16進文字列で計算する</summary>
    public static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}