using System.Text;

namespace TeamsSync.Domain.Teams;

/// <summary>
///     氏名による同一人物判定のための正規化・比較ユーティリティ
/// </summary>
public static class UserIdentifier
{
    /// <summary>
    ///     全角/半角の表記揺れを吸収するためNFKC正規化を行い、空白文字を除去した文字列を返す
    /// </summary>
    public static string NormalizeName(string value)
    {
        return string.Concat(
            value.Normalize(NormalizationForm.FormKC).Where(character => !char.IsWhiteSpace(character)));
    }

    /// <summary>
    ///     正規化したうえで大文字小文字を区別せずに2つの氏名を比較する
    /// </summary>
    public static bool NameEquals(string left, string right)
    {
        return string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);
    }
}