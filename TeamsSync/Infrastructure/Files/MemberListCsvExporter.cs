using System.Globalization;
using System.Text;

using CsvHelper;

using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Files;

/// <summary>チームメンバー一覧を、表示名・メールアドレス・役割の列を持つCSVとして書き出す</summary>
public sealed class MemberListCsvExporter : IMemberListExporter
{
    private static readonly string[] HeaderColumns = ["表示名", "メールアドレス", "役割"];

    /// <inheritdoc />
    public void Export(IReadOnlyList<TeamMember> members, string path)
    {
        // 保存先はユーザーが名前を付けて保存ダイアログで選んだファイルなので、既存のダイアログの
        // 上書き確認に委ね、ここではAtomicFileWriter側の上書き可否をtrueにする
        AtomicFileWriter.Write(path, stream => WriteCsv(stream, members), true);
    }

    /// <inheritdoc />
    public string SanitizeForFileName(string value)
    {
        return FileNameSanitizer.Sanitize(value, "チーム");
    }

    private static void WriteCsv(Stream stream, IReadOnlyList<TeamMember> members)
    {
        using StreamWriter writer = new(stream, new UTF8Encoding(true), leaveOpen: true);
        using CsvWriter csv = new(writer, CultureInfo.InvariantCulture, leaveOpen: true);

        CsvRowWriter.WriteRow(csv, HeaderColumns, quoted: false);
        foreach (TeamMember member in members.OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            string role = member.IsOwner ? "所有者" : "メンバー";
            CsvRowWriter.WriteRow(csv, [member.DisplayName, member.Email, role], quoted: true);
        }

        csv.Flush();
    }
}
