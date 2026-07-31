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

        var values = lines.Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
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
}