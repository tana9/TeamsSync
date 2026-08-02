namespace TeamsSync.Domain.Teams;

/// <summary>`ParseDisplayLine`が入力行を分類した結果の種別。</summary>
public enum DisplayLineFormat
{
    /// <summary>山括弧を含まない、そのまま識別子として使える行。</summary>
    PlainIdentifier,

    /// <summary>`表示名 &lt;識別子&gt;`形式で、山括弧内を識別子として抽出できた。</summary>
    BracketedIdentifier,

    /// <summary>山括弧の対応が崩れている、または開始位置が不正。</summary>
    MalformedBrackets,

    /// <summary>山括弧内が空。</summary>
    EmptyBracketedIdentifier
}

/// <summary>
///     「1行1ユーザー」の貼り付け入力欄で使う`表示名 &lt;メールアドレス&gt;`形式の、
///     組み立て(<see cref="FormatDisplayLine" />)と解析(<see cref="ParseDisplayLine" />)を
///     1箇所にまとめる。往復(現メンバーを取り込んでテキスト化し、それを再び貼り付けて解析する)で
///     解釈がずれないよう、両方向を同じ規則で扱う。
/// </summary>
public static class MemberIdentifierLine
{
    /// <summary>
    ///     表示名とメールアドレスから`表示名 &lt;メールアドレス&gt;`形式の行を組み立てる。
    ///     表示名中の制御文字と`&lt;`・`&gt;`は、<see cref="ParseDisplayLine" />で再解析したときに
    ///     山括弧の対応が崩れないよう空白へ置き換える。表示名が空になった場合はメールアドレスのみを返す。
    /// </summary>
    public static string FormatDisplayLine(string displayName, string email)
    {
        string sanitizedName = new string(displayName
            .Select(character => char.IsControl(character) || character is '<' or '>' ? ' ' : character)
            .ToArray()).Trim();
        string trimmedEmail = email.Trim();
        return string.IsNullOrWhiteSpace(sanitizedName) ? trimmedEmail : $"{sanitizedName} <{trimmedEmail}>";
    }

    /// <summary>
    ///     入力行を解析し、`表示名 &lt;識別子&gt;`形式であれば山括弧内だけを、山括弧を含まなければ
    ///     行全体をそのまま識別子として返す。山括弧の対応が崩れている場合や、山括弧内が空の場合は
    ///     <paramref name="identifier" />を空文字にし、その旨を戻り値の種別で表す。
    /// </summary>
    public static DisplayLineFormat ParseDisplayLine(ReadOnlySpan<char> line, out string identifier)
    {
        ReadOnlySpan<char> trimmed = line.Trim();
        if (trimmed.IsEmpty)
        {
            identifier = "";
            return DisplayLineFormat.PlainIdentifier;
        }

        int lastOpen = trimmed.LastIndexOf('<');
        if (lastOpen < 0 && !trimmed.Contains('>'))
        {
            identifier = trimmed.ToString();
            return DisplayLineFormat.PlainIdentifier;
        }

        if (lastOpen <= 0 || !trimmed.EndsWith(">") || trimmed.IndexOf('<') != lastOpen ||
            trimmed.IndexOf('>') != trimmed.Length - 1)
        {
            identifier = "";
            return DisplayLineFormat.MalformedBrackets;
        }

        ReadOnlySpan<char> address = trimmed.Slice(lastOpen + 1, trimmed.Length - lastOpen - 2).Trim();
        if (address.IsEmpty)
        {
            identifier = "";
            return DisplayLineFormat.EmptyBracketedIdentifier;
        }

        identifier = address.ToString();
        return DisplayLineFormat.BracketedIdentifier;
    }
}
