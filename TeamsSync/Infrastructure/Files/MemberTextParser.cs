using System.Security.Cryptography;
using System.Text;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
///     テキストとして貼り付けられた氏名・メールアドレスの一覧を解析し、アドレス一覧へ変換する。
/// </summary>
public sealed class MemberTextParser : IMemberTextParser
{
    public const int MaximumEntries = 5000;
    public const int MaximumTextLength = 500_000;
    public const int MaximumLineLength = 512;

    // ReadOnlySpan<char>.EnumerateLines()はフォームフィード(U+000C)・NEL(U+0085)・LS/PS(U+2028/2029)も
    // 行区切りとして扱うため、これらを使うと制御文字を拒否する下のチェックをすり抜けて
    // 無言で改行扱いになってしまう。意図した区切り(\r\n・\n・\r)だけを使うため、
    // 明示的な区切り文字リストで分割する。
    private static readonly string[] LineSeparators = ["\r\n", "\n", "\r"];

    /// <inheritdoc />
    public MemberListDocument Parse(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length > MaximumTextLength)
        {
            throw new InvalidDataException($"貼り付け入力は{MaximumTextLength:N0}文字までです。");
        }

        List<string> values = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int lineNumber = 0;

        foreach (string lineText in text.Split(LineSeparators, StringSplitOptions.None))
        {
            ReadOnlySpan<char> line = lineText.AsSpan();
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            if (line.Contains('\t'))
            {
                throw new InvalidDataException($"{lineNumber}行目にタブが含まれています。1列だけを貼り付けてください。");
            }

            foreach (char c in line)
            {
                if (char.IsControl(c))
                {
                    throw new InvalidDataException($"{lineNumber}行目に使用できない制御文字が含まれています。");
                }
            }

            if (line.Length > MaximumLineLength)
            {
                throw new InvalidDataException($"{lineNumber}行目は{MaximumLineLength:N0}文字以内で入力してください。");
            }

            string value = ParseLine(line, lineNumber);
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                values.Add(value);
            }
        }

        switch (values.Count)
        {
            case 0:
                throw new InvalidDataException("氏名またはメールアドレスを1行に1件入力してください。");
            case > MaximumEntries:
                throw new InvalidDataException($"貼り付け入力は{MaximumEntries:N0}件までです。");
        }

        string normalizedText = string.Join('\n', values);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText)));
        return new MemberListDocument(values, "貼り付け入力.txt", "", DateTime.Now,
            "テキスト貼り付け", "1行1ユーザー", hash);
    }

    /// <summary>
    ///     貼り付けテキストを1行1件として検証・解析する。タブ・制御文字・行長超過・件数超過は
    ///     <see cref="InvalidDataException" />として拒否する。
    /// </summary>
    public MemberListDocument Parse(string text)
    {
        return Parse(text, CancellationToken.None);
    }

    /// <summary>`表示名 &lt;メールアドレス&gt;`形式の場合は山括弧内だけを識別子として返す。</summary>
    private static string ParseLine(ReadOnlySpan<char> line, int lineNumber)
    {
        DisplayLineFormat format = MemberIdentifierLine.ParseDisplayLine(line, out string identifier);
        return format switch
        {
            DisplayLineFormat.PlainIdentifier or DisplayLineFormat.BracketedIdentifier => identifier,
            DisplayLineFormat.EmptyBracketedIdentifier => throw new InvalidDataException(
                $"{lineNumber}行目の山括弧内にメールアドレスを入力してください。"),
            _ => throw new InvalidDataException(
                $"{lineNumber}行目の表示名とメールアドレスの形式が正しくありません。表示名 <メールアドレス> の形式で入力してください。")
        };
    }
}