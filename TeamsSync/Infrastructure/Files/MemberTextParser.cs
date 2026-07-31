using System.Security.Cryptography;
using System.Text;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
/// テキストとして貼り付けられた氏名・メールアドレスの一覧を解析し、アドレス一覧へ変換する。
/// </summary>
public sealed class MemberTextParser : IMemberTextParser
{
    public const int MaximumEntries = 5000;
    public const int MaximumTextLength = 500_000;
    public const int MaximumLineLength = 512;

    /// <summary>
    /// 貼り付けテキストを1行1件として検証・解析する。タブ・制御文字・行長超過・件数超過は
    /// <see cref="InvalidDataException"/>として拒否する。
    /// </summary>
    public MemberListDocument Parse(string text)
    {
        if (text.Length > MaximumTextLength)
            throw new InvalidDataException($"貼り付け入力は{MaximumTextLength:N0}文字までです。");

        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Contains('\t'))
                throw new InvalidDataException($"{index + 1}行目にタブが含まれています。1列だけを貼り付けてください。");
            if (line.Any(char.IsControl))
                throw new InvalidDataException($"{index + 1}行目に使用できない制御文字が含まれています。");
            if (line.Length > MaximumLineLength)
                throw new InvalidDataException($"{index + 1}行目は{MaximumLineLength:N0}文字以内で入力してください。");
        }

        var values = lines.Select((line, index) => ParseLine(line, index + 1))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (values.Count == 0)
            throw new InvalidDataException("氏名またはメールアドレスを1行に1件入力してください。");
        if (values.Count > MaximumEntries)
            throw new InvalidDataException($"貼り付け入力は{MaximumEntries:N0}件までです。");

        var normalizedText = string.Join('\n', values);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText)));
        return new MemberListDocument(values, "貼り付け入力.txt", "", DateTime.Now,
            "テキスト貼り付け", "1行1ユーザー", hash);
    }

    /// <summary>`表示名 &lt;メールアドレス&gt;`形式の場合は山括弧内だけを識別子として返す。</summary>
    private static string ParseLine(string line, int lineNumber)
    {
        var value = line.Trim();
        if (value.Length == 0) return "";
        var containsBracket = value.Contains('<') || value.Contains('>');
        if (!containsBracket) return value;

        var open = value.LastIndexOf('<');
        if (open <= 0 || !value.EndsWith('>') || value.IndexOf('<') != open ||
            value.IndexOf('>') != value.Length - 1)
            throw new InvalidDataException(
                $"{lineNumber}行目の表示名とメールアドレスの形式が正しくありません。表示名 <メールアドレス> の形式で入力してください。");

        var address = value[(open + 1)..^1].Trim();
        if (address.Length == 0)
            throw new InvalidDataException($"{lineNumber}行目の山括弧内にメールアドレスを入力してください。");
        return address;
    }
}
